using System;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>Cloud data, leaderboards through a script event, and inventory.</summary>
    [Collection(ServerCollection.Name)]
    public class DataTests : ServerTestBase
    {
        public DataTests(ServerFixture server)
            : base(server)
        {
        }

        [Fact]
        public async Task CloudDataRoundTripsAndPublicDataIsReadableByOthers()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, aliceLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var (bob, _) = await LoggedInClientAsync(developerKey, gameKey);

            await Wait.For(alice.CloudData.SetAsync("progress", "level 3"));
            Assert.Equal("level 3", await Wait.For(alice.CloudData.GetAsync("progress")));

            Assert.Null(await Wait.For(alice.CloudData.GetPublicAsync("motto")));
            await Wait.For(alice.CloudData.SetPublicAsync("motto", "fast"));
            Assert.Equal("fast", await Wait.For(alice.CloudData.GetPublicAsync("motto")));
            Assert.Equal("fast", await Wait.For(bob.CloudData.GetPlayerPublicAsync(aliceLogin.PlayerId, "motto")));
            Assert.Null(await Wait.For(bob.CloudData.GetPlayerPublicAsync(aliceLogin.PlayerId, "missing")));

            await Assert.ThrowsAsync<ArgumentException>(() => alice.CloudData.SetAsync(new string('k', 101), "x"));
        }

        [Fact]
        public async Task ScoresSetByAScriptEventShowInTheLeaderboard()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, login) = await LoggedInClientAsync(developerKey, gameKey);
            string board = Unique.Id("board");

            // An empty board answers (the old SDK never completed a Top query on an empty board).
            LeaderboardPage empty = await Wait.For(client.Leaderboards.GetTopAsync(board, 0, 10));
            Assert.Empty(empty.Entries);

            // Clients cannot set scores; a server script event does, deciding the score itself.
            var compiler = Server.Grains.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey.ToString(), "submitScore"));
            await compiler.Initialize("submitScore",
                new[] { new EventParameter { Type = EventParameter.ParamType.IntParam } }.ToList(),
                "async Task<int> submitScore(int score) { await WildCat.SetScoreInLeaderBoard(WildCat.CurrentPlayer, \"" + board
                + "\", Math.Min(score, 1000)); return await WildCat.GetPlayerRankInLeaderBoard(WildCat.CurrentPlayer, \"" + board + "\"); }")
                .WaitAsync(Wait.Long);

            int rank = await Wait.For(client.Scripts.ExecuteAsync<int>("submitScore", 5000));
            LeaderboardPage top = await Wait.For(client.Leaderboards.GetTopAsync(board, 0, 10));
            LeaderboardEntry entry = Assert.Single(top.Entries);
            Assert.Equal(login.PlayerId, entry.PlayerId);
            Assert.Equal(1000, entry.Score);
            Assert.Equal(1, top.TotalCount);
            Assert.Equal(rank, await Wait.For(client.Leaderboards.GetRankAsync(board)));
            LeaderboardPage around = await Wait.For(client.Leaderboards.GetAroundMeAsync(board, 5));
            Assert.Equal(rank, around.PlayerRank);
            Assert.Single(around.Entries);

            await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Scripts.ExecuteAsync("noSuchEvent")));
            // Only the wire's value types can be arguments; anything else fails before sending.
            await Assert.ThrowsAsync<ArgumentException>(() => client.Scripts.ExecuteAsync("submitScore", new object()));
        }

        [Fact]
        public async Task InventoryReadsAndRefusesToSpendWhatIsNotThere()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, _) = await LoggedInClientAsync(developerKey, gameKey);

            Assert.Empty(await Wait.For(client.Inventory.GetAsync()));
            var error = await Assert.ThrowsAsync<WildCatException>(() =>
                Wait.For(client.Inventory.RemoveAsync("", new[] { new InventoryItem("sword", 1) })));
            Assert.Equal(WildCatErrorCodes.Refused, error.ErrorCode);
        }
    }
}
