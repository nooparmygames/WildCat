# Testing grains

All (docs). Package `Microsoft.Orleans.TestingHost`, xUnit.

## InProcessTestCluster (9.0+, recommended)

```csharp
public sealed class ClusterFixture : IAsyncLifetime
{
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((options, silo) =>
        {
            silo.AddMemoryGrainStorage("Default");
            silo.AddMemoryGrainStorage("PubSubStore");
            silo.AddMemoryStreams("game");
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IClock, FakeClock>();     // test doubles go here
        });
        builder.ConfigureClient(client => client.AddMemoryStreams("game"));
        builder.ConfigureHost(host => host.Services.AddSingleton<ISharedThing, SharedThing>());
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(ClusterCollection))]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture> { }

[Collection(nameof(ClusterCollection))]
public sealed class PlayerGrainTests(ClusterFixture fixture)
{
    [Fact]
    public async Task SetName_persists()
    {
        var grain = fixture.Cluster.Client.GetGrain<IPlayerGrain>("p1");
        await grain.SetName("Ash");
        Assert.Equal("Ash", (await grain.GetProfile()).Name);
    }
}
```

- Delegate-based configuration; services registered in `ConfigureHost` are shared by test code,
  silos and client, which is the point of the in-process variant.
- Options: `ClusterId`, `ServiceId` (auto), `InitialSilosCount` (1), `InitializeClientOnDeploy`
  (true), `ConfigureFileLogging` (true), `UseRealEnvironmentStatistics` (false), `GatewayPerSilo`
  (true).
- `await cluster.StartSiloAsync()`, `StopSiloAsync(silo)`, `RestartAsync()` for membership tests.
- Startup costs seconds, so share one cluster per test collection. Use distinct grain keys per
  test instead of resetting state.

## TestCluster (7.x and 8.x compatible, multi-process capable)

```csharp
public sealed class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; } = new TestClusterBuilder()
        .AddSiloBuilderConfigurator<TestSiloConfigurations>()
        .AddClientBuilderConfigurator<TestClientConfigurations>()
        .Build();
    public ClusterFixture() => Cluster.Deploy();
    void IDisposable.Dispose() => Cluster.StopAllSilos();
}

file sealed class TestSiloConfigurations : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo) { silo.AddMemoryGrainStorage("Default"); }
}
```

Configurator classes must be public or accessible to the test host and cannot capture test
instances, which is why the in-process API exists. Default silo count is 2. The legacy tests in
this repo use this API with 3.x names (`TestClusterBuilder`, `ISiloBuilderConfigurator` became
`ISiloConfigurator`).

## Mocks

Grains can be constructed directly and `GrainFactory` mocked if the grain exposes it as
`public new virtual IGrainFactory GrainFactory => base.GrainFactory;`. Limits: no scheduler,
no reentrancy semantics, no serialization checks. OrleansTestKit (OrleansContrib) is the
alternative.

## What to test in this repo

- Every grain interface change: a round trip through the test cluster catches missing
  `[GenerateSerializer]`/`[Id]` attributes at once (calls fail to serialize).
- Reentrancy assumptions: run the two calls concurrently with `Task.WhenAll` and assert they
  complete within a few seconds; a deadlock shows up as a 30 s timeout.
- Streams: subscribe from `Cluster.Client`, publish from a grain, await the item with a timeout.
- Reminders: use `UseInMemoryReminderService` and assert registration via `GetReminders`;
  do not wait for real ticks.
