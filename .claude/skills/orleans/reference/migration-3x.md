# Porting the legacy 3.4.3 backend to Orleans 10

The code in `backend/` is Orleans 3.4.3 on net5.0 with `Orleans.Providers.MongoDB` 3.3.1,
`Microsoft.Orleans.CodeGenerator.MSBuild`, `AddSimpleMessageStreamProvider("SMS")`,
`UseTransactions()`, `Grain<TState>` + `[StorageProvider]`, `ClientBuilder` in the gateway and
`TestClusterBuilder` in tests. This is the ordered checklist. Items are (docs) unless marked.

## 1. Projects and packages

- Target `net10.0` everywhere (Messages was netstandard2.0 so Unity could share it; keep the
  wire types Unity-compatible by hand instead of sharing the assembly, or split a netstandard
  2.1 project that references no Orleans packages).
- Interfaces and Collections: replace `Microsoft.Orleans.Core.Abstractions` and
  `Microsoft.Orleans.CodeGenerator.MSBuild` with `Microsoft.Orleans.Sdk`.
- Silo: `Microsoft.Orleans.Server` plus `Microsoft.Orleans.Reminders`, `Microsoft.Orleans.Streaming`
  (or `Microsoft.Orleans.BroadcastChannel`) and the chosen provider packages. Drop the unused
  Azure clustering/persistence/reminder packages and `Microsoft.Orleans.Transactions` unless a
  grain adopts transactional state.
- Gateway (FrontEnd): drop the project reference to Silo and the separate client. Either co-host
  the silo in the ASP.NET Core process (recommended, see hosting.md) or reference
  `Microsoft.Orleans.Client`.
- Tests: `Microsoft.Orleans.TestingHost` 10.x, move to `InProcessTestClusterBuilder`.
- Remove every `ConfigureApplicationParts(...)` call; the source generator covers it. Remove
  `KnownAssembly` attributes in favor of `[GenerateCodeForDeclaringAssembly(typeof(X))]` when a
  referenced assembly holds types without attributes.
- Enable `<ImplicitUsings>` and `<Nullable>`; the Sdk package adds the Orleans usings.

## 2. Serialization (the largest mechanical change)

- Every state class (`DeveloperState`, `PlayerState`, `RelayServerGameState`, `LeaderBoardState`,
  `PlayerInventoryState`, `GameModeManagerState`, `IAPState`, ...), every DTO in Interfaces
  (`GameInfo`, `LeaderBoardEntry`, `GameMode`, `ChallengeRequestState`, `FoundMatchResult`, ...),
  every enum used in state is fine as is, and every custom exception (`ChallengeTimeoutException`,
  `GameNotRunningException`) gets `[GenerateSerializer]`, `[Alias]` and `[Id(n)]` on each member.
  `[Serializable]` stays harmless but does nothing.
- Types with `IPlayer`/`IRelayServerGame` grain references as members or dictionary keys still
  serialize; consider switching keys to the player id string for readable JSON state.
- `Tuple<...>` and `ValueTuple` returns in the interfaces (`Task<Tuple<int, List<...>>>`,
  `Task<(int, string)>`) serialize, but named records are clearer; do not rename members of
  records after deployment.
- `IBackendStorageServer` returns PlayFab model types; those go away with PlayFab.
- The MongoDB provider stored JSON with `NullValueHandling.Include` and
  `ObjectCreationHandling.Replace`; if any data must be migrated, export it and re-import it
  under the new provider rather than trying to read 3.x documents directly (inferred: type name
  metadata in the old documents will not match).

## 3. Hosting

- Silo `Program.cs`: `Host.CreateApplicationBuilder(args).UseOrleans(silo => ...)`. Move the
  `#if DEBUG` provider switch to configuration (`ASPNETCORE_ENVIRONMENT` or an `Orleans:Provider`
  setting). Replace `devsconfig.json` reading with the options pattern and user secrets.
- Gateway: delete `ClusterClientHostedService` (the retry loop, `ClientBuilder`,
  `AddSimpleMessageStreamProvider`). With co-hosting, inject `IClusterClient` into the WebSocket
  handler. `Program.Main` currently blocks on connecting before building the web host; that whole
  dance disappears.
- `AddStartupTask<StartupTasks>()` still exists; the docs recommend an `IHostedService`
  registered after `UseOrleans` instead. The seed logic (create developer, game, leaderboard
  "l1") belongs in a dev-only hosted service or a CLI command, not in silo startup.
- `services.Configure<ConsoleLifetimeOptions>` and `logging.AddConsole()` stay valid.

## 4. Grain base class changes

- `OnActivateAsync()` becomes `OnActivateAsync(CancellationToken)`;
  `OnDeactivateAsync()` becomes `OnDeactivateAsync(DeactivationReason, CancellationToken)`.
- `Grain<TState>` with `[StorageProvider(ProviderName = "Default")]` compiles and works (legacy
  path). Prefer converting to constructor-injected `IPersistentState<T>` grain by grain; the
  storage provider name `"blob"` used for large data needs a matching `AddXGrainStorage("blob")`.
- Reminder and stream helpers are extension methods: `this.RegisterOrUpdateReminder(...)`,
  `this.GetReminders()`, `this.GetStreamProvider("...")`. Unqualified calls fail to compile.
- `RegisterTimer(cb, state, due, period)` is obsolete: `this.RegisterGrainTimer(cb, state, new
  GrainTimerCreationOptions { DueTime, Period, Interleave = true })` keeps the old interleaving
  semantics; drop `Interleave` once the callback is known to be safe without it.
- `DeactivateOnIdle()`, `DelayDeactivation()` unchanged.
- `IGrainWithGuidCompoundKey` and `GetPrimaryKey(out string ext)` unchanged.
- `[AlwaysInterleave]` on `ITimedExecutionManager.AddGroup` still valid. `[Reentrant]` on
  `RelayServerGame` still valid; revisit with `[ReadOnly]` and `AllowCallChainReentrancy`.
- `[Unordered]` is a no-op now; remove it if present.

## 5. Streams

- `AddSimpleMessageStreamProvider("SMS", x => x.Configure(y => y.FireAndForgetDelivery = true))`
  has no equivalent. Choose per use:
  - Gateway subscribes on behalf of a connected player (the `FRIEND_EVENTS_STREAM_NAME` and
    `CHALLENGE_EVENTS_STREAM_NAME` streams): `AddMemoryStreams("game")` on silo and client plus
    `AddMemoryGrainStorage("PubSubStore")`, and `StreamId.Create(ns, key)` instead of
    `GetStream<T>(Guid, ns)`. Note the legacy code keyed streams by Guid; a player id string is
    now allowed directly.
  - Grain-only fan-out with implicit subscribers: `AddBroadcastChannel("...")` with
    `[ImplicitChannelSubscription]` and `IOnBroadcastChannelSubscribed`.
- `IUNetConnectionObserver` / `UNetConnectionDataTransferMessage` (an observer-based push from
  grains to the gateway) maps to `IGrainObserver` with `ObserverManager` and `[OneWay]`; the
  observer interface methods must return `Task`/`ValueTask`, not `void`.

## 6. Transactions

`UseTransactions()` in the silo without any `[Transaction]` methods or `ITransactionalState` is
dead weight; remove it. If the purchase path (inventory + wallet) later needs atomicity, follow
persistence.md.

## 7. Cancellation and filters

- Replace any `GrainCancellationToken` with `CancellationToken` parameters.
- `services.AddGrainCallFilter(...)` (if added) becomes `silo.AddIncomingGrainCallFilter(...)`.
- The gateway's per-message session check (`AddUserCallCheckSessionAndGetInfo`) is a candidate
  for a `RequestContext` value set by the gateway plus an incoming call filter, instead of a call
  on every request.

## 8. Tests

- `TestClusterBuilder` with `ISiloBuilderConfigurator` becomes `InProcessTestClusterBuilder`
  with `ConfigureSilo`. `cluster.GrainFactory` becomes `cluster.Client` (or keep `GrainFactory`
  on `TestCluster`).
- Tests that depended on PlayFab (`ClusterFixture` registers `IBackendStorageServer` as
  `PlayfabManager`) must switch to a fake `IBackendStorageServer` or, once PlayFab is removed,
  to the local implementations.

## 9. Providers

- MongoDB: `Orleans.Providers.MongoDB` 9.5.0 replaces 3.3.1. `UseMongoDBClient`,
  `UseMongoDBClustering`, `UseMongoDBReminders`, `AddMongoDBGrainStorage` keep their names;
  `ConfigureJsonSerializerSettings` and `CreateShardKeyForCosmos` as used in `Silo/Program.cs`
  must be re-checked against the 9.5 options types (inferred: the shard key option may remain,
  the Newtonsoft settings hook is replaced by `IGrainStorageSerializer`). Not verified on
  Orleans 10.
- Or switch to official ADO.NET (PostgreSQL) or Redis providers; both cover clustering,
  persistence and reminders. See persistence.md.
- `ConfigureEndpoints(IPAddress.Loopback, 11111, 30000)` becomes `ConfigureEndpoints(11111,
  30000)` plus `EndpointOptions.AdvertisedIPAddress` only when needed.

## 10. Operational

- No rolling upgrade from 3.x: new cluster, then cut over.
- Telemetry consumers are gone; wire `Microsoft.Orleans` meters into OpenTelemetry.
- The membership protocol detects failures in about 90 s (was 10 min); default placement is
  resource-optimized (was random).

## Order of work that keeps the build green

1. Bump target frameworks and packages; fix hosting so an empty silo starts.
2. Add serialization attributes to Interfaces (the compiler and analyzers flag missing ids).
3. Fix grain base class signatures and extension method calls in Collections.
4. Replace SMS with memory streams or broadcast channels.
5. Port the gateway to co-hosting.
6. Port tests to `InProcessTestCluster` and run them; fix reentrancy timeouts as they appear.
