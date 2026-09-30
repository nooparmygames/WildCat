using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// GetLeaderboard (26) over real WebSockets: the Top, Around and Me queries, on an empty board
    /// and on one with scores (set directly on the leaderboard grain, as a server script would),
    /// and the checks of the request. SetLeaderboard (28) is in <see cref="GatewayInventoryHandlerTests"/>.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayLeaderboardHandlerTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayLeaderboardHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private ILeaderBoard Board(Guid gameKey, string name)
        {
            return GrainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), name));
        }

        private static Task<OptionalResponse<GetLeaderBoardResponse>> Query(GatewayPlayer player, string board,
            GetLeaderBoardRequest.RequestType type, int count = 0, int offset = 0)
        {
            return player.CallAsync<GetLeaderBoardResponse>(WildCatMsgType.GetLeaderboardRequest,
                new GetLeaderBoardRequest { leaderBoardName = board, type = type, count = count, offset = offset },
                WildCatMsgType.GetLeaderboardResponse);
        }

        /// <summary>Gives players named after their scores those scores on the board; returns their ids by score.</summary>
        private async Task<Dictionary<int, string>> AddScores(ILeaderBoard board, params int[] scores)
        {
            var ids = new Dictionary<int, string>();
            foreach (int score in scores)
            {
                var player = GrainFactory.GetGrain<IPlayer>(TestSetup.UniqueId("scorer"));
                await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
                await player.SetPlayerName($"p{score}");
                await board.AddPlayerScore(player, score);
                ids[score] = player.GetPrimaryKeyString();
            }
            return ids;
        }

        [Fact]
        public async Task AnEmptyBoardAnswersEveryQueryWithItsType()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);

            // The SDK told these apart by which fields were filled, which failed for all three.
            var me = await Query(alice, "empty", GetLeaderBoardRequest.RequestType.Me);
            Assert.True(me.isSuccesful, me.errorMsg);
            Assert.True(me.msg.IsPlayerRank);
            Assert.Equal(-1, me.msg.playerRank);

            var around = await Query(alice, "empty", GetLeaderBoardRequest.RequestType.Around, count: 5);
            Assert.True(around.msg.IsAroundMeResult);
            Assert.Equal(-1, around.msg.playerRank);
            Assert.Empty(around.msg.entries);

            var top = await Query(alice, "empty", GetLeaderBoardRequest.RequestType.Top, count: 5);
            Assert.True(top.msg.IsTopResult);
            Assert.Equal(0, top.msg.count);
            Assert.Empty(top.msg.entries);
            Assert.Equal(-1, top.msg.playerRank);
        }

        [Fact]
        public async Task TopAroundAndRankReadTheSessionsGameBoard()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            await GrainFactory.GetGrain<IPlayer>(alice.Id).SetPlayerName("Alice");
            var board = Board(gameKey, "weekly");
            var others = await AddScores(board, 50, 40, 20, 10);
            await board.AddPlayerScore(GrainFactory.GetGrain<IPlayer>(alice.Id), 30);

            var top = await Query(alice, "weekly", GetLeaderBoardRequest.RequestType.Top, count: 3);
            Assert.True(top.isSuccesful, top.errorMsg);
            Assert.True(top.msg.IsTopResult);
            Assert.Equal(5, top.msg.count);
            Assert.Equal(new[] { 50, 40, 30 }, top.msg.entries.Select(e => e.score));
            Assert.Equal(new[] { others[50], others[40], alice.Id }, top.msg.entries.Select(e => e.playerId));
            Assert.Equal(new[] { "p50", "p40", "Alice" }, top.msg.entries.Select(e => e.userName));
            Assert.All(top.msg.entries, e => Assert.True(DateTime.UtcNow - e.entryTime < TimeSpan.FromMinutes(5)));

            var secondPage = await Query(alice, "weekly", GetLeaderBoardRequest.RequestType.Top, count: 3, offset: 3);
            Assert.Equal(new[] { 20, 10 }, secondPage.msg.entries.Select(e => e.score));

            var me = await Query(alice, "weekly", GetLeaderBoardRequest.RequestType.Me);
            Assert.True(me.msg.IsPlayerRank);
            Assert.Equal(2, me.msg.playerRank);
            Assert.Empty(me.msg.entries);

            var around = await Query(alice, "weekly", GetLeaderBoardRequest.RequestType.Around, count: 3);
            Assert.True(around.msg.IsAroundMeResult);
            Assert.Equal(2, around.msg.playerRank);
            Assert.Equal(new[] { 40, 30, 20 }, around.msg.entries.Select(e => e.score));

            // A board of the same name in another game is another board.
            var (otherDeveloper, otherGame) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var bob = await GatewayPlayer.ConnectAsync(host, otherDeveloper, otherGame);
            var elsewhere = await Query(bob, "weekly", GetLeaderBoardRequest.RequestType.Top, count: 3);
            Assert.Equal(0, elsewhere.msg.count);
        }

        [Fact]
        public async Task CountsAreCheckedAndCapped()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var alice = await GatewayPlayer.ConnectAsync(host, developerKey, gameKey);
            var board = Board(gameKey, "big");
            await AddScores(board, Enumerable.Range(1, GetLeaderboardHandler.MaxEntries + 5).ToArray());

            var top = await Query(alice, "big", GetLeaderBoardRequest.RequestType.Top, count: int.MaxValue);
            Assert.True(top.isSuccesful, top.errorMsg);
            Assert.Equal(GetLeaderboardHandler.MaxEntries + 5, top.msg.count);
            Assert.Equal(GetLeaderboardHandler.MaxEntries, top.msg.entries.Count);

            foreach (var (count, offset) in new[] { (-1, 0), (3, -1) })
            {
                var negative = await Query(alice, "big", GetLeaderBoardRequest.RequestType.Top, count, offset);
                Assert.False(negative.isSuccesful);
                Assert.Equal(GatewayErrors.Refused, negative.errorCode);
                Assert.Equal(GetLeaderboardHandler.NegativeRange, negative.errorMsg);
            }

            var unknown = await Query(alice, "big", (GetLeaderBoardRequest.RequestType)7, count: 3);
            Assert.False(unknown.isSuccesful);
            Assert.Equal(GetLeaderboardHandler.UnknownQueryType, unknown.errorMsg);
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        }

        [Fact]
        public async Task GetLeaderboardNeedsALoggedInPlayer()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var request = new GetLeaderBoardRequest { leaderBoardName = "l1", type = GetLeaderBoardRequest.RequestType.Me };

            var noTicket = await client.CallAsync<EmptyMessage>(WildCatMsgType.GetLeaderboardRequest, request, WildCatMsgType.ErrorMsg);
            Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
            var notLoggedIn = await client.CallAsync<EmptyMessage>(WildCatMsgType.GetLeaderboardRequest, request,
                WildCatMsgType.ErrorMsg, session.ToString());
            Assert.Equal(GatewayErrors.NotLoggedIn, notLoggedIn.errorMsg);
        }
    }
}
