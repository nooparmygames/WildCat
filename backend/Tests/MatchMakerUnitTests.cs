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
    public class MatchMakerUnitTests
    {
        private TestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.GrainFactory;
        public MatchMakerUnitTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task FindMatch()
        {
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("user1");
            IPlayer p2 = GrainFactory.GetGrain<IPlayer>("user2");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p1.SetPlayerName("Ashkan");
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await p2.SetPlayerName("Ramin");
            var MM = GrainFactory.GetGrain<IMatchMaker>("30game1");
            var p3 = MM.FindMatch(p1, 10);
            var p4 = MM.FindMatch(p2, 10);
            await Task.WhenAll(p3, p4);
            Assert.True(p3 != null && p4 != null);
            string s;
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.Equal("Ramin", p3.Result.otherPlayerName);
            Assert.Equal("Ashkan", p4.Result.otherPlayerName);
        }

        [Fact]
        public async Task FindMatchForMany()
        {
            int playerCount = 100;
            IPlayer[] players = new IPlayer[playerCount];
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < players.Length; ++i)
            {
                players[i] = GrainFactory.GetGrain<IPlayer>("loadTestPlayer" + i);
                tasks.Add(players[i].CreateAccountWithDevice(players[i].GetPrimaryKeyString()));
            }
            await Task.WhenAll(tasks);
            var MM = GrainFactory.GetGrain<IMatchMaker>("30game1");
            var MMTasks = new List<Task<FoundMatchResult>>();
            for(int i=0;i<players.Length;++i)
            {
                MMTasks.Add(MM.FindMatch(players[i], 10));
            }
            await Task.WhenAll(MMTasks);
            Assert.NotNull(players);
            string s;
            foreach(var t in MMTasks)
            {
                Assert.True(t.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            }
        }

        [Fact]
        public async Task FindMatchWithDelayAndDifferentElo()
        {
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("Xuser3");
            IPlayer p2 = GrainFactory.GetGrain<IPlayer>("Xuser4");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p1.SetPlayerName("Ashkan");
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await p2.SetPlayerName("Ramin");
            await p1.SetEloScore(1300);
            await p2.SetEloScore(1600);
            var MM = GrainFactory.GetGrain<IMatchMaker>("30game1");
            var p3 = MM.FindMatch(p1, 10);
            await Task.Delay(5000);
            var p4 = MM.FindMatch(p2, 10);
            await Task.WhenAll(p3, p4);

            Assert.True(p3 != null && p4 != null);
            string s;
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.Equal("Ramin", p3.Result.otherPlayerName);
            Assert.Equal("Ashkan", p4.Result.otherPlayerName);

            p3 = MM.FindMatch(p1, 10);
            await Task.Delay(5000);
            p4 = MM.FindMatch(p2, 10);
            await Task.WhenAll(p3, p4);
            
            Assert.True(p3 != null && p4 != null);
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.Equal("Ramin", p3.Result.otherPlayerName);
            Assert.Equal("Ashkan", p4.Result.otherPlayerName);
        }
    }
}
