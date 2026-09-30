# Hosting, configuration and operations

All (docs) unless marked. Namespaces: options live in `Orleans.Configuration`, builder helpers in
`Orleans.Hosting`. With `<ImplicitUsings>enable</ImplicitUsings>` the `Orleans` and
`Orleans.Hosting` namespaces are imported automatically by the Sdk package.

## Packages

- Silo host: `Microsoft.Orleans.Server` (includes Client and Sdk).
- External client: `Microsoft.Orleans.Client`.
- Grain interface and grain class libraries: `Microsoft.Orleans.Sdk` (brings the source
  generator; there is no separate CodeGenerator.MSBuild package any more).
- Reminders: `Microsoft.Orleans.Reminders` (+ a provider package). Streams:
  `Microsoft.Orleans.Streaming` (+ provider). Broadcast channel:
  `Microsoft.Orleans.BroadcastChannel`. Transactions: `Microsoft.Orleans.Transactions`.
- Clustering: `Microsoft.Orleans.Clustering.{AdoNet,Redis,AzureStorage,Cosmos,DynamoDB,
  Cassandra,Consul,ZooKeeper}`. Persistence: `Microsoft.Orleans.Persistence.{AdoNet,Redis,
  AzureStorage,Cosmos,DynamoDB,Memory}`. Reminders: `Microsoft.Orleans.Reminders.{AdoNet,Redis,
  AzureStorage,Cosmos,DynamoDB}`. Grain directory: `Microsoft.Orleans.GrainDirectory.{AdoNet,
  Redis,AzureStorage}`.
- Kubernetes: `Microsoft.Orleans.Hosting.Kubernetes`. Dashboard (10.0+, preview):
  `Microsoft.Orleans.Dashboard` and `Microsoft.Orleans.Dashboard.Abstractions`.
- Testing: `Microsoft.Orleans.TestingHost`.

## Silo

```csharp
var builder = Host.CreateApplicationBuilder(args);   // or WebApplication.CreateBuilder(args)
builder.UseOrleans(silo =>
{
    silo.Configure<ClusterOptions>(o =>
    {
        o.ClusterId = "wildcat-dev";   // which hosts form one cluster; may change per deployment
        o.ServiceId = "wildcat";       // stable forever; storage providers key data by it
    });
    silo.UseLocalhostClustering();     // dev: loopback, ports 11111 (silo) and 30000 (gateway)
    silo.ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000);   // optional
});
using var host = builder.Build();
await host.RunAsync();
```

Both ids default to "default". Two clusters may share a ServiceId (blue/green); every host in a
cluster must share the ServiceId.

Endpoints: `EndpointOptions.SiloPort`, `GatewayPort`, `AdvertisedIPAddress`,
`SiloListeningEndpoint`, `GatewayListeningEndpoint`. The listening endpoints can bind to
`IPAddress.Any` on different ports than the advertised ones (NAT and port forwarding).

Clustering providers on the silo builder:

- `UseLocalhostClustering()` and `UseDevelopmentClustering(primarySiloEndpoint)`: dev only.
- `UseAdoNetClustering(o => { o.Invariant = "Npgsql"; o.ConnectionString = ...; })`.
- `UseRedisClustering(o => o.ConfigurationOptions = ConfigurationOptions.Parse(...))`.
- `UseAzureStorageClustering(o => o.ConfigureTableServiceClient(uri, new DefaultAzureCredential()))`.
- Cosmos, DynamoDB, Cassandra, Consul, ZooKeeper have matching `Use*Clustering` methods.

Kubernetes hosting is not clustering: `UseKubernetesHosting()` sets the silo name to the pod name,
advertises the pod IP, binds listeners to any address, reads `ClusterId`/`ServiceId` from pod
labels `orleans/clusterId` and `orleans/serviceId`, and marks silos without pods dead. Pods need
env vars `POD_NAME`, `POD_NAMESPACE`, `POD_IP`, `ORLEANS_SERVICE_ID`, `ORLEANS_CLUSTER_ID`, an
RBAC role with get/watch/list/delete/patch on pods, and `automountServiceAccountToken: true`.
Set `terminationGracePeriodSeconds` and `DOTNET_SHUTDOWNTIMEOUTSECONDS` high enough for a
graceful silo shutdown (the docs example uses 180 and 120). Orleans 9+ detects a dead silo in
about 90 seconds.

## External client

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleansClient(client =>
{
    client.Configure<ClusterOptions>(o => { o.ClusterId = "wildcat-dev"; o.ServiceId = "wildcat"; });
    client.UseLocalhostClustering();    // or UseAdoNetClustering / UseRedisClustering / UseStaticClustering(gateways)
});
using var host = builder.Build();
await host.StartAsync();              // connects during host start
var client = host.Services.GetRequiredService<IClusterClient>();
```

The client connects when the host starts and stays usable until disposed. Connection retries at
startup go through an `IClientConnectionRetryFilter` registered in DI (return true to retry,
false to give up). A `SiloUnavailableException` during a grain call means the connection dropped;
the grain reference stays valid, retry later. `GatewayOptions.GatewayListRefreshPeriod`
controls gateway discovery. `UseStaticClustering` takes explicit gateway URIs
(`new IPEndPoint(ip, 30000).ToGatewayUri()`).

Co-hosting: when the gateway (ASP.NET Core, WebSocket server) runs inside the silo process, do
not configure a client. The silo host registers `IClusterClient` and `IGrainFactory`, and calls
skip the gateway hop. Register `UseOrleans` before `ConfigureWebHostDefaults` or before
`builder.Build()` in minimal hosting so the silo is started before Kestrel accepts requests.
Client code is not single-threaded; the gateway must do its own synchronization.

## Startup work and background services

Preferred: a `BackgroundService` or `IHostedService` registered after `UseOrleans`, since hosted
services start in registration order. Legacy `silo.AddStartupTask<T>()` or
`AddStartupTask(async (services, ct) => ...)` still works; an exception in a startup task stops
the silo.

## Options worth knowing

| Options class | Use |
|---|---|
| `ClusterOptions` | ClusterId, ServiceId |
| `EndpointOptions` | ports and advertised address |
| `SiloMessagingOptions` / `ClientMessagingOptions` | `ResponseTimeout` (default 30 s), `CancelRequestOnTimeout` (default false since 10.0), `WaitForCancellationAcknowledgement` |
| `GrainCollectionOptions` | `CollectionAge` (default 15 min), `ClassSpecificCollectionAge`, memory-based shedding |
| `ClusterMembershipOptions` | failure detection tuning |
| `LoadSheddingOptions` | `LoadSheddingEnabled`, `CpuThreshold` (was `LoadSheddingLimit`) |
| `GatewayOptions`, `StaticGatewayListProviderOptions` | client gateway discovery |
| `ResourceOptimizedPlacementOptions` | weights for the default placement (9.2+) |
| `SchedulingOptions`, `PerformanceTuningOptions`, `ProcessExitHandlingOptions` | tuning |

Options follow the .NET options pattern and bind from configuration.

## Dashboard (10.0+, preview)

```csharp
builder.UseOrleans(silo => silo.AddDashboard(o => { o.HideTrace = false; o.CounterUpdateIntervalMs = 1000; o.HistoryLength = 100; }));
var app = builder.Build();
app.MapOrleansDashboard(routePrefix: "/dashboard").RequireAuthorization("DashboardAccess");
```

Every silo must call `AddDashboard()` even when the UI is served from a client host. Grain
profiling stops after `GrainProfilerOptions.DeactivationTime` (1 minute) of dashboard inactivity
unless `TraceAlways = true`. Exclude hot grains with `[NoProfiling]`. Protect it with
authorization before exposing it.

## Aspire (8.0+)

AppHost: `builder.AddOrleans("cluster").WithClustering(redis).WithGrainStorage("Default", redis)
.WithReminders(redis).WithMemoryStreaming("game")` then `AddProject<Projects.WildCat_Host>("silo")
.WithReference(orleans).WaitFor(redis).WithReplicas(3)`; web frontends reference
`orleans.AsClient()`. Silo: `builder.AddServiceDefaults(); builder.AddKeyedRedisClient(
"orleans-redis"); builder.UseOrleans();` with no delegate, because Aspire injects ClusterId,
ServiceId, endpoints and providers through environment variables. Forgetting the `AddKeyed*`
call fails at runtime with a dependency resolution error. Packages: `Aspire.Hosting.Orleans` in
the AppHost, `Aspire.StackExchange.Redis` (or the Azure equivalents) in the services.
`aspire publish -o ./k8s-manifests` (with `Aspire.Hosting.Kubernetes`) emits Kubernetes YAML and
Helm charts.

## Observability

Orleans 7+ dropped the telemetry consumer packages. Metrics come from the `Microsoft.Orleans`
meter (`dotnet counters monitor -n MyApp --counters Microsoft.Orleans`, or
`AddMeter("Microsoft.Orleans")` in OpenTelemetry). Traces come from activity sources
`Orleans.Diagnostics.ActivitySources.ApplicationGrainActivitySourceName` and
`LifecycleActivitySourceName` (also `StorageActivitySourceName`, `RuntimeActivitySourceName`,
`AllActivitySourceName`). Call `clientBuilder.AddActivityPropagation()` to flow traces across
calls. The `orleans-app-requests-canceled` metric counts cancellations.

## Shutdown

`RunConsoleAsync()` or `UseConsoleLifetime()` handle Ctrl+C and SIGTERM and shut the silo down
through the generic host. Silo shutdown is graceful: it deactivates grains and leaves the
cluster. Give containers enough grace period.

## Rolling upgrades

Rolling upgrades between 3.x and 7+ are impossible (wire protocol changed) and between 7.x and
10.0 are not recommended. Deploy a new cluster with a new ClusterId under the same ServiceId,
migrate state if needed, switch traffic, retire the old cluster. Within a major version, grain
interface versioning (`[Version]`) supports heterogeneous clusters.
