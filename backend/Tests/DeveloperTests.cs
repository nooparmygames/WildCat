using Interfaces;
using Orleans;
using Orleans.Streams;
using Orleans.TestingHost;
using System;
using System.Linq;
using System.Threading.Tasks;
using Tests;
using Xunit;

namespace GrainUnitTests
{
    [Collection(ClusterCollection.Name)]
    public class DeveloperTests 
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public DeveloperTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task InitializeSessionTest()
        {
            var devKey = await GrainFactory.GetGrain<IDeveloperManager>("main").AddDeveloper(new DeveloperState { Email="Something@something.com"});
            var gameKey = await GrainFactory.GetGrain<IDeveloper>(devKey).AddGame(new GameInfo { Name = "game1"});
            var sessionKey = await GrainFactory.GetGrain<IDeveloper>(devKey).InitializeSession(gameKey);
            Assert.NotEqual(sessionKey, Guid.Empty);
        }

        [Fact]
        public async Task AddGameTest()
        {
            var d = Guid.NewGuid();
            await GrainFactory.GetGrain<IDeveloper>(d).CreateDeveloper(new DeveloperState());
            var games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Empty(games);
            var gameKey = await GrainFactory.GetGrain<IDeveloper>(d).AddGame(new GameInfo { Name = "game1" });
            games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Single(games);
            Assert.True(games.First().Key == gameKey && games.First().Name == "game1");
        }

        [Fact]
        public async Task EditGameTest()
        {
            var d = Guid.NewGuid();
            await GrainFactory.GetGrain<IDeveloper>(d).CreateDeveloper(new DeveloperState());
            var games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Empty(games);
            var gameKey = await GrainFactory.GetGrain<IDeveloper>(d).AddGame(new GameInfo { Name = "game1" });
            await GrainFactory.GetGrain<IDeveloper>(d).EditGame(gameKey,new GameInfo { Name = "game2" });
            games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Single(games);
            Assert.True(games.First().Key == gameKey && games.First().Name == "game2");
        }

        [Fact]
        public async Task IsGameValidTest()
        {
            var d = Guid.NewGuid();
            await GrainFactory.GetGrain<IDeveloper>(d).CreateDeveloper(new DeveloperState());
            var games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Empty(games);
            var gameKey = await GrainFactory.GetGrain<IDeveloper>(d).AddGame(new GameInfo { Name = "game1" });
            games = await GrainFactory.GetGrain<IDeveloper>(d).GetGames();
            Assert.Single(games);
            Assert.True(games.First().Key == gameKey && games.First().Name == "game1");
        }
    }
}
