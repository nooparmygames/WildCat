using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Holds password work on its worker while checking that an interleavable grain call
    /// completes. No assertion depends on the speed of PBKDF2 or a scheduler delay.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class PasswordHashingTurnTests
    {
        private readonly InProcessTestCluster cluster;
        private readonly ControlledPasswordHashing passwordHashing;
        private IGrainFactory GrainFactory => cluster.Client;

        public PasswordHashingTurnTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
            passwordHashing = fixture.PasswordHashing;
        }

        private async Task AssertAnsweredDuringTheHash(string password, Func<Task> hashing, Func<Task> interleaving)
        {
            using var gate = passwordHashing.Pause(password);
            Task hashed = hashing();
            try
            {
                await gate.Started.WaitAsync(TestSetup.Timeout);
                await interleaving().WaitAsync(TestSetup.Timeout);
                Assert.False(hashed.IsCompleted, "Password work completed before the test released it.");
            }
            finally
            {
                gate.Dispose();
                await hashed.WaitAsync(TestSetup.Timeout);
            }
        }

        private async Task<(IPlayer Player, string User)> CreateUserPasswordPlayer(string password)
        {
            string user = TestSetup.UniqueId("user");
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(user, password);
            Assert.Equal(AdditionResult.Success, created.result);
            return (GrainFactory.GetGrain<IPlayer>(created.Account), user);
        }

        [Fact]
        public async Task PlayerLoginChecksThePasswordOffTheGrainTurn()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var (player, user) = await CreateUserPasswordPlayer("right-password");
            string password = TestSetup.UniqueId("wrong-password");
            await AssertAnsweredDuringTheHash(password,
                async () => Assert.Null(await player.LoginWithUserNameAndPassword(user, password, sessionKey, Guid.NewGuid())),
                () => player.GetState());
        }

        [Fact]
        public async Task PlayerPasswordChangeHashesOffTheGrainTurn()
        {
            var (player, user) = await CreateUserPasswordPlayer("first-password");
            string password = TestSetup.UniqueId("new-password");

            await AssertAnsweredDuringTheHash(password,
                async () => Assert.True(await player.ChangePasswordForUserId(user, password)),
                () => player.GetState());
        }

        [Fact]
        public async Task PlayerEmailLoginChecksThePasswordOffTheGrainTurn()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, "right-password");
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            await player.Activate((await player.GetState()).ActivationCodes[email].Code);
            string password = TestSetup.UniqueId("wrong-password");

            await AssertAnsweredDuringTheHash(password,
                async () => Assert.Null(await player.LoginWithEmail(email, password, sessionKey, Guid.NewGuid())),
                () => player.GetState());
        }

        [Fact]
        public async Task DeveloperPasswordCheckRunsOffTheGrainTurn()
        {
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory, "right-password"));
            var secret = (await developer.GetDeveloperData()).DeveloperSecret;
            string password = TestSetup.UniqueId("wrong-password");

            await AssertAnsweredDuringTheHash(password,
                async () => Assert.False(await developer.IsPasswordCorrect(password)),
                async () => Assert.True(await developer.IsSecretValid(secret)));
            Assert.True(await developer.IsPasswordCorrect("right-password"));
        }
    }
}
