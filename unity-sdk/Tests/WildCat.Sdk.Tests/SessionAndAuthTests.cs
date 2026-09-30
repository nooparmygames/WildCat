using System;
using System.Threading.Tasks;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>Connecting, the session, logins and accounts, against the real gateway.</summary>
    [Collection(ServerCollection.Name)]
    public class SessionAndAuthTests : ServerTestBase
    {
        public SessionAndAuthTests(ServerFixture server)
            : base(server)
        {
        }

        [Fact]
        public async Task ConnectsInitializesASessionAndLogsInWithADevice()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            WildCatClient client = NewClient();
            var connected = new Recorder<bool>();
            client.Connected += () => connected.Add(true);

            await Wait.For(client.ConnectAsync());
            Assert.True(client.IsConnected);
            Assert.Equal(ConnectionState.Connected, client.Connection.State);
            await connected.WaitForAnyAsync("the Connected event");

            Guid ticket = await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            Assert.NotEqual(Guid.Empty, ticket);
            Assert.True(client.Session.IsInitialized);
            Assert.Equal(gameKey, client.Session.GameKey);

            string device = Unique.Id("device");
            LoginResult first = await Wait.For(client.Auth.LoginWithDeviceAsync(device));
            Assert.True(first.IsNewAccount);
            Assert.False(string.IsNullOrEmpty(first.PlayerId));
            Assert.True(client.IsLoggedIn);
            Assert.Equal(first.PlayerId, client.Auth.PlayerId);

            await Wait.For(client.Profile.SetNameAsync("Ada"));
            Assert.Equal("Ada", await Wait.For(client.Profile.GetNameAsync()));

            // The same device on another connection is the same account.
            var (other, second) = await LoggedInClientAsync(developerKey, gameKey, device);
            Assert.False(second.IsNewAccount);
            Assert.Equal(first.PlayerId, second.PlayerId);
            Assert.Equal("Ada", second.PlayerName);
            Assert.Equal("Ada", await Wait.For(other.Profile.GetPlayerNameAsync(first.PlayerId)));
        }

        [Fact]
        public async Task AGameKeyThatIsNotTheDevelopersFailsWithTheServersError()
        {
            var (developerKey, _) = await Server.CreateGameAsync();
            WildCatClient client = NewClient();
            await Wait.For(client.ConnectAsync());

            var error = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Session.InitializeAsync(developerKey, Guid.NewGuid())));
            Assert.Equal(WildCatErrorCodes.Refused, error.ErrorCode);
            Assert.False(error.IsGatewayRefusal);
            Assert.False(client.Session.IsInitialized);
        }

        [Fact]
        public async Task TheGatewaysRefusalsFailTheRequestInsteadOfLeavingItPending()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            WildCatClient client = NewClient();
            await Wait.For(client.ConnectAsync());

            // No session: the SDK has no ticket to send.
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.Profile.GetNameAsync());

            // A session but no login: the gateway answers with ErrorMsg (8), which the old SDK ignored.
            await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            var error = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Profile.GetNameAsync()));
            Assert.True(error.IsGatewayRefusal);
            Assert.Equal(WildCatErrorCodes.Refused, error.ErrorCode);
            Assert.Equal("Log in before sending this message.", error.Message);
            Assert.Equal(WildCat.Protocol.WildCatMsgType.GetNameRequest, error.RequestType);
        }

        [Fact]
        public async Task EmailAccountsAreCreatedActivatedAndLoggedInto()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            WildCatClient client = NewClient();
            await Wait.For(client.ConnectAsync());
            await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            string email = $"{Unique.Id("player")}@example.com";

            // ws:// to 127.0.0.1 is allowed for passwords: it never leaves the machine.
            string account = await Wait.For(client.Auth.CreateAccountWithEmailAsync(email, "a long password 1"));
            Assert.False(string.IsNullOrEmpty(account));
            await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Auth.LoginWithEmailAsync(email, "a long password 1")));

            string code = await Server.Emails.WaitForActivationCodeAsync(email);
            await Wait.For(client.Auth.ActivateEmailAsync(email, code));
            LoginResult login = await Wait.For(client.Auth.LoginWithEmailAsync(email, "a long password 1"));
            Assert.Equal(account, login.PlayerId);

            var wrong = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Auth.LoginWithEmailAsync(email, "not the password")));
            Assert.Equal(WildCatErrorCodes.Refused, wrong.ErrorCode);
        }

        [Fact]
        public async Task UserNameLoginsCanBeAddedToADeviceAccount()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, login) = await LoggedInClientAsync(developerKey, gameKey);
            string userName = Unique.Id("user");
            await Wait.For(client.Auth.AddUserNameAsync(userName, "another password"));

            WildCatClient other = NewClient();
            await Wait.For(other.ConnectAsync());
            await Wait.For(other.Session.InitializeAsync(developerKey, gameKey));
            LoginResult again = await Wait.For(other.Auth.LoginWithUserNameAsync(userName, "another password"));
            Assert.Equal(login.PlayerId, again.PlayerId);
        }

        [Fact]
        public async Task ServerTimeNeedsNoSession()
        {
            WildCatClient client = NewClient();
            await Wait.For(client.ConnectAsync());

            DateTime serverNow = await Wait.For(client.ServerTime.GetAsync());

            Assert.Equal(DateTimeKind.Utc, serverNow.Kind);
            Assert.InRange(serverNow, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
            Assert.True(client.ServerTime.IsSynchronized);
            Assert.InRange(client.ServerTime.Offset, TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(5));
        }
    }
}
