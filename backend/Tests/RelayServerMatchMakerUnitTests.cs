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
    public class RelayServerMatchMakerUnitTests
    {
        private TestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.GrainFactory;
        public RelayServerMatchMakerUnitTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }
        [Fact]
        public async Task FindMatchWhichShouldFail()
        {
            Guid gameKey = Guid.NewGuid();
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("user-0");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            var MM = GrainFactory.GetGrain<ICustomMatchMaker>(gameKey, "game1", null);
            System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
            st.Start();
            var t = MM.FindMatch(p1, 100, 10);
            await t;
            st.Stop();
            Assert.Null(t.Result);
            Assert.True(st.Elapsed.Seconds < 15);
            var count = await MM.GetOnlinePlayersCount();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task FindMatch()
        {
            Guid gameKey = Guid.NewGuid();
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("user81");
            IPlayer p2 = GrainFactory.GetGrain<IPlayer>("user82");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            var MM = GrainFactory.GetGrain<ICustomMatchMaker>(gameKey, "game1", null);
            var p3 = MM.FindMatch(p1, 100, 10);
            var p4 = MM.FindMatch(p2, 100, 10);
            await Task.WhenAll(p3, p4);
            Assert.True(p3 != null && p4 != null);
            string s;
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            var count = await MM.GetOnlinePlayersCount();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task FindMatchForFourPlayerGame()
        {
            Guid gameKey = Guid.NewGuid();
            await GrainFactory.GetGrain<IGameModeManager>(gameKey).AddOrUpdateGameMode(
                new GameMode
                {
                    MaxPlayers = 4,
                    MinPlayers = 4,
                    Name = "multi",
                    Criterias = new List<GameMode.MatchMakingCriteria>
                    {
                        new GameMode.MatchMakingCriteria
                        {
                            AcceptableThreshold = 100,
                            StartTimeInSeconds = 0
                        }
                    }
                }
                );
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("user21");
            IPlayer p2 = GrainFactory.GetGrain<IPlayer>("user22");
            IPlayer p3 = GrainFactory.GetGrain<IPlayer>("user23");
            IPlayer p4 = GrainFactory.GetGrain<IPlayer>("user24");

            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            await p3.CreateAccountWithDevice(p3.GetPrimaryKeyString());
            await p4.CreateAccountWithDevice(p4.GetPrimaryKeyString());
            var MM = GrainFactory.GetGrain<ICustomMatchMaker>(gameKey, "multi", null);
            var m1 = MM.FindMatch(p1, 100, 10);
            var m2 = MM.FindMatch(p2, 100, 10);
            var m3 = MM.FindMatch(p3, 100, 10);
            var m4 = MM.FindMatch(p4, 100, 10);
            await Task.WhenAll(m1, m2, m3, m4);
            Assert.True(m1 != null && m2 != null && m3 != null && m4 != null);
            string s = "";
            Assert.True(m1.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(m2.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(m3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(m4.Result.game.GetPrimaryKey(out s) != Guid.Empty);

            var players1 = await m1.Result.game.GetPlayers();
            var players2 = await m2.Result.game.GetPlayers();
            var players3 = await m3.Result.game.GetPlayers();
            var players4 = await m4.Result.game.GetPlayers();

            Assert.True(players1.Count == 4);
            Assert.True(players2.Count == 4);
            Assert.True(players3.Count == 4);
            Assert.True(players4.Count == 4);
            var count = await MM.GetOnlinePlayersCount();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task FindMatchForMany()
        {
            Guid gameKey = Guid.NewGuid();
            int playerCount = 100;
            IPlayer[] players = new IPlayer[playerCount];
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < players.Length; ++i)
            {
                players[i] = GrainFactory.GetGrain<IPlayer>("XloadTestPlayerX" + i);
                tasks.Add(players[i].CreateAccountWithDevice(players[i].GetPrimaryKeyString()));
            }
            await Task.WhenAll(tasks);
            var MM = GrainFactory.GetGrain<ICustomMatchMaker>(gameKey, "game1", null);
            var MMTasks = new List<Task<FoundRelayMatchResult>>();
            for (int i = 0; i < players.Length; ++i)
            {
                MMTasks.Add(MM.FindMatch(players[i], 100, 10));
            }
            await Task.WhenAll(MMTasks);
            Assert.NotNull(players);
            string s;
            foreach (var t in MMTasks)
            {
                Assert.True(t.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            }
            var count = await MM.GetOnlinePlayersCount();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task FindMatchWithDifferentSkills()
        {
            Guid gameKey = Guid.NewGuid();
            IPlayer p1 = GrainFactory.GetGrain<IPlayer>("user3");
            IPlayer p2 = GrainFactory.GetGrain<IPlayer>("user4");
            await p1.CreateAccountWithDevice(p1.GetPrimaryKeyString());
            await p2.CreateAccountWithDevice(p2.GetPrimaryKeyString());
            var MM = GrainFactory.GetGrain<ICustomMatchMaker>(gameKey, "game1", null);
            var p3 = MM.FindMatch(p1, 1300, 10);
            await Task.Delay(3000);
            var p4 = MM.FindMatch(p2, 1600, 10);
            await Task.WhenAll(p3, p4);
            string s;
            Assert.True(p3 != null && p4 != null);
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);

            p3 = MM.FindMatch(p1, 1350, 10);
            await Task.Delay(3000);
            p4 = MM.FindMatch(p2, 1500, 10);
            await Task.WhenAll(p3, p4);

            Assert.True(p3 != null && p4 != null);
            Assert.True(p3.Result.game.GetPrimaryKey(out s) != Guid.Empty);
            Assert.True(p4.Result.game.GetPrimaryKey(out s) != Guid.Empty);
        }
    }
}
