using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The memory grain storage of the Memory provider (the default host setting and the test
    /// cluster) stores state as JSON with the same Orleans settings as the ADO.NET storage, so it
    /// needs the same fix: values equal to their type's default have to be written, or a property
    /// whose constructor sets another value comes back with that value after a deactivation.
    /// PostgreSqlStorageTests checks the same for the PostgreSQL provider.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class MemoryStorageTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public MemoryStorageTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task ValuesEqualToTheirTypeDefaultSurviveDeactivation()
        {
            // GameMode.FinishGameWithOneDisconnect is true unless set otherwise.
            var manager = GrainFactory.GetGrain<IGameModeManager>(Guid.NewGuid());
            await manager.AddOrUpdateGameMode(new GameMode
            {
                Name = "duel",
                MinPlayers = 2,
                MaxPlayers = 2,
                FinishGameWithOneDisconnect = false,
                Criterias = new List<GameMode.MatchMakingCriteria> { new() { AcceptableThreshold = 0, StartTimeInSeconds = 0 } },
            });

            // LeaderBoardState.AcceptBetterScoresOnly is true unless set otherwise.
            var board = GrainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(Guid.NewGuid().ToString(), "l1"));
            await board.Initialize("l1", DateTime.Now, DateTime.MaxValue, false);

            await cluster.Deactivate(manager);
            await cluster.Deactivate(board);

            var mode = await manager.GetGameModeData("duel");
            Assert.False(mode.FinishGameWithOneDisconnect);
            Assert.Equal(0, Assert.Single(mode.Criterias).AcceptableThreshold);
            Assert.False((await board.GetInfo()).AcceptBetterScoresOnly);
        }

        [Fact]
        public async Task AMatchThatGoesOnAfterADisconnectStillDoesAfterADeactivation()
        {
            var match = await MatchTestSetup.StartAsync(GrainFactory, MatchTestSetup.Mode(false));

            await cluster.Deactivate(match.Game);
            Assert.False((await match.Game.GetState()).Mode.FinishGameWithOneDisconnect);

            // The mode used to come back as "finish with one disconnect", which ended the match.
            await match.A.Disconnect();
            Assert.Equal(RelayServerGameStatus.SomeJoined, await match.Game.GetGameState());
            Assert.Equal(RelayServerPlayerGameStatus.InGame, await match.Game.GetPlayerStatus(match.B.Player));
        }
    }
}
