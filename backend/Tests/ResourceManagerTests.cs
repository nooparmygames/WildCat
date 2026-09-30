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
    public class ResourceManagerTests
    {
        private InProcessTestCluster cluster;
        public IGrainFactory GrainFactory => this.cluster.Client;
        public ResourceManagerTests(ClusterFixture fixture)
        {
            this.cluster = fixture.Cluster;
        }

        [Fact]
        public async Task AddResourceTest()
        {
            Guid gameKey = Guid.NewGuid();
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(gameKey.ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(2), true);
            var manager = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()));
            await manager.AddResource(leaderboard);
            var resources = await manager.GetResources(ResourcesUtility.GetLeaderBoardClassName());
            Assert.Equal(1, resources.Count);
            Assert.NotNull(resources);
            Assert.Equal(resources[0].GetKey().Result, leaderboard.GetKey().Result);
        }

        [Fact]
        public async Task RemoveResourceTest()
        {
            Guid gameKey = Guid.NewGuid();
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(gameKey.ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(2), true);
            var manager = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()));
            await manager.AddResource(leaderboard);
            await manager.RemoveResource(leaderboard);
            var resources = await manager.GetResources(ResourcesUtility.GetLeaderBoardClassName());
            Assert.Empty(resources);
            Assert.NotNull(resources);
        }

        [Fact]
        public async Task HasResourceTest()
        {
            Guid gameKey = Guid.NewGuid();
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(gameKey.ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(2), true);
            var manager = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()));
            await manager.AddResource(leaderboard);
            var hasResource = await manager.HasResource(leaderboard.GetKey().Result);
            Assert.True(hasResource);
            hasResource = await manager.HasResource("dummy");
            Assert.False(hasResource);
        }

        [Fact]
        public async Task GetResourcesTest()
        {
            Guid gameKey = Guid.NewGuid();
            var leaderboard = GrainFactory.GetGrain<ILeaderBoard>(gameKey.ToString() + "global");
            await leaderboard.Initialize("global", DateTime.Now, DateTime.Now.AddDays(2), true);
            var manager = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()));
            var resources = await manager.GetResources(ResourcesUtility.GetLeaderBoardClassName());
            Assert.Empty(resources);
            Assert.NotNull(resources);
            await manager.AddResource(leaderboard);
            resources = await manager.GetResources(ResourcesUtility.GetLeaderBoardClassName());
            Assert.Single(resources);
            Assert.NotNull(resources);
            var leaderBoard2 = GrainFactory.GetGrain<ILeaderBoard>(gameKey.ToString() + "weekly");
            await leaderBoard2.Initialize("global2", DateTime.Now, DateTime.Now.AddDays(2), true);
            await manager.AddResource(leaderBoard2);
            resources = await manager.GetResources(ResourcesUtility.GetLeaderBoardClassName());
            Assert.Equal(2, resources.Count);
            Assert.NotEqual(resources[0].GetKey().Result, resources[1].GetKey().Result);
            string code = await manager.GetResourcesCSharpCode("LeaderBoards");
            Assert.True(!string.IsNullOrEmpty(code));
            Assert.True(GrainFactory.GetGrain<ILeaderBoard>(resources[0].GetPrimaryKeyString()) != null);
        }
    }
}
