# Sources

Research date 2026-09-30. All pages are on learn.microsoft.com under /en-us/dotnet/orleans/
unless a full URL is given. The date in parentheses is the page's own `ms.date` or
`updated_at`, whichever is later.

- overview (2026-06-29): version summary; 10.3.1 targets .NET 8/9/10; dashboard, Redis providers.
- migration-guide (2026-06-29): 3.x to 7.0 and 7.0 to 10.0 breaking changes.
- grains/ (2026-03-30): interfaces, return types, response timeout, IAsyncEnumerable, references,
  error propagation, virtual methods.
- grains/grain-identity (2026-04-03): type names, keys, compound keys.
- grains/grain-persistence/ (2026-07-01): IPersistentState API, failure modes, Redis, Cosmos,
  custom providers.
- grains/grain-persistence/relational-storage (2026-03-30): ADO.NET provider, options.
- host/configuration-guide/adonet-configuration (2026-07-30): invariants, scripts, packages.
- host/configuration-guide/configuring-ado-dot-net-providers (2026-01-23): clustering, reminders,
  storage samples; Microsoft.Data.SqlClient note.
- host/configuration-guide/serialization (2026-03-30): attributes, records, versioning rules,
  surrogates, MessagePack, storage serializers.
- host/configuration-guide/serialization-immutability (2026-03-30): [Immutable], Immutable<T>.
- streaming/ (2026-01-23): programming model, providers, semantics.
- streaming/streams-programming-apis (2026-03-30): subscribe, resume, implicit, PubSubStore.
- streaming/stream-providers (2026-06-29): Event Hubs, Azure Queue, adapters, memory streams via
  Aspire.
- streaming/broadcast-channel (2026-03-30): channels vs streams.
- grains/observers (2026-03-30): IGrainObserver, ObserverManager, weak references.
- grains/timers-and-reminders (2026-06-29): RegisterGrainTimer options, reminders, providers.
- grains/request-scheduling (2026-03-11): reentrancy table, deadlock cases, call chain
  reentrancy, MayInterleave.
- grains/external-tasks-and-grains (2026-03-30): scheduler rules, deadlocks, summary table.
- grains/transactions (2026-03-30): options, ITransactionalState, ITransactionClient.
- grains/grain-lifecycle (2026-03-30): stages, memory shedding, migration, [Immovable].
- host/configuration-guide/activation-collection (2026-03-30): CollectionAge, DelayDeactivation,
  DeactivateOnIdle, [KeepAlive].
- grains/grain-placement (2026-01-28): strategies, default change in 9.2, repartitioning,
  rebalancing.
- grains/stateless-worker-grains (2026-01-28).
- grains/interceptors (2026-01-28): call filters, ordering, exception conversion.
- grains/request-context (2026-04-02).
- grains/cancellation-tokens (2026-03-30): semantics, options, ORLEANS0109.
- grains/oneway (2026-03-30).
- host/configuration-guide/local-development-configuration (2026-03-30).
- host/configuration-guide/server-configuration (2026-06-29): ClusterOptions, endpoints.
- host/configuration-guide/client-configuration (2026-06-29).
- host/client (2026-03-30): co-hosting, retry filter, SiloUnavailableException.
- host/configuration-guide/startup-tasks (2025-05-29): BackgroundService recommendation.
- host/configuration-guide/list-of-options-classes (2026-03-30).
- host/configuration-guide/shutting-down-orleans (2026-03-30).
- dashboard/ (2026-02-23): AddDashboard, MapOrleansDashboard, options, authorization.
- host/aspire-integration (2026-08-17): AppHost methods, keyed clients, package versions
  (Microsoft.Orleans.Server 10.3.1, Aspire.Hosting.Orleans 13.1.3).
- deployment/kubernetes (2026-06-29): UseKubernetesHosting, env vars, RBAC, probes.
- implementation/testing (2026-01-23): InProcessTestCluster, TestCluster, mocks.
- resources/best-practices (2026-03-30).
- resources/nuget-packages (2026-01-23): package list.
- toc.json: full table of contents (used to find the pages above).
- https://github.com/dotnet/orleans/releases: v10.3.1 latest (2026-08-28), v10.3.0, v10.2.2.
- https://www.nuget.org/packages/Orleans.Providers.MongoDB: 9.5.0 (2026-01-12), depends on
  Microsoft.Orleans.Reminders >= 9.0.1, MongoDB.Driver >= 3.0.0, net8.0.
- https://github.com/OrleansContrib/Orleans.Providers.MongoDB: README API names; no GitHub
  releases published.

Not fetched (candidates if a topic comes up): grains/grain-references, grains/grain-extensions,
grains/grainservices, grains/event-sourcing/*, grains/grain-versioning/*, host/grain-directory,
host/silo-lifecycle, host/heterogeneous-silos, host/transport-layer-security,
host/configuration-guide/serialization-configuration, serialization-customization,
typical-configurations, silo-metadata, deployment/handling-failures, implementation/*.
