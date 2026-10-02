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
        /// Starts <paramref name="hashing"/>, sends <paramref name="interleaving"/> an eighth of a
        /// hash time later and times its answer. Had the hash held the grain's turn, that answer
        /// would have waited the rest of it (seven eighths of a hash time) and come after the
        /// hashing call's; off the turn it comes back at once, while the hash is still running.
        /// </summary>
        /// <remarks>
        /// The hash time is measured on the test thread, and on a busy machine the grain's hash can
        /// run faster than that. Then the hashing call may be done before the interleaving call is
        /// even sent, and the attempt shows nothing either way, so it is tried again (the callers
        /// stay below the Player grain's five wrong passwords before a lockout). With the hash on the
        /// grain's turn every attempt fails.
        /// </remarks>
        private static async Task AssertAnsweredDuringTheHash(Func<Task> hashing, Func<Task> interleaving, TimeSpan hashTime, string call)
        {
            const int Attempts = 3;
            var seen = new List<string>();
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                Task hashed = hashing();
                await Task.Delay(hashTime / 8);
                var watch = Stopwatch.StartNew();
                await interleaving().WaitAsync(TestSetup.Timeout);
                TimeSpan callTime = watch.Elapsed;
                bool hashStillRunning = !hashed.IsCompleted;
                await hashed.WaitAsync(TestSetup.Timeout);
                if (hashStillRunning && callTime < hashTime / 2)
                    return;
                seen.Add($"{call} took {callTime.TotalMilliseconds} ms, "
                         + (hashStillRunning ? "while the hash was still running" : "after the hashing call was done"));
            }
            Assert.Fail($"One hash takes {hashTime.TotalMilliseconds} ms. {string.Join("; ", seen)}.");
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
            await AssertAnsweredDuringTheHash(
                async () => Assert.Null(await player.LoginWithUserNameAndPassword(user, "wrong-password", sessionKey, Guid.NewGuid())),
                () => player.GetState(), hashTime, "GetState");
        }

        [Fact]
        public async Task PlayerPasswordChangeHashesOffTheGrainTurn()
        {
            var hashTime = MeasureHashTime();
            var (player, user) = await CreateUserPasswordPlayer("first-password");
            Assert.True(await player.ChangePasswordForUserId(user, "warm-up-password"));
            await player.GetState();

            await AssertAnsweredDuringTheHash(
                async () => Assert.True(await player.ChangePasswordForUserId(user, TestSetup.UniqueId("password"))),
                () => player.GetState(), hashTime, "GetState");
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

            await AssertAnsweredDuringTheHash(
                async () => Assert.Null(await player.LoginWithEmail(email, "wrong-password", sessionKey, Guid.NewGuid())),
                () => player.GetState(), hashTime, "GetState");
        }

        [Fact]
        public async Task DeveloperPasswordCheckRunsOffTheGrainTurn()
        {
            var hashTime = MeasureHashTime();
            var developer = GrainFactory.GetGrain<IDeveloper>(await TestSetup.CreateDeveloper(GrainFactory, "right-password"));
            var secret = (await developer.GetDeveloperData()).DeveloperSecret;
            Assert.False(await developer.IsPasswordCorrect("wrong-password"));
            Assert.True(await developer.IsSecretValid(secret));

            await AssertAnsweredDuringTheHash(
                async () => Assert.False(await developer.IsPasswordCorrect("wrong-password")),
                async () => Assert.True(await developer.IsSecretValid(secret)), hashTime, "IsSecretValid");
            Assert.True(await developer.IsPasswordCorrect("right-password"));
        }
    }
}
