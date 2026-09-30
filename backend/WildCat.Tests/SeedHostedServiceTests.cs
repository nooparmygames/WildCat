using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The startup seed that replaced devsconfig.json.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class SeedHostedServiceTests
    {
        private readonly ClusterFixture fixture;
        private IGrainFactory GrainFactory => fixture.Cluster.Client;

        public SeedHostedServiceTests(ClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public async Task SeedCreatesDeveloperGameAndLeaderBoardOnceAndSessionsWork()
        {
            var seed = new SeedDeveloper
            {
                Key = Guid.NewGuid(),
                Name = "Seed",
                Email = $"{TestSetup.UniqueId("seed")}@example.com",
                Games = { new SeedGame { Key = Guid.NewGuid(), Name = "SeedGame" } },
            };
            var service = CreateService(seed);

            await service.StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);
            // A second start (the next process start) finds everything and changes nothing.
            await service.StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);

            var developer = await GrainFactory.GetGrain<IDeveloperManager>("main").GetDeveloper(seed.Email);
            Assert.NotNull(developer);
            Assert.Equal(seed.Key, developer.GetPrimaryKey());
            var game = Assert.Single(await developer.GetGames());
            Assert.Equal(seed.Games[0].Key, game.Key);
            Assert.Equal("SeedGame", game.Name);

            string gameKey = seed.Games[0].Key.ToString();
            var resources = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey));
            Assert.True(await resources.HasResource(ResourcesUtility.GetLeaderBoardKey(gameKey, SeedHostedService.LeaderBoardName)));

            Guid ticket = await developer.InitializeSession(seed.Games[0].Key);
            Assert.NotEqual(Guid.Empty, ticket);
        }

        [Fact]
        public async Task SeedAddsNewGamesToAnExistingDeveloper()
        {
            var seed = new SeedDeveloper
            {
                Key = Guid.NewGuid(),
                Name = "Seed",
                Email = $"{TestSetup.UniqueId("seed")}@example.com",
                Games = { new SeedGame { Key = Guid.NewGuid(), Name = "First" } },
            };
            await CreateService(seed).StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);

            seed.Games.Add(new SeedGame { Key = Guid.NewGuid(), Name = "Second" });
            await CreateService(seed).StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);

            var games = await GrainFactory.GetGrain<IDeveloper>(seed.Key).GetGames();
            Assert.Equal(new[] { "First", "Second" }, games.Select(g => g.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task SeedDoesNotOverwriteADeveloperWhoseKeyHasAnotherEmail()
        {
            var original = new SeedDeveloper { Key = Guid.NewGuid(), Name = "Original", Email = $"{TestSetup.UniqueId("seed")}@example.com" };
            await CreateService(original).StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);

            var clash = new SeedDeveloper { Key = original.Key, Name = "Clash", Email = $"{TestSetup.UniqueId("seed")}@example.com" };
            await CreateService(clash).StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);

            var data = await GrainFactory.GetGrain<IDeveloper>(original.Key).GetDeveloperData();
            Assert.Equal(original.Email, data.Email);
            Assert.Null(await GrainFactory.GetGrain<IDeveloperManager>("main").GetDeveloper(clash.Email));
        }

        [Fact]
        public async Task EmptySeedDoesNothing()
        {
            var service = new SeedHostedService(GrainFactory, Options.Create(new SeedOptions()), NullLogger<SeedHostedService>.Instance);
            await service.StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout);
        }

        [Fact]
        public async Task DeveloperWithoutKeyIsRejected()
        {
            var service = CreateService(new SeedDeveloper { Name = "NoKey", Email = $"{TestSetup.UniqueId("seed")}@example.com" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None).WaitAsync(TestSetup.Timeout));
        }

        private SeedHostedService CreateService(params SeedDeveloper[] developers)
        {
            var options = new SeedOptions { Developers = developers.ToList() };
            return new SeedHostedService(GrainFactory, Options.Create(options), NullLogger<SeedHostedService>.Instance);
        }
    }
}
