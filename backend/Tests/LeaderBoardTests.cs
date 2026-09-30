using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;


namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class LeaderBoardTests
    {
        private TestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.GrainFactory;
        public LeaderBoardTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        
        [Fact]
        public async Task AddItem()
        {
            var gamekey = Guid.NewGuid().ToString();
            string leaderboardKey = gamekey + "global";
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(leaderboardKey);
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(7), true);
            var player = GrainFactory.GetGrain<IPlayer>("l1");
            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            await player.SetPlayerName("l1");
            var player2 = GrainFactory.GetGrain<IPlayer>("l2");
            await player2.CreateAccountWithDevice(player2.GetPrimaryKeyString());
            await player2.SetPlayerName("l2");
            await leaderboard.AddPlayerScore(player, 100);
            await leaderboard.AddPlayerScore(player2, 102);
            var entries = await leaderboard.GetTopEntries(2, 0);
            Assert.Equal(2, entries.Item2.Count);
            Assert.Equal(102, entries.Item2[0].Score);
            Assert.Equal(100, entries.Item2[1].Score);
            Assert.Equal("l2", entries.Item2[0].Name);
            Assert.Equal("l1", entries.Item2[1].Name);
            Assert.Equal(entries.Item2[0].PlayerID, player2.GetPrimaryKeyString());
            Assert.Equal(entries.Item2[1].PlayerID, player.GetPrimaryKeyString());
        }

        [Fact]
        public async Task GetTopWithHighCount()
        {
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(Guid.NewGuid().ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(7), true);
            var player = GrainFactory.GetGrain<IPlayer>("l3");
            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            var player2 = GrainFactory.GetGrain<IPlayer>("l4");
            await player2.CreateAccountWithDevice(player2.GetPrimaryKeyString());
            await leaderboard.AddPlayerScore(player, 100);
            await leaderboard.AddPlayerScore(player2, 102);
            var entries = await leaderboard.GetTopEntries(5, 0);
            Assert.Equal(2, entries.Item2.Count);
            Assert.True(entries.Item2[0].Score > entries.Item2[1].Score);
        }

        [Fact]
        public async Task GetTopWithEmptyBoard()
        {
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(Guid.NewGuid().ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(7), true);
            var player = GrainFactory.GetGrain<IPlayer>("Xl3");
            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            var player2 = GrainFactory.GetGrain<IPlayer>("Xl4");
            await player2.CreateAccountWithDevice(player2.GetPrimaryKeyString());
            var entries = await leaderboard.GetTopEntries(5, 0);
            Assert.Empty(entries.Item2);
            Assert.Equal(0, entries.Item1);
        }

        [Fact]
        public async Task GetArroundMeWithBigCount()
        {
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(Guid.NewGuid().ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(7), true);
            var player = GrainFactory.GetGrain<IPlayer>("l5");
            await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
            var player2 = GrainFactory.GetGrain<IPlayer>("l6");
            await player2.CreateAccountWithDevice(player2.GetPrimaryKeyString());
            await leaderboard.AddPlayerScore(player, 100);
            await leaderboard.AddPlayerScore(player2, 102);
            var entries = await leaderboard.GetAroundMe(player2, 2);
            Assert.Equal(2, entries.Item2.Count);
        }

        [Fact]
        public async Task GetRanking()
        {
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(Guid.NewGuid() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(30), true);
            List<IPlayer> players = new List<IPlayer>();
            for (int i = 0; i < 10; ++i)
            {
                var player = GrainFactory.GetGrain<IPlayer>("group" + i);
                await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
                await player.SetPlayerName(i.ToString());
                //Add socre in a way which name and rank are related
                ///first player is the last ranking second is 
                /////one before the last ...
                await leaderboard.AddPlayerScore(player, i * 10);
                players.Add(player);
            }
            foreach (var player in players)
            {
                int rank = await leaderboard.GetPlayerRank(player);
                int name = int.Parse((await player.GetPlayerName()));
                Assert.Equal(name, 9 - rank);
            }
        }

        [Fact]
        public async Task AroundMeTest()
        {
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(Guid.NewGuid().ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(3), true);
            List<IPlayer> players = new List<IPlayer>();
            for (int i = 10; i < 20; ++i)
            {
                var player = GrainFactory.GetGrain<IPlayer>("group" + i);
                await player.CreateAccountWithDevice(player.GetPrimaryKeyString());
                //Add socre in a way which name and rank are equal numbers at the end.
                await leaderboard.AddPlayerScore(player, i * 10);
                players.Add(player);
            }
            var aroundMiddle = await leaderboard.GetAroundMe(players[5], 5);
            Assert.Equal(5, aroundMiddle.Item2.Count);
            Assert.Equal(aroundMiddle.Item2[2].PlayerID, players[5].GetPrimaryKeyString());
            Assert.Equal(aroundMiddle.Item2[0].PlayerID, players[7].GetPrimaryKeyString());
            Assert.Equal(aroundMiddle.Item2[4].PlayerID, players[3].GetPrimaryKeyString());
            Assert.Equal(4, aroundMiddle.Item1);

            var aroundWorst = await leaderboard.GetAroundMe(players[0], 5);
            Assert.Equal(5, aroundWorst.Item2.Count);
            Assert.Equal(aroundWorst.Item2[0].PlayerID, players[4].GetPrimaryKeyString());
            Assert.Equal(aroundWorst.Item2[4].PlayerID, players[0].GetPrimaryKeyString());
            Assert.Equal(9, aroundWorst.Item1);

            var aroundTop = await leaderboard.GetAroundMe(players[9], 5);
            Assert.Equal(5, aroundTop.Item2.Count);
            Assert.Equal(aroundTop.Item2[0].PlayerID, players[9].GetPrimaryKeyString());
            Assert.Equal(aroundTop.Item2[4].PlayerID, players[5].GetPrimaryKeyString());
            Assert.Equal(0, aroundTop.Item1);
        }

    }
}
