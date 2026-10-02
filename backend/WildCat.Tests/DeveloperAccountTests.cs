using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The grain methods behind the admin API's account flows: registration, activation and reset
    /// codes, the developer search and the game owner index.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class DeveloperAccountTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;
        private IDeveloperManager Manager => GrainFactory.GetGrain<IDeveloperManager>("main");

        public DeveloperAccountTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static string UniqueEmail() => $"{TestSetup.UniqueId("acct")}@example.com";

        [Fact]
        public async Task RegistrationCreatesAnInactiveDeveloperWithAHashedPassword()
        {
            string email = UniqueEmail();
            var registration = await Manager.RegisterDeveloper(email, "a long password", "Ada", "Lovelace");
            Assert.NotNull(registration);
            Assert.Equal(10, registration.ActivationCode.Length);

            var developer = GrainFactory.GetGrain<IDeveloper>(registration.Key);
            var data = await developer.GetDeveloperData();
            Assert.Equal(DeveloperState.DeveloperStatus.NotActivated, data.Status);
            Assert.StartsWith("pbkdf2-sha512$", data.PasswordHash);
            Assert.NotEqual(registration.ActivationCode, data.ActivationCode.Hash);
            Assert.True(await developer.IsPasswordCorrect("a long password"));
            Assert.Equal(registration.Key, (await Manager.GetDeveloper(email)).GetPrimaryKey());

            Assert.Null(await Manager.RegisterDeveloper(email.ToUpperInvariant(), "another password", null, null));
            await Assert.ThrowsAsync<ArgumentException>(() => Manager.RegisterDeveloper(UniqueEmail(), "", null, null));
        }

        [Fact]
        public async Task ActivationCodeActivatesAndDiesAfterFiveWrongTries()
        {
            var registration = await Manager.RegisterDeveloper(UniqueEmail(), "a long password", null, null);
            var developer = GrainFactory.GetGrain<IDeveloper>(registration.Key);
            for (int i = 0; i < 5; i++)
                Assert.False(await developer.Activate("WRONGCODE2"));
            Assert.False(await developer.Activate(registration.ActivationCode));

            string code = await developer.CreateActivationCode();
            Assert.True(await developer.Activate(" " + code.ToLowerInvariant() + " "));
            Assert.Equal(DeveloperState.DeveloperStatus.Normal, (await developer.GetDeveloperData()).Status);
            Assert.Null(await developer.CreateActivationCode());
        }

        [Fact]
        public async Task AnActiveAccountStillChecksTheActivationCode()
        {
            var registration = await Manager.RegisterDeveloper(UniqueEmail(), "a long password", null, null);
            var developer = GrainFactory.GetGrain<IDeveloper>(registration.Key);
            Assert.True(await developer.Activate(registration.ActivationCode));

            // Any other code fails as it does for an inactive account, so the answer does not tell a
            // caller without the code that the account is active.
            Assert.False(await developer.Activate("anything"));
            Assert.False(await developer.Activate("WRONGCODE2"));
            // The code that activated the account works again (a second click on the emailed link),
            // also after a reactivation.
            Assert.True(await developer.Activate(registration.ActivationCode));
            await cluster.Deactivate(developer);
            Assert.True(await developer.Activate(registration.ActivationCode.ToLowerInvariant()));
            Assert.Equal(DeveloperState.DeveloperStatus.Normal, (await developer.GetDeveloperData()).Status);

            // Wrong tries still count: after five the code stops working, and the account stays active.
            for (int i = 0; i < 3; i++)
                Assert.False(await developer.Activate("WRONGCODE2"));
            Assert.False(await developer.Activate(registration.ActivationCode));
            Assert.Null((await developer.GetDeveloperData()).ActivationCode);
            Assert.Equal(DeveloperState.DeveloperStatus.Normal, (await developer.GetDeveloperData()).Status);

            // A developer created without registration never had a code.
            var seeded = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory));
            Assert.False(await seeded.Activate("anything"));
        }

        [Fact]
        public async Task PasswordResetCodeIsSingleUseAndSurvivesReactivation()
        {
            var developerKey = await TestSetup.CreateDeveloper(GrainFactory, "old password");
            var developer = GrainFactory.GetGrain<IDeveloper>(developerKey);
            string first = await developer.CreatePasswordResetCode();
            string second = await developer.CreatePasswordResetCode();
            Assert.False(await developer.ConsumePasswordResetCode(first));

            await cluster.Deactivate(developer);
            Assert.True(await developer.ConsumePasswordResetCode(second));
            Assert.False(await developer.ConsumePasswordResetCode(second));
            Assert.True(await developer.IsPasswordCorrect("old password"));
        }

        [Fact]
        public async Task ChangeNameUpdatesTheProfile()
        {
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory));
            await developer.ChangeName("Grace", "Hopper");
            var data = await developer.GetDeveloperData();
            Assert.Equal("Grace", data.FirstName);
            Assert.Equal("Hopper", data.LastName);
        }

        [Fact]
        public async Task SearchDevelopersPagesThroughMatchingEmails()
        {
            string tag = TestSetup.UniqueId("search");
            var keys = new List<Guid>();
            foreach (var prefix in new[] { "c", "a", "b" })
                keys.Add((await Manager.RegisterDeveloper($"{prefix}-{tag}@example.com", "a long password", null, null)).Key);

            var firstPage = await Manager.SearchDevelopers(tag.ToUpperInvariant(), 0, 2);
            Assert.Equal(3, firstPage.Total);
            Assert.Equal(new[] { $"a-{tag}@example.com", $"b-{tag}@example.com" }, firstPage.Items.Select(x => x.Email));
            Assert.Equal(keys[1], firstPage.Items[0].Key);

            var secondPage = await Manager.SearchDevelopers(tag, 2, 2);
            Assert.Equal($"c-{tag}@example.com", Assert.Single(secondPage.Items).Email);
        }

        [Fact]
        public async Task GameOwnerIndexRecordsAndForgetsOwners()
        {
            Guid game = Guid.NewGuid();
            Guid owner = Guid.NewGuid();
            Assert.Equal(Guid.Empty, await Manager.GetGameOwner(game));
            await Manager.SetGameOwner(game, owner);
            Assert.Equal(owner, await Manager.GetGameOwner(game));
            await Manager.RemoveGameOwner(game);
            Assert.Equal(Guid.Empty, await Manager.GetGameOwner(game));
        }

        [Fact]
        public async Task ResourceManagerRemovesByKeyAndLeaderBoardReportsItsSettings()
        {
            string game = Guid.NewGuid().ToString();
            var manager = GrainFactory.GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(game));
            var board = GrainFactory.GetGrain<ILeaderBoard>(ResourcesUtility.GetLeaderBoardKey(game, "daily"));
            await board.Initialize("daily", DateTime.UtcNow, DateTime.MaxValue, false);
            Assert.True(await manager.AddResource(board));

            var info = await board.GetInfo();
            Assert.Equal("daily", info.Name);
            Assert.False(info.AcceptBetterScoresOnly);

            Assert.True(await manager.RemoveResourceByKey(await board.GetKey()));
            Assert.False(await manager.RemoveResourceByKey(await board.GetKey()));
            Assert.False(await manager.HasResource(await board.GetKey()));
        }
    }
}
