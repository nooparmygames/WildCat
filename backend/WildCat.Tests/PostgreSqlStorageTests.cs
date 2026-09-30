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

        // The ADO.NET provider keys a row by service id, grain key and state name; the grain class is
        // not part of the key. The next three tests put two grain classes with the same key side by
        // side, which only works when every class stores its state under a name of its own.

        [PostgreSqlFact]
        public async Task DeveloperAndDeveloperStorageWithTheSameKeyKeepTheirOwnState()
        {
            var developerKey = await TestSetup.CreateDeveloper(GrainFactory).WaitAsync(TestSetup.Timeout);
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            var storage = GrainFactory.GetGrain<IDeveloperStorage>(developerKey);
            var data = await developer.GetDeveloperData().WaitAsync(TestSetup.Timeout);
            await storage.SetString("motd", "hello", data.DeveloperSecret).WaitAsync(TestSetup.Timeout);
            await developer.ChangeName("Changed", "Later").WaitAsync(TestSetup.Timeout);

            await fixture.Cluster.Deactivate(developer);
            await fixture.Cluster.Deactivate(storage);

            var reloaded = await developer.GetDeveloperData().WaitAsync(TestSetup.Timeout);
            Assert.True(reloaded.IsCreated);
            Assert.Equal(data.Email, reloaded.Email);
            Assert.Equal("Changed", reloaded.FirstName);
            Assert.Equal("hello", await storage.GetString("motd").WaitAsync(TestSetup.Timeout));
            var all = await storage.GetAllStringData(data.DeveloperSecret).WaitAsync(TestSetup.Timeout);
            Assert.Equal("hello", Assert.Single(all).Value);
        }

        [PostgreSqlFact]
        public async Task FriendListAndChallengeHandlerWithTheSameKeyKeepTheirOwnState()
        {
            var gameKey = Guid.NewGuid();
            string playerId = TestSetup.UniqueId("pg-player");
            var friend = GrainFactory.GetGrain<IPlayer>(TestSetup.UniqueId("pg-friend"));
            var challenger = GrainFactory.GetGrain<IPlayer>(TestSetup.UniqueId("pg-challenger"));
            var matchKey = Guid.NewGuid();
            var friendList = GrainFactory.GetGrain<IPlayerFriendList>(gameKey, playerId, null);
            var challenges = GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, playerId, null);

            await friendList.AddFriend(friend, "a friend").WaitAsync(TestSetup.Timeout);
            Assert.Equal(ChallengeAcceptionStatus.Received,
                await challenges.ChallengeRequested(challenger, new byte[] { 1 }, true).WaitAsync(TestSetup.Timeout));
            // Stores the received request (ChallengeRequested alone only keeps it in memory).
            await challenges.UpdateInReceiveList(challenger, matchKey).WaitAsync(TestSetup.Timeout);
            await friendList.AddFriend(challenger, "another friend").WaitAsync(TestSetup.Timeout);

            await fixture.Cluster.Deactivate(friendList);
            await fixture.Cluster.Deactivate(challenges);

            var friends = await friendList.GetFriends(Array.Empty<string>()).WaitAsync(TestSetup.Timeout);
            Assert.Equal(
                new[] { friend.GetPrimaryKeyString(), challenger.GetPrimaryKeyString() },
                friends.Friends.Select(x => x.Player.GetPrimaryKeyString()).ToArray());
            var (sent, received) = await challenges.GetChallengeRequests().WaitAsync(TestSetup.Timeout);
            Assert.Empty(sent);
            var request = Assert.Single(received);
            Assert.Equal(challenger.GetPrimaryKeyString(), request.Player.GetPrimaryKeyString());
            Assert.Equal(matchKey, request.Game);
            Assert.Equal(ChallengeRequestState.Status.Started, request.State);
        }

        [PostgreSqlFact]
        public async Task LeaderBoardChunkAndALeaderBoardNamedLikeItKeepTheirOwnState()
        {
            string gameKey = Guid.NewGuid().ToString();
            string l1 = ResourcesUtility.GetLeaderBoardKey(gameKey, "l1");
            string l10 = ResourcesUtility.GetLeaderBoardKey(gameKey, "l10");
            // Chunk 0 of l1 used to have the key of the leaderboard l10 ("{game}_L_l1" + "0").
            var chunk = GrainFactory.GetGrain<ILeaderBoardStorage>(ResourcesUtility.GetLeaderBoardChunkKey(l1, 0));
            var board = GrainFactory.GetGrain<ILeaderBoard>(l10);

            await board.Initialize("l10", DateTime.Now, DateTime.MaxValue, false).WaitAsync(TestSetup.Timeout);
            await chunk.WriteEntries(new List<LeaderBoardEntry> { new("player-1", "Player One", 42, DateTime.UtcNow) })
                .WaitAsync(TestSetup.Timeout);

            await fixture.Cluster.Deactivate(board);
            await fixture.Cluster.Deactivate(chunk);

            var info = await board.GetInfo().WaitAsync(TestSetup.Timeout);
            Assert.Equal("l10", info.Name);
            Assert.False(info.AcceptBetterScoresOnly);
            var entry = Assert.Single(await chunk.GetEntries().WaitAsync(TestSetup.Timeout));
            Assert.Equal("player-1", entry.PlayerID);
            Assert.Equal(42, entry.Score);
        }

        [PostgreSqlFact]
        public async Task ValuesEqualToTheirTypeDefaultSurviveDeactivation()
        {
            // The Orleans JSON settings used to leave out false, 0 and null, so a property whose
            // constructor sets another value (GameMode.FinishGameWithOneDisconnect = true,
            // LeaderBoardState.AcceptBetterScoresOnly = true) came back with that value instead.
            var manager = GrainFactory.GetGrain<IGameModeManager>(Guid.NewGuid());
            await manager.AddOrUpdateGameMode(new GameMode
            {
                Name = "duel",
                MinPlayers = 2,
                MaxPlayers = 2,
                FinishGameWithOneDisconnect = false,
                Criterias = new List<GameMode.MatchMakingCriteria> { new() { AcceptableThreshold = 0, StartTimeInSeconds = 0 } },
            }).WaitAsync(TestSetup.Timeout);

            await fixture.Cluster.Deactivate(manager);

            var mode = await manager.GetGameModeData("duel").WaitAsync(TestSetup.Timeout);
            Assert.False(mode.FinishGameWithOneDisconnect);
            Assert.Equal(0, Assert.Single(mode.Criterias).AcceptableThreshold);
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
