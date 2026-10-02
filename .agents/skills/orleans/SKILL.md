---
name: orleans
description: Port and maintain WildCat's Orleans backend. Use when writing or reviewing grains, silo or client hosting, persistence, streams, timers, reminders, grain tests, or the Orleans 3.4.3 to 10 migration.
---

# Orleans 10 for this repository

Written from the official docs at learn.microsoft.com/dotnet/orleans on 2026-09-30. Every page
used is listed with its own date in `reference/sources.md`. Where a statement is inferred rather
than documented it says so.

The original research recorded Orleans 10.3.1 (2026-08-28) as the stable release and .NET 8, 9
and 10 as its targets. Treat version and API claims here as a 2026-09-30 research snapshot;
check the selected packages and official documentation before applying them. The machine this
repo was set up on had the .NET 10.0.401 SDK. The merged backend now targets Orleans 10.3.1
on .NET 10. WildCat.Protocol targets netstandard2.1 with C# 9 so Unity 6 can compile the same
source. `reference/migration-3x.md` preserves the historical Orleans 3.4.3/net5.0 migration
checklist; that migration has already been completed.

Read [AGENTS.md](../../../AGENTS.md) for the confirmed project choices. WildCat targets Orleans
10 on .NET 10 with PostgreSQL through the official ADO.NET providers for clustering, grain
state and reminders, and memory providers in Debug and tests. The other providers below and in
the references are background research, not alternatives to reopen unless the user changes the
decision. There is no legacy data to migrate. Read only the reference files needed for the task.

## The ten rules that catch most mistakes

1. Every type that crosses a grain call or goes into grain state carries `[GenerateSerializer]`
   and every serialized member carries `[Id(n)]`, with ids starting at 0 per class level. Give
   types an `[Alias("name")]` so they can be renamed later. `[Serializable]` alone does nothing.
   Custom exceptions thrown from grains need the same treatment. `reference/serialization.md`.
2. Grains are single-threaded and non-reentrant by default. A call cycle (A calls B calls A)
   deadlocks until the 30 second response timeout. Break cycles with `[ReadOnly]`,
   `[AlwaysInterleave]`, `RequestContext.AllowCallChainReentrancy()`, or by not making the cycle.
   `reference/scheduling.md`.
3. Never block inside a grain: no `.Wait()`, `.Result`, `GetAwaiter().GetResult()`, and never
   `ConfigureAwait(false)` in grain code. `Task.Run` escapes the grain scheduler; use
   `Task.Factory.StartNew(...).Unwrap()` to stay on it. `async void` is banned.
4. State: inject `IPersistentState<T>` with `[PersistentState("name", "provider")]`, mutate
   `State`, then `await WriteStateAsync()`. State is loaded before `OnActivateAsync`, not in the
   constructor. `Grain<T>` plus `[StorageProvider]` still works but is the legacy path.
   `reference/persistence.md`.
5. Streams: the old SimpleMessageStream ("SMS") is gone. Use `AddBroadcastChannel` for
   fire-and-forget fan-out with implicit subscribers, or `AddMemoryStreams` (needs a
   `PubSubStore` grain storage) when you need explicit subscriptions, clients as subscribers, or
   the same API as durable providers. Stream ids are `StreamId.Create(ns, key)`.
   `reference/messaging.md`.
6. Push to a connected client: an `IGrainObserver` registered with
   `CreateObjectReference` (best effort, must be re-subscribed on a timer, always delete the
   reference), or a client-side stream subscription. Observers are one-way; never build
   correctness on them.
7. Timers vs reminders: `this.RegisterGrainTimer(...)` for sub-minute, activation-scoped work
   (callbacks do not interleave unless `Interleave = true`, and do not keep the grain alive
   unless `KeepAlive = true`). Reminders (`IRemindable` + `RegisterOrUpdateReminder`) are durable,
   minute-scale or slower, and need a reminder provider (`UseInMemoryReminderService` only in
   dev). Reminder and stream helpers are extension methods in 7+, so write `this.GetReminders()`.
8. Hosting: `Host.CreateApplicationBuilder(args).UseOrleans(silo => ...)` for silos and
   `UseOrleansClient(client => ...)` for external clients. A silo already registers
   `IClusterClient` and `IGrainFactory`, so a co-hosted ASP.NET Core gateway needs no separate
   client. Call `UseOrleans` before the web host so Orleans starts first. `reference/hosting.md`.
9. Identity: grain type name is the class name minus "Grain", lowercased, unless `[GrainType]`
   says otherwise. Keys are strings underneath; Guid, long, string and compound keys are all
   supported. Use a fixed key such as `"default"` for singletons. `[StatelessWorker]` grains are
   not addressable and cannot subscribe to streams.
10. Tests: `InProcessTestClusterBuilder` with `ConfigureSilo((options, silo) => ...)` shared
    through an xUnit collection fixture. Register memory storage named `Default` and
    `PubSubStore` in the fixture. `reference/testing.md`.

## Provider reference from the original research

| Concern | Dev / tests | Production candidates (all official unless noted) |
|---|---|---|
| Clustering | `UseLocalhostClustering()` | ADO.NET (PostgreSQL via `Npgsql`), Redis, Azure Table, Cosmos, Kubernetes hosting + one of these |
| Grain storage | `AddMemoryGrainStorage("Default")` | ADO.NET, Redis, Azure Blob/Table, Cosmos, DynamoDB; MongoDB via the contrib `Orleans.Providers.MongoDB` 9.5.0 (built for Orleans 9, untested on 10) |
| Reminders | `UseInMemoryReminderService()` | ADO.NET, Redis, Azure Table, Cosmos |
| Pub/sub | `AddBroadcastChannel` or `AddMemoryStreams` + `AddMemoryGrainStorage("PubSubStore")` | Same in-memory options are acceptable for transient game events; Azure Queue or Event Hubs when durability is required |
| Transactions | `UseTransactions()` + memory storage bridge | `AddAzureTableTransactionalStateStorage`; DynamoDB transactional storage arrived in 10.3 |

The legacy backend used MongoDB for all three system stores plus SMS streams. The original
research recorded the contrib MongoDB package at 9.5.0 (2026-01-12, depending on
`Microsoft.Orleans.Reminders` >= 9.0.1), with Orleans 10 compatibility unverified. WildCat's
confirmed replacement is PostgreSQL through the official ADO.NET packages for clustering,
persistence and reminders. The reference retains the provider survey for context.

## Recipes

Silo with a co-hosted WebSocket gateway (the shape used by this repository):

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();                       // dev only
    silo.AddMemoryGrainStorage("Default");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("game");                       // or AddBroadcastChannel("game-events")
    silo.UseInMemoryReminderService();
    silo.AddDashboard();                                 // Microsoft.Orleans.Dashboard, preview
});
var app = builder.Build();
app.UseWebSockets();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.Run();
```

Grain with named state, a timer and a reminder:

```csharp
public interface IPlayerGrain : IGrainWithStringKey
{
    Task<PlayerProfile> GetProfile(CancellationToken ct = default);
    Task SetName(string name, CancellationToken ct = default);
    [ReadOnly] Task<int> GetScore();
}

[GenerateSerializer, Alias("wildcat.player.state")]
public sealed class PlayerState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Score { get; set; }
}

public sealed class PlayerGrain(
    [PersistentState("profile", "Default")] IPersistentState<PlayerState> state,
    ILogger<PlayerGrain> log) : Grain, IPlayerGrain, IRemindable
{
    public override Task OnActivateAsync(CancellationToken ct)
    {
        this.RegisterGrainTimer(static (g, ct) => g.Tick(ct), this,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromSeconds(5), Period = TimeSpan.FromSeconds(30) });
        return Task.CompletedTask;
    }

    public Task<PlayerProfile> GetProfile(CancellationToken ct) =>
        Task.FromResult(new PlayerProfile(this.GetPrimaryKeyString(), state.State.Name));

    public async Task SetName(string name, CancellationToken ct)
    {
        state.State.Name = name;
        await state.WriteStateAsync();
    }

    public Task<int> GetScore() => Task.FromResult(state.State.Score);

    private Task Tick(CancellationToken ct) => Task.CompletedTask;

    public Task ReceiveReminder(string name, TickStatus status) => Task.CompletedTask;
}
```

## Reference files

- `reference/hosting.md`: silo and client configuration, clustering providers, endpoints, options
  classes, startup work, dashboard, Aspire, Kubernetes, shutdown, OpenTelemetry.
- `reference/grains.md`: interfaces, keys, references, lifecycle, activation collection,
  placement, stateless workers, call filters, request context, cancellation, one-way calls,
  IAsyncEnumerable, response timeouts, migration of activations.
- `reference/serialization.md`: attributes, versioning rules, records, surrogates, immutability,
  grain storage serializers.
- `reference/persistence.md`: IPersistentState API, failure modes, ADO.NET setup with scripts and
  invariants, Redis, Azure, Cosmos, MongoDB contrib, transactions.
- `reference/messaging.md`: streams API, memory streams, broadcast channels, observers, and how
  to pick between them for a WebSocket gateway.
- `reference/scheduling.md`: single-threaded execution, reentrancy options, deadlocks, timers,
  reminders, external tasks and the scheduler.
- `reference/testing.md`: InProcessTestCluster, TestCluster, fixtures, mocks.
- `reference/migration-3x.md`: the checklist for porting the legacy 3.4.3 code in this repo.
- `reference/sources.md`: URLs and dates of every page used.
