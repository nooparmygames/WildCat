using System.Diagnostics;
using Orleans;
using Orleans.TestingHost;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// PBKDF2 takes a few hundred milliseconds on purpose. The Player and Developer grains run it
    /// on the thread pool, so the grain's turn is not held for that long: a call that may interleave
    /// (IPlayer.GetState, IDeveloper.IsSecretValid) is answered while the hashing is still going.
    /// When the hashing ran inline, such a call had to wait for the whole turn.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class PasswordHashingTurnTests
    {
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public PasswordHashingTurnTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        /// <summary>How long one hash takes on this machine, measured after a warm-up.</summary>
        private static TimeSpan MeasureHashTime()
        {
            PasswordHasher.Hash("warm-up");
            var watch = Stopwatch.StartNew();
            PasswordHasher.Hash("measure");
            return watch.Elapsed;
        }

        /// <summary>
        /// The interleaving call was sent an eighth of a hash time into the call that hashes. Had
        /// the hash held the grain's turn, the answer would have waited the rest of it (seven
        /// eighths of a hash time); off the turn it comes back at once.
        /// </summary>
        private static void AssertAnsweredDuringTheHash(TimeSpan callTime, TimeSpan hashTime, string call)
        {
            Assert.True(callTime < hashTime / 2,
                $"{call} took {callTime.TotalMilliseconds} ms while one hash takes {hashTime.TotalMilliseconds} ms");
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
            var hashTime = MeasureHashTime();
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var (player, user) = await CreateUserPasswordPlayer("right-password");
            // Warm up the call path so the measured call starts right away.
            Assert.Null(await player.LoginWithUserNameAndPassword(user, "wrong-password", sessionKey, Guid.NewGuid()));
            await player.GetState();

            // A wrong password: the whole login is the password check, with no other await in it.
            var login = player.LoginWithUserNameAndPassword(user, "wrong-password", sessionKey, Guid.NewGuid());
            await Task.Delay(hashTime / 8);
            var watch = Stopwatch.StartNew();
            await player.GetState().WaitAsync(TestSetup.Timeout);
            var getStateTime = watch.Elapsed;

            AssertAnsweredDuringTheHash(getStateTime, hashTime, "GetState");
            Assert.False(login.IsCompleted);
            Assert.Null(await login.WaitAsync(TestSetup.Timeout));
        }

        [Fact]
        public async Task PlayerPasswordChangeHashesOffTheGrainTurn()
        {
            var hashTime = MeasureHashTime();
            var (player, user) = await CreateUserPasswordPlayer("first-password");
            Assert.True(await player.ChangePasswordForUserId(user, "warm-up-password"));
            await player.GetState();

            var change = player.ChangePasswordForUserId(user, "second-password");
            await Task.Delay(hashTime / 8);
            var watch = Stopwatch.StartNew();
            await player.GetState().WaitAsync(TestSetup.Timeout);
            var getStateTime = watch.Elapsed;

            Assert.True(await change.WaitAsync(TestSetup.Timeout));
            AssertAnsweredDuringTheHash(getStateTime, hashTime, "GetState");
        }

        [Fact]
        public async Task PlayerEmailLoginChecksThePasswordOffTheGrainTurn()
        {
            var hashTime = MeasureHashTime();
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, "right-password");
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            await player.Activate((await player.GetState()).ActivationCodes[email].Code);
            Assert.Null(await player.LoginWithEmail(email, "wrong-password", sessionKey, Guid.NewGuid()));

            var login = player.LoginWithEmail(email, "wrong-password", sessionKey, Guid.NewGuid());
            await Task.Delay(hashTime / 8);
            var watch = Stopwatch.StartNew();
            await player.GetState().WaitAsync(TestSetup.Timeout);
            var getStateTime = watch.Elapsed;

            AssertAnsweredDuringTheHash(getStateTime, hashTime, "GetState");
            Assert.False(login.IsCompleted);
            Assert.Null(await login.WaitAsync(TestSetup.Timeout));
        }

        [Fact]
        public async Task DeveloperPasswordCheckRunsOffTheGrainTurn()
        {
            var hashTime = MeasureHashTime();
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory, "right-password"));
            var secret = (await developer.GetDeveloperData()).DeveloperSecret;
            Assert.False(await developer.IsPasswordCorrect("wrong-password"));
            Assert.True(await developer.IsSecretValid(secret));

            var check = developer.IsPasswordCorrect("wrong-password");
            await Task.Delay(hashTime / 8);
            var watch = Stopwatch.StartNew();
            Assert.True(await developer.IsSecretValid(secret).WaitAsync(TestSetup.Timeout));
            var secretCheckTime = watch.Elapsed;

            AssertAnsweredDuringTheHash(secretCheckTime, hashTime, "IsSecretValid");
            Assert.False(check.IsCompleted);
            Assert.False(await check.WaitAsync(TestSetup.Timeout));
            Assert.True(await developer.IsPasswordCorrect("right-password"));
        }
    }
}
