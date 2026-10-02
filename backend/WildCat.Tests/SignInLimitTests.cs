using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Grains;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The limits on guessing passwords and codes beyond the per-connection allowance: a sign-in
    /// allowance per client address shared by all its sockets (a new socket used to come with a
    /// full allowance), and a lockout per account that grows with every wrong password.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class SignInLimitTests
    {
        private const string Password = "a-good-password";

        private readonly InProcessTestCluster cluster;
        private readonly ClusterFixture fixture;
        private IGrainFactory GrainFactory => cluster.Client;

        public SignInLimitTests(ClusterFixture fixture)
        {
            this.fixture = fixture;
            cluster = fixture.Cluster;
        }

        private static async Task<string> SignInError(GatewayTestClient client)
        {
            var reply = await client.RequestAsync(client.AuthenticateRequest(Guid.NewGuid(), "d"));
            Assert.Equal(WildCatMsgType.AuthenticateResponse, reply.msgType);
            return reply.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg;
        }

        [Fact]
        public async Task SignInAttemptsAreLimitedPerAddressAcrossConnections()
        {
            var time = new ManualTimeProvider();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, options =>
            {
                options.AuthAttemptBurst = 5;
                options.AuthAttemptsPerAddressBurst = 3;
                options.AuthAttemptsPerAddressPerMinute = 6;
            }, time, forwardedHeaders: true);

            // Three sockets from one address: each has a fresh allowance of 5, the address one of 3.
            var sockets = new List<GatewayTestClient>();
            try
            {
                for (int i = 0; i < 3; ++i)
                    sockets.Add(await host.ConnectAsync(forwardedFor: "203.0.113.7"));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[0]));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[1]));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[2]));
                Assert.Equal(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[2]));
                Assert.Equal(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[0]));

                // A new socket from the same address gets nothing more.
                sockets.Add(await host.ConnectAsync(forwardedFor: "203.0.113.7"));
                Assert.Equal(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[3]));

                // Another address, and a client without a forwarded address (loopback), are not affected.
                sockets.Add(await host.ConnectAsync(forwardedFor: "198.51.100.20"));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[4]));
                sockets.Add(await host.ConnectAsync());
                for (int i = 0; i < 5; ++i)
                    Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[5]));

                // The address earns an attempt back every 10 seconds.
                time.Advance(TimeSpan.FromSeconds(10));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[3]));
                Assert.Equal(GatewayErrors.TooManyAuthAttempts, await SignInError(sockets[3]));

                // Other requests of a limited address go on.
                var name = await sockets[0].CallAsync<EmptyMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(), WildCatMsgType.ErrorMsg);
                Assert.Equal(GatewayErrors.SessionRequired, name.errorMsg);
            }
            finally
            {
                foreach (var socket in sockets)
                    await socket.DisposeAsync();
            }
        }

        private static AddressAttemptLimiter Limiter(TimeProvider time, int burst, double perMinute = 6)
        {
            var options = Options.Create(new GameGatewayOptions
            {
                AuthAttemptsPerAddressBurst = burst,
                AuthAttemptsPerAddressPerMinute = perMinute,
            });
            return new AddressAttemptLimiter(options, time, NullLogger<AddressAttemptLimiter>.Instance);
        }

        [Fact]
        public void TheAddressLimitCountsIPv6NetworksAndSkipsLoopbackAndUnknownAddresses()
        {
            var limiter = Limiter(new ManualTimeProvider(), burst: 2);

            Assert.True(limiter.TryTake(IPAddress.Parse("2001:db8:1:2::10")));
            Assert.True(limiter.TryTake(IPAddress.Parse("2001:db8:1:2:ffff::1")));
            Assert.False(limiter.TryTake(IPAddress.Parse("2001:db8:1:2:abcd::99")));
            Assert.True(limiter.TryTake(IPAddress.Parse("2001:db8:1:3::10")));

            // An IPv4 address counts the same whether or not it arrives mapped into IPv6.
            Assert.True(limiter.TryTake(IPAddress.Parse("192.0.2.1")));
            Assert.True(limiter.TryTake(IPAddress.Parse("::ffff:192.0.2.1")));
            Assert.False(limiter.TryTake(IPAddress.Parse("192.0.2.1")));

            for (int i = 0; i < 10; ++i)
            {
                Assert.True(limiter.TryTake(IPAddress.Loopback));
                Assert.True(limiter.TryTake(IPAddress.IPv6Loopback));
                Assert.True(limiter.TryTake(null));
            }

            var off = Limiter(new ManualTimeProvider(), burst: 0);
            for (int i = 0; i < 10; ++i)
                Assert.True(off.TryTake(IPAddress.Parse("192.0.2.1")));
            Assert.Equal(0, off.TrackedAddresses);
        }

        [Fact]
        public void AddressesThatHaveRefilledAreForgotten()
        {
            var time = new ManualTimeProvider();
            var limiter = Limiter(time, burst: 2, perMinute: 6);
            for (int i = 0; i < 50; ++i)
                limiter.TryTake(IPAddress.Parse($"192.0.2.{i + 1}"));
            limiter.TryTake(IPAddress.Parse("198.51.100.1"));
            limiter.TryTake(IPAddress.Parse("198.51.100.1"));
            Assert.Equal(51, limiter.TrackedAddresses);

            // A minute later the one-attempt addresses are full again and go at the next new
            // address; the one that used its whole allowance has earned it back too.
            time.Advance(TimeSpan.FromSeconds(61));
            limiter.TryTake(IPAddress.Parse("203.0.113.1"));
            Assert.Equal(1, limiter.TrackedAddresses);
        }

        [Fact]
        public async Task WrongPasswordsLockTheAccountForGrowingPeriods()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string user = TestSetup.UniqueId("user");
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(user, Password);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            var clock = fixture.SignInClock;
            Task<LoginResult> Login(string password) => player.LoginWithUserNameAndPassword(user, password, sessionKey, Guid.NewGuid());

            // Four wrong passwords change nothing yet.
            for (int i = 0; i < Player.MaxPasswordFailures - 1; ++i)
                Assert.Null(await Login("wrong-password"));
            Assert.NotNull(await Login(Password));

            // A right password resets the count, so it takes five in a row to lock the account.
            for (int i = 0; i < Player.MaxPasswordFailures; ++i)
                Assert.Null(await Login("wrong-password"));
            Assert.Null(await Login(Password));

            clock.Advance(Player.FirstPasswordLockout - TimeSpan.FromSeconds(1));
            Assert.Null(await Login(Password));
            clock.Advance(TimeSpan.FromSeconds(1));

            // Unlocked, but still counting: the next wrong password locks it for twice as long.
            Assert.Null(await Login("wrong-password"));
            clock.Advance(Player.FirstPasswordLockout);
            Assert.Null(await Login(Password));
            clock.Advance(Player.FirstPasswordLockout);
            Assert.NotNull(await Login(Password));

            // After the right password the count starts over.
            for (int i = 0; i < Player.MaxPasswordFailures - 1; ++i)
                Assert.Null(await Login("wrong-password"));
            Assert.NotNull(await Login(Password));
        }

        [Fact]
        public async Task TheLockoutCoversEmailLoginsAndStopsAtItsMaximum()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string email = $"{TestSetup.UniqueId("lock")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, Password);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            await player.Activate((await player.GetState()).ActivationCodes[email].Code);
            var clock = fixture.SignInClock;
            Task<LoginResult> Login(string password) => player.LoginWithEmail(email, password, sessionKey, Guid.NewGuid());

            for (int i = 0; i < Player.MaxPasswordFailures + 12; ++i)
            {
                Assert.Null(await Login("wrong-password"));
                clock.Advance(Player.MaxPasswordLockout);
            }
            // However many came before, one maximum lockout later the right password works.
            Assert.Null(await Login("wrong-password"));
            clock.Advance(Player.MaxPasswordLockout - TimeSpan.FromSeconds(1));
            Assert.Null(await Login(Password));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.NotNull(await Login(Password));
        }

        [Fact]
        public async Task ALockedAccountAnswersLikeAWrongPasswordThroughTheGateway()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            string user = TestSetup.UniqueId("user");
            await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(user, Password);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, o => o.AuthAttemptBurst = 20);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            async Task<OptionalResponse<AuthenticationResponse>> Login(string password)
            {
                var reply = await client.RequestAsync(client.Request(WildCatMsgType.AuthenticateRequest, new AuthenticationRequest
                {
                    ticket = session.ToString(), LoginType = LoginType.UserWithPassword, Id = user, PasswordHash = password,
                }));
                return reply.GetPayload<OptionalResponse<AuthenticationResponse>>();
            }

            for (int i = 0; i < Player.MaxPasswordFailures; ++i)
                Assert.Equal("The login failed.", (await Login("wrong-password")).errorMsg);
            var locked = await Login(Password);
            Assert.False(locked.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, locked.errorCode);
            Assert.Equal("The login failed.", locked.errorMsg);

            fixture.SignInClock.Advance(Player.FirstPasswordLockout);
            var unlocked = await Login(Password);
            Assert.True(unlocked.isSuccesful, unlocked.errorMsg);
        }
    }
}
