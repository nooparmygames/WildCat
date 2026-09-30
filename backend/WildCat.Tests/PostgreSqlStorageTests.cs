using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// A one-silo test cluster whose grain storage and reminders come from
    /// <see cref="SiloBuilderExtensions.AddWildCatStorage"/> with the PostgreSQL provider, the same
    /// code path as the host. The in-process test cluster always brings its own membership, so
    /// clustering is covered by <see cref="PostgreSqlHostTests"/> instead. Every run uses its own
    /// ServiceId, so runs do not see each other's rows. Nothing starts when the environment
    /// variable is not set.
    /// </summary>
    public class PostgreSqlStorageFixture : IAsyncLifetime
    {
        public string ServiceId { get; } = $"wildcat-test-{Guid.NewGuid():N}";

        public InProcessTestCluster Cluster { get; private set; }

        public async Task InitializeAsync()
        {
            string connectionString = PostgreSqlFactAttribute.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
            builder.Options.ServiceId = ServiceId;
            builder.ConfigureSilo((siloOptions, silo) =>
            {
                var hosting = new OrleansHostingOptions { Provider = OrleansProvider.PostgreSQL, ServiceId = ServiceId };
                silo.AddWildCatStorage(hosting, connectionString);
                silo.AddGameStreams();
                silo.AddLegacyCallChainReentrancy();
                silo.Services.AddSingleton<IEmailSender>(new CapturingEmailSender());
            });
            builder.ConfigureClient(client => client.AddMemoryStreams(StreamNames.PROVIDER_NAME));
            Cluster = builder.Build();
            await Cluster.DeployAsync();
        }

        public async Task DisposeAsync()
        {
            if (Cluster != null)
            {
                await Cluster.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Opt-in integration tests of the PostgreSQL grain storage and reminder table. Start a
    /// database with the Orleans scripts applied (the compose file does that) and set
    /// WILDCAT_TEST_POSTGRES, for example
    /// "Host=localhost;Port=5432;Database=wildcat;Username=wildcat;Password=...".
    /// </summary>
    public class PostgreSqlStorageTests : IClassFixture<PostgreSqlStorageFixture>
    {
        private readonly PostgreSqlStorageFixture fixture;
        private IGrainFactory GrainFactory => fixture.Cluster.Client;

        public PostgreSqlStorageTests(PostgreSqlStorageFixture fixture)
        {
            this.fixture = fixture;
        }

        [PostgreSqlFact]
        public async Task PlayerStateIsStoredInPostgreSqlAndReadBackAfterDeactivation()
        {
            string id = TestSetup.UniqueId("pg-player");
            string user = TestSetup.UniqueId("pg-user");
            var player = GrainFactory.GetGrain<IPlayer>(id);
            await player.CreateAccountWithDevice(id).WaitAsync(TestSetup.Timeout);
            Assert.Equal(AdditionResult.Success, await player.AddPlayerUserAndPassword(user, "a-password").WaitAsync(TestSetup.Timeout));

            await fixture.Cluster.Deactivate(player);

            var state = await player.GetState().WaitAsync(TestSetup.Timeout);
            Assert.True(state.IsCreated);
            Assert.True(state.UserNamePasswordPairs.ContainsKey(user));
            Assert.True(await PostgreSqlFactAttribute.CountAsync(
                "SELECT count(*) FROM orleansstorage WHERE serviceid = @service", ("service", fixture.ServiceId)) > 0);
        }

        [PostgreSqlFact]
        public async Task LeaderBoardStateInTheBlobStorageSurvivesDeactivation()
        {
            string key = ResourcesUtility.GetLeaderBoardKey(Guid.NewGuid().ToString(), "l1");
            var leaderBoard = GrainFactory.GetGrain<ILeaderBoard>(key);
            await leaderBoard.Initialize("l1", DateTime.Now, DateTime.MaxValue, true).WaitAsync(TestSetup.Timeout);

            await fixture.Cluster.Deactivate(leaderBoard);

            Assert.Equal("l1", await leaderBoard.GetName().WaitAsync(TestSetup.Timeout));
        }

        [PostgreSqlFact]
        public async Task ReminderTableStoresAndRemovesARow()
        {
            var table = fixture.Cluster.Silos[0].ServiceProvider.GetRequiredService<IReminderTable>();
            var grainId = GrainId.Create("wildcat-test", Guid.NewGuid().ToString("N"));
            string etag = await table.UpsertRow(new ReminderEntry
            {
                GrainId = grainId,
                ReminderName = "sanity",
                StartAt = DateTime.UtcNow.AddHours(1),
                Period = TimeSpan.FromHours(1),
            }).WaitAsync(TestSetup.Timeout);

            var row = await table.ReadRow(grainId, "sanity").WaitAsync(TestSetup.Timeout);
            Assert.NotNull(row);
            Assert.Equal(TimeSpan.FromHours(1), row.Period);
            Assert.Equal(1, await PostgreSqlFactAttribute.CountAsync(
                "SELECT count(*) FROM orleansreminderstable WHERE serviceid = @service", ("service", fixture.ServiceId)));

            Assert.True(await table.RemoveRow(grainId, "sanity", etag).WaitAsync(TestSetup.Timeout));
            Assert.Equal(0, await PostgreSqlFactAttribute.CountAsync(
                "SELECT count(*) FROM orleansreminderstable WHERE serviceid = @service", ("service", fixture.ServiceId)));
        }
    }
}
