# Grains: interfaces, identity, lifecycle, placement, filters

All (docs) unless marked.

## Interfaces

- A grain interface extends one key marker: `IGrainWithGuidKey`, `IGrainWithIntegerKey` (long),
  `IGrainWithStringKey`, `IGrainWithGuidCompoundKey`, `IGrainWithIntegerCompoundKey`.
- Methods return `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` or `IAsyncEnumerable<T>`.
- At most one `CancellationToken` parameter, last, ideally `= default` (build error ORLEANS0109
  otherwise). Adding or removing it later is wire compatible.
- `[ResponseTimeout("00:00:05")]` on the interface method (not the class) overrides the 30 s
  default from `MessagingOptions.ResponseTimeout`. A timeout throws `TimeoutException`.
- `[ReadOnly]`, `[AlwaysInterleave]`, `[OneWay]`, `[Transaction(...)]` go on interface methods.
- Interfaces and grain classes live in libraries referencing `Microsoft.Orleans.Sdk`. The source
  generator emits proxies and serializers at build time; no runtime code generation, no
  `ConfigureApplicationParts`.

## Identity

Identity is `type/key`, both strings. Type name = class name without a trailing "Grain",
lowercased (`PlayerGrain` becomes `player`). Override with `[GrainType("player")]` on the class;
generic grains add the arity (`[GrainType("dict`2")]`). `[DefaultGrainType]` on an interface
picks the implementation `GetGrain<I>` resolves; `[GrainInterfaceType]` renames an interface
without breaking stored references (pair it with `[Alias]`). Stick to alphanumerics and `- _ @ =`
in keys.

Reading the key inside a grain: `this.GetPrimaryKey()`, `GetPrimaryKeyLong()`,
`GetPrimaryKeyString()`, and `GetPrimaryKey(out string ext)` for compound keys. Client side:
`GetGrain<I>(guid)`, `GetGrain<I>(long)`, `GetGrain<I>(string)`, `GetGrain<I>(guid, ext, null)`.
Singletons use a well-known key such as "default" or 0.

## References

`GrainFactory.GetGrain<I>(key)` in a grain, `IClusterClient.GetGrain<I>(key)` outside. A reference
is a proxy: serializable, storable in state, valid across restarts. `this.AsReference<I>()` gets
a reference to the current grain. `GetGrain` is local and cheap; the grain activates on first
call. Exceptions thrown by grain methods propagate to the caller; the type must be serializable
on both sides or the caller sees `UnavailableExceptionFallbackException` with the message and
stack preserved. Only `InconsistentStateException` deactivates the grain.

## Class shape

```csharp
public sealed class GameGrain(
    [PersistentState("game", "Default")] IPersistentState<GameState> state,
    ILogger<GameGrain> log) : Grain, IGameGrain
{
    public override Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;
}
```

- Constructor injection from the silo's DI container. `IGrainContext` is injectable.
- `OnActivateAsync(CancellationToken)`: state is already loaded. An exception fails activation.
- `OnDeactivateAsync(DeactivationReason, CancellationToken)`: best effort only; never rely on it
  for persistence (silo crashes skip it).
- POCO grains implement `IGrainBase` (needs an `IGrainContext GrainContext` property) instead of
  inheriting `Grain`; extension methods such as `DeactivateOnIdle`, `RegisterGrainTimer`,
  `GetReminders`, `GetStreamProvider` then work through `this.`.

## Lifecycle and activation collection

Stages: `First`, `SetupState` (state read), `Activate` (OnActivateAsync), `Last`. Components
injected into the grain can subscribe to the lifecycle via `IGrainContext.ObservableLifecycle`.

Idle activations are collected after `GrainCollectionOptions.CollectionAge` (15 minutes default,
per-class override through `ClassSpecificCollectionAge` or `[CollectionAgeLimit]`). Counts as
activity: receiving a call, a reminder, or a stream event. Does not count: making calls, timer
ticks (unless `KeepAlive = true`), I/O. `DeactivateOnIdle()` deactivates after the current
request; `DelayDeactivation(TimeSpan)` postpones collection (not a pin, still best effort);
`[KeepAlive]` exempts a class from idle collection. Memory-based shedding:
`EnableActivationSheddingOnMemoryPressure`, `MemoryUsageLimitPercentage` (80),
`MemoryUsageTargetPercentage` (75).

Migration (8+): `this.MigrateOnIdle()` moves an activation to another silo keeping in-memory
state; `IPersistentState` and `Grain<T>` state migrate automatically, other fields need
`IGrainMigrationParticipant` (`OnDehydrate`/`OnRehydrate`). `[Immovable]` opts out of the
experimental repartitioner (ORLEANSEXP001) and rebalancer (ORLEANSEXP002). Stateless workers,
grain services and client grains cannot migrate.

## Placement

Default since 9.2 is `ResourceOptimizedPlacement` (CPU, memory, activation count weights and a
`LocalSiloPreferenceMargin`). Per class attributes: `[RandomPlacement]`,
`[PreferLocalPlacement]`, `[HashBasedPlacement]`, `[ActivationCountBasedPlacement]`,
`[SiloRoleBasedPlacement]`, `[StatelessWorker]`. Change the global default with
`silo.Services.AddSingleton<PlacementStrategy, RandomPlacement>()`. Custom strategies implement
`IPlacementDirector` and register with `AddPlacementDirector<TStrategy, TDirector>()`.
Placement filters (9+) narrow candidate silos by silo metadata.

## Stateless workers

`[StatelessWorker]` (optional `maxLocalWorkers`, default = CPU count): the runtime creates as many
local activations as needed, requests never leave the silo when a compatible one exists, and
activations are not individually addressable. Use key 0 or `Guid.Empty`. Good for decode,
validate, pre-aggregate, hot read caches. Not reentrant unless `[Reentrant]` is added. Cannot
subscribe to streams (undefined behavior) and cannot migrate.

## Grain call filters

```csharp
silo.AddIncomingGrainCallFilter<AuthFilter>();                    // silo-wide, in registration order
silo.AddIncomingGrainCallFilter(async ctx => { await ctx.Invoke(); });
client.AddOutgoingGrainCallFilter(ctx => { RequestContext.Set("trace", id); return ctx.Invoke(); });

public sealed class AuthFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext ctx)
    {
        if (ctx.ImplementationMethod.GetCustomAttribute<AdminOnlyAttribute>() is not null
            && RequestContext.Get("isAdmin") is not true)
            throw new UnauthorizedAccessException(ctx.ImplementationMethod.Name);
        await ctx.Invoke();
    }
}
```

`IIncomingGrainCallContext` exposes `Grain`, `InterfaceMethod`, `ImplementationMethod`,
`Arguments`, `Result`. A grain can also implement `IIncomingGrainCallFilter` itself (runs after
the DI filters). Filters see grain extension calls too (streams, cancellation), so
`ImplementationMethod` is not always on the grain class. `AddGrainCallFilter` on
`IServiceCollection` was removed in 10.0. Exception conversion for clients that lack a DLL is a
documented filter use case.

## Request context

`RequestContext.Set(key, value)` / `Get(key)` / `Remove(key)`: async-local metadata that flows
with every outgoing call (client to grain, grain to grain) and into `StartNew`/`ContinueWith`,
but never back with responses. Keep values small (strings, guids, numbers). Available during
placement through `PlacementTarget.RequestContextData`. This is the channel for auth claims and
trace ids between a gateway and grains; the gateway is responsible for setting it correctly.

## Cancellation (9+)

Cooperative: the grain must observe the token. Already-cancelled tokens fail the call before it
is sent; cancellation signals propagate while the call is active, including across silos. Token
callbacks registered inside a grain run on the grain scheduler. `CancelRequestOnTimeout` defaults
to false in 10.0; set it true to signal timed-out callees. `GrainCancellationToken` is legacy.

## One-way calls and immutability

`[OneWay]` on a `Task`/`ValueTask` interface method returns immediately, no completion, no error,
no delivery guarantee. Reserve for high-volume fire-and-forget such as relayed position updates
where a lost packet is acceptable. `[Immutable]` on a type, member or parameter (or `Immutable<T>`
around a value) skips the defensive deep copy Orleans makes for every call argument and return
value; only use it when neither side mutates the object afterwards. Byte arrays relayed through
a game grain are the classic candidate.

## IAsyncEnumerable

Grain methods may return `IAsyncEnumerable<T>` (7.2+). Use `[EnumeratorCancellation]` on the
token, `.WithBatchSize(n)` on the consumer (default batch 100). It is request-scoped
grain-to-caller streaming, not pub/sub; use Orleans streams for that.
