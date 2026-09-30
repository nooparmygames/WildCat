using Orleans;
using Orleans.TestingHost;
using WildCat.Grains;
using WildCat.Interfaces;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Server-side password hashing and login ticket checks in the Player grain.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class PlayerSecurityTests
    {
        private const string PasswordKey = "password";
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public PlayerSecurityTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task UserPasswordIsStoredHashedAndOnlyTheRightPasswordLogsIn()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string user = TestSetup.UniqueId("user");
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(user, "s3cret-Password");
            Assert.Equal(AdditionResult.Success, created.result);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);

            string stored = (await player.GetPlayerUserPasswordPairs())[user][PasswordKey];
            Assert.StartsWith("pbkdf2-sha512$210000$", stored);
            Assert.DoesNotContain("s3cret", stored);

            Assert.Null(await player.LoginWithUserNameAndPassword(user, "wrong-password", sessionKey, Guid.NewGuid()));
            Assert.False(await player.IsLoggedIn());

            var login = await player.LoginWithUserNameAndPassword(user, "s3cret-Password", sessionKey, Guid.NewGuid());
            Assert.NotNull(login);
            Assert.Equal(created.Account, login.userID);
            Assert.True(await player.IsLoggedIn());

            // A failed attempt by someone else leaves the logged-in player alone.
            Assert.Null(await player.LoginWithUserNameAndPassword(user, "wrong-password", sessionKey, Guid.NewGuid()));
            Assert.True(await player.IsLoggedIn());
        }

        [Fact]
        public async Task ChangedUserPasswordReplacesTheOldOne()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string user = TestSetup.UniqueId("user");
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithUserAndPassword(user, "first-password");
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);

            Assert.True(await player.ChangePasswordForUserId(user, "second-password"));

            Assert.Null(await player.LoginWithUserNameAndPassword(user, "first-password", sessionKey, Guid.NewGuid()));
            Assert.NotNull(await player.LoginWithUserNameAndPassword(user, "second-password", sessionKey, Guid.NewGuid()));
        }

        [Fact]
        public async Task EmailPasswordResetUsesTheCodeOnce()
        {
            var (_, _, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            string email = $"{TestSetup.UniqueId("player")}@example.com";
            var created = await GrainFactory.GetGrain<IPlayerCreator>(0).CreateAccountWithEmail(email, "first-password");
            Assert.Equal(AdditionResult.Success, created.result);
            var player = GrainFactory.GetGrain<IPlayer>(created.Account);
            var account = (await player.GetPlayerEmailAccounts())[email];
            Assert.StartsWith("pbkdf2-sha512$", account[PasswordKey]);
            Assert.Matches("^[A-Z0-9]{6}$", account["code"]);
            await player.Activate(account["code"]);

            string resetCode = await player.SendPasswordChangeCodeToEmail(email);
            Assert.False(await player.ChangePasswordForEmailByCode("", "hijacked-password"));
            Assert.True(await player.ChangePasswordForEmailByCode(resetCode, "second-password"));
            // The used code is cleared and an empty code matches nothing.
            Assert.False(await player.ChangePasswordForEmailByCode(resetCode, "third-password"));
            await Assert.ThrowsAsync<ArgumentException>(() => player.Activate(""));

            Assert.Null(await player.LoginWithEmail(email, "first-password", sessionKey, Guid.NewGuid()));
            Assert.NotNull(await player.LoginWithEmail(email, "second-password", sessionKey, Guid.NewGuid()));
        }

        [Fact]
        public async Task UnacceptablePasswordIsRejectedWithoutClaimingTheName()
        {
            string user = TestSetup.UniqueId("user");
            var creator = GrainFactory.GetGrain<IPlayerCreator>(0);

            Assert.Equal(AdditionResult.Failure, (await creator.CreateAccountWithUserAndPassword(user, "")).result);
            Assert.Equal(AdditionResult.Failure, (await creator.CreateAccountWithUserAndPassword(user, new string('x', 257))).result);

            var created = await creator.CreateAccountWithUserAndPassword(user, "acceptable");
            Assert.Equal(AdditionResult.Success, created.result);
        }

        [Fact]
        public async Task StaleDisconnectDoesNotLogOutTheNewerLogin()
        {
            var (developerKey, gameKey, firstSession) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            var secondSession = await GrainFactory.GetGrain<IDeveloper>(developerKey).InitializeSession(gameKey);
            Guid firstLogin = Guid.NewGuid();
            Guid secondLogin = Guid.NewGuid();
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, firstSession, firstLogin);
            string device = player.GetPrimaryKeyString();

            // The same account logs in again, then the first socket closes.
            Assert.NotNull(await player.LoginWithDeviceID(device, secondSession, secondLogin));
            await player.Disconnected(firstLogin);

            Assert.True(await player.IsLoggedIn());
            var info = await GrainFactory.GetGrain<ISession>(secondSession).TouchAndGetInfo();
            Assert.Equal(device, info.UserID);

            // The login ticket is persisted, so the check holds after the grain is reactivated.
            await cluster.Deactivate(player);
            await player.Disconnected(firstLogin);
            Assert.True(await player.IsLoggedIn());

            // The current login's own disconnect still logs out and ends its session.
            await player.Disconnected(secondLogin);
            Assert.False(await player.IsLoggedIn());
            await Assert.ThrowsAsync<SessionNotInitializedException>(
                () => GrainFactory.GetGrain<ISession>(secondSession).TouchAndGetInfo());
        }

        [Fact]
        public async Task LoginOnAnEndedSessionKeepsTheCurrentLoginTicket()
        {
            var (developerKey, gameKey, sessionKey) = await TestSetup.CreateDeveloperGameAndSession(GrainFactory);
            Guid currentLogin = Guid.NewGuid();
            var player = await TestSetup.CreateLoggedInPlayer(GrainFactory, sessionKey, currentLogin);
            var endedSession = await GrainFactory.GetGrain<IDeveloper>(developerKey).InitializeSession(gameKey);
            await GrainFactory.GetGrain<ISession>(endedSession).EndSession();

            await Assert.ThrowsAsync<SessionNotInitializedException>(
                () => player.LoginWithDeviceID(player.GetPrimaryKeyString(), endedSession, Guid.NewGuid()));

            // The failed login did not replace the ticket, so the real socket's disconnect still counts.
            Assert.True(await player.IsLoggedIn());
            await player.Disconnected(currentLogin);
            Assert.False(await player.IsLoggedIn());
        }
    }
}
