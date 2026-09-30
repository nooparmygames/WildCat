---
name: orleans
description: Microsoft Orleans 10 (virtual actors on .NET 8/9/10) as used to build and modernize this game backend - grain interfaces and keys, the [GenerateSerializer]/[Id] serialization rules, IPersistentState storage, streams vs broadcast channels vs observers, timers vs reminders, reentrancy and deadlocks, the task-scheduler rules, hosting with UseOrleans/UseOrleansClient, clustering and persistence providers (ADO.NET/PostgreSQL, Redis, Azure, the MongoDB contrib package), InProcessTestCluster testing, the built-in dashboard, Aspire and Kubernetes hosting, and the 3.x to 7+ migration checklist for the legacy code in backend/. Use when writing or reviewing any grain, silo or client host, storage or stream configuration, test fixture, or when porting a 3.4-era file.
---

# Orleans 10 for this repository

Written from the official docs at learn.microsoft.com/dotnet/orleans on 2026-09-30. Every page
used is listed with its own date in `reference/sources.md`. Where a statement is inferred rather
than documented it says so.

Version facts (docs): the current stable release is Orleans 10.3.1 (2026-08-28). Orleans 10
targets .NET 8, 9 and 10. The machine this repo was set up on has the .NET 10.0.401 SDK. The
legacy code in `backend/` is Orleans 3.4.3 on net5.0 and does not compile against 10 without the
changes in `reference/migration-3x.md`.

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

## Choosing providers for this project

| Concern | Dev / tests | Production candidates (all official unless noted) |
|---|---|---|
| Clustering | `UseLocalhostClustering()` | ADO.NET (PostgreSQL via `Npgsql`), Redis, Azure Table, Cosmos, Kubernetes hosting + one of these |
| Grain storage | `AddMemoryGrainStorage("Default")` | ADO.NET, Redis, Azure Blob/Table, Cosmos, DynamoDB; MongoDB via the contrib `Orleans.Providers.MongoDB` 9.5.0 (built for Orleans 9, untested on 10) |
| Reminders | `UseInMemoryReminderService()` | ADO.NET, Redis, Azure Table, Cosmos |
| Pub/sub | `AddBroadcastChannel` or `AddMemoryStreams` + `AddMemoryGrainStorage("PubSubStore")` | Same in-memory options are acceptable for transient game events; Azure Queue or Event Hubs when durability is required |
| Transactions | `UseTransactions()` + memory storage bridge | `AddAzureTableTransactionalStateStorage`; DynamoDB transactional storage arrived in 10.3 |

The legacy backend used MongoDB for all three system stores plus SMS streams. The contrib MongoDB
package exists and is maintained (9.5.0, 2026-01-12, depends on `Microsoft.Orleans.Reminders`
>= 9.0.1) but nobody has verified it on Orleans 10. Prefer an official provider unless keeping
MongoDB is a hard requirement; PostgreSQL through the ADO.NET packages covers clustering,
persistence and reminders with one database and ships SQL scripts in the docs.

## Recipes

Silo with co-hosted WebSocket gateway (the shape this repo is moving to):

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
