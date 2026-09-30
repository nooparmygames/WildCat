using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;
using LoginType = WildCat.Protocol.LoginType;

namespace WildCat.Tests
{
    /// <summary>
    /// The Auth handlers over real WebSockets: Authenticate (76), CreateNewAccount (78),
    /// EmailActivation (80), AddAuthMechanismToAccount (82) and the account recovery messages
    /// RecoverPassword (84), ResendActivation (86) and ResetPasswordWithCode (163).
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayAuthHandlerTests
    {
        private const string Password = "a-good-password";
        private const string WrongOrExpiredCode = "The code is wrong or has expired.";
        private readonly InProcessTestCluster cluster;
        private readonly CapturingEmailSender emails;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayAuthHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
            emails = fixture.Emails;
        }

        private static string NewEmail() => $"{TestSetup.UniqueId("player")}@example.com";

        private async Task<(GatewayTestHost Host, GatewayTestClient Client, Guid Session)> Start(Action<GameGatewayOptions> configure = null)
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var host = await GatewayTestHost.StartAsync(cluster.Client, configure ?? (o => o.AuthAttemptBurst = 100));
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            return (host, client, session);
        }

        private static Task<OptionalResponse<AuthenticationResponse>> Authenticate(GatewayTestClient client, Guid session,
            LoginType type, string id, string password = "", bool autoCreate = false)
        {
            return client.CallAsync<AuthenticationResponse>(WildCatMsgType.AuthenticateRequest,
                new AuthenticationRequest { ticket = session.ToString(), LoginType = type, Id = id, PasswordHash = password, AutoCreate = autoCreate },
                WildCatMsgType.AuthenticateResponse);
        }

        private static Task<OptionalResponse<StringMessage>> CreateAccount(GatewayTestClient client, Guid session,
            LoginType type, string id, string password = "")
        {
            return client.CallAsync<StringMessage>(WildCatMsgType.CreateNewAccountRequest,
                new CreateAccountRequest { ticket = session.ToString(), LoginType = type, Id = id, PasswordHash = password },
                WildCatMsgType.CreateNewAccountResponse);
        }

        private static Task<OptionalResponse<EmptyMessage>> Activate(GatewayTestClient client, Guid session, string email, string code)
        {
            return client.CallAsync<EmptyMessage>(WildCatMsgType.EmailActivationRequest,
                new EmailActivationRequest { ticket = session.ToString(), email = email, code = code },
                WildCatMsgType.EmailActivationResponse);
        }

        private async Task<string> ActivationCode(string accountId, string email)
        {
            return (await GrainFactory.GetGrain<IPlayer>(accountId).GetState()).ActivationCodes[email].Code;
        }

        private async Task<string> ResetCode(string accountId, string email)
        {
            return (await GrainFactory.GetGrain<IPlayer>(accountId).GetState()).PasswordResetCodes[email].Code;
        }

        private static void AssertFailed<T>(OptionalResponse<T> response, int code, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(code, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        [Fact]
        public async Task DeviceLoginCreatesTheAccountOnceAndBindsTheConnection()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string device = TestSetup.UniqueId("device");

            var first = await Authenticate(client, session, LoginType.Device, device, autoCreate: true);
            Assert.True(first.isSuccesful, first.errorMsg);
            Assert.True(first.msg.isNewlyCreated);
            Assert.False(string.IsNullOrEmpty(first.msg.ticket));
            Assert.Contains(first.msg.userId, host.Connections.PlayerIds);
            Assert.True(await GrainFactory.GetGrain<IPlayer>(first.msg.userId).IsLoggedIn());

            var again = await Authenticate(client, session, LoginType.Device, device, autoCreate: true);
            Assert.True(again.isSuccesful, again.errorMsg);
            Assert.False(again.msg.isNewlyCreated);
            Assert.Equal(first.msg.userId, again.msg.userId);
        }

        [Fact]
        public async Task UserNameLoginChecksThePassword()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string user = TestSetup.UniqueId("user");

            var created = await Authenticate(client, session, LoginType.UserWithPassword, user, Password, autoCreate: true);
            Assert.True(created.isSuccesful, created.errorMsg);
            Assert.True(created.msg.isNewlyCreated);

            AssertFailed(await Authenticate(client, session, LoginType.UserWithPassword, user, "wrong-password"),
                GatewayErrors.Refused, "The login failed.");
            var loggedIn = await Authenticate(client, session, LoginType.UserWithPassword, user, Password);
            Assert.True(loggedIn.isSuccesful, loggedIn.errorMsg);
            Assert.Equal(created.msg.userId, loggedIn.msg.userId);
        }

        [Fact]
        public async Task LoginFailuresAreErrorCodeOne()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;

            AssertFailed(await Authenticate(client, session, LoginType.Device, TestSetup.UniqueId("unknown")),
                GatewayErrors.Refused, "The login failed.");
            // Email accounts are never created by a login.
            AssertFailed(await Authenticate(client, session, LoginType.Email, NewEmail(), Password, autoCreate: true),
                GatewayErrors.Refused, "The login failed.");
            AssertFailed(await Authenticate(client, session, (LoginType)9, "x"), GatewayErrors.Refused, "Unsupported login type");

            var badTicket = await client.CallAsync<AuthenticationResponse>(WildCatMsgType.AuthenticateRequest,
                new AuthenticationRequest { ticket = "not-a-guid", LoginType = LoginType.Device, Id = "d", AutoCreate = true },
                WildCatMsgType.AuthenticateResponse);
            AssertFailed(badTicket, GatewayErrors.Refused, GatewayErrors.InvalidTicket);
            AssertFailed(await Authenticate(client, Guid.NewGuid(), LoginType.Device, "d", autoCreate: true),
                GatewayErrors.Refused, SessionNotInitializedException.DefaultMessage);
        }

        [Fact]
        public async Task EmailAccountLogsInOnceActivatedWhateverTheCaseOfTheAddress()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string email = NewEmail();
            string shouted = email.ToUpperInvariant();

            var created = await CreateAccount(client, session, LoginType.Email, shouted, Password);
            Assert.True(created.isSuccesful, created.errorMsg);
            await emails.WaitForAsync(email);

            AssertFailed(await Authenticate(client, session, LoginType.Email, email, Password), GatewayErrors.Refused, "The login failed.");
            var activated = await Activate(client, session, shouted, await ActivationCode(created.msg.value, email));
            Assert.True(activated.isSuccesful, activated.errorMsg);

            var login = await Authenticate(client, session, LoginType.Email, shouted, Password);
            Assert.True(login.isSuccesful, login.errorMsg);
            Assert.Equal(created.msg.value, login.msg.userId);
        }

        [Fact]
        public async Task CreateNewAccountCreatesEachKindOnceAndReportsDuplicates()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string device = TestSetup.UniqueId("device");

            var byDevice = await CreateAccount(client, session, LoginType.Device, device);
            Assert.True(byDevice.isSuccesful, byDevice.errorMsg);
            Assert.True(Guid.TryParse(byDevice.msg.value, out Guid accountId));
            Assert.NotEqual(Guid.Empty, accountId);
            AssertFailed(await CreateAccount(client, session, LoginType.Device, device), GatewayErrors.AlreadyExists, "Already exists");

            var byUser = await CreateAccount(client, session, LoginType.UserWithPassword, TestSetup.UniqueId("user"), Password);
            Assert.True(byUser.isSuccesful, byUser.errorMsg);
            AssertFailed(await CreateAccount(client, session, LoginType.UserWithPassword, TestSetup.UniqueId("user"), ""),
                GatewayErrors.Refused, "The account could not be created.");
            AssertFailed(await CreateAccount(client, session, (LoginType)7, "x"), GatewayErrors.Refused, "Unsupported login type");

            var badTicket = await client.CallAsync<StringMessage>(WildCatMsgType.CreateNewAccountRequest,
                new CreateAccountRequest { ticket = Guid.NewGuid().ToString(), LoginType = LoginType.Device, Id = TestSetup.UniqueId("device") },
                WildCatMsgType.CreateNewAccountResponse);
            AssertFailed(badTicket, GatewayErrors.Refused, SessionNotInitializedException.DefaultMessage);
        }

        [Fact]
        public async Task EmailActivationRefusesAWrongCodeAndAnUnknownAddress()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string email = NewEmail();
            var created = await CreateAccount(client, session, LoginType.Email, email, Password);
            Assert.True(created.isSuccesful, created.errorMsg);

            AssertFailed(await Activate(client, session, email, "WRONG1"), GatewayErrors.Refused, WrongOrExpiredCode);
            AssertFailed(await Activate(client, session, NewEmail(), "WRONG1"), GatewayErrors.Refused, "No account has this email address.");
            var activated = await Activate(client, session, email, await ActivationCode(created.msg.value, email));
            Assert.True(activated.isSuccesful, activated.errorMsg);
        }

        [Fact]
        public async Task AddAuthMechanismAddsLoginsToTheLoggedInAccount()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string ticket = session.ToString();

            var noLogin = await client.CallAsync<EmptyMessage>(WildCatMsgType.AddAuthMechanismToAccountRequest,
                new AddAuthenticationToAccountRequest { LoginType = LoginType.Device, Id = "d" }, WildCatMsgType.ErrorMsg, ticket);
            Assert.Equal(GatewayErrors.NotLoggedIn, noLogin.errorMsg);

            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            string user = TestSetup.UniqueId("user");
            string email = NewEmail();
            Task<OptionalResponse<EmptyMessage>> Add(LoginType type, string id, string password) =>
                client.CallAsync<EmptyMessage>(WildCatMsgType.AddAuthMechanismToAccountRequest,
                    new AddAuthenticationToAccountRequest { LoginType = type, Id = id, PasswordHash = password },
                    WildCatMsgType.AddAuthMechanismToAccountResponse, ticket);

            Assert.True((await Add(LoginType.UserWithPassword, user, Password)).isSuccesful);
            AssertFailed(await Add(LoginType.UserWithPassword, user, Password), GatewayErrors.Refused, "Already exists");
            AssertFailed(await Add(LoginType.UserWithPassword, TestSetup.UniqueId("user"), ""), GatewayErrors.Refused, "The password cannot be empty.");
            AssertFailed(await Add(LoginType.Email, "not an email", Password), GatewayErrors.Refused, "Email not an email contains invalid characters");
            Assert.True((await Add(LoginType.Email, email, Password)).isSuccesful);
            await emails.WaitForAsync(email);
            Assert.True((await Add(LoginType.Device, TestSetup.UniqueId("device"), "")).isSuccesful);

            // The new user name logs in to the same account.
            await using var other = await host.ConnectAsync();
            var byUser = await Authenticate(other, session, LoginType.UserWithPassword, user, Password);
            Assert.True(byUser.isSuccesful, byUser.errorMsg);
            Assert.Equal(login.userId, byUser.msg.userId);
        }

        [Fact]
        public async Task PasswordRecoveryResetsThePasswordWithTheEmailedCode()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string ticket = session.ToString();
            string email = NewEmail();
            var created = await CreateAccount(client, session, LoginType.Email, email, Password);
            Assert.True(created.isSuccesful, created.errorMsg);

            var recover = await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                new RecoverPasswordRequest { email = email.ToUpperInvariant() }, WildCatMsgType.RecoverPasswordResponse, ticket);
            Assert.True(recover.isSuccesful, recover.errorMsg);
            await emails.WaitForAsync(email, 2);
            string code = await ResetCode(created.msg.value, email);

            Task<OptionalResponse<EmptyMessage>> Reset(string address, string resetCode, string newPassword) =>
                client.CallAsync<EmptyMessage>(WildCatMsgType.ResetPasswordWithCodeRequest,
                    new ResetPasswordWithCodeRequest { email = address, code = resetCode, newPassword = newPassword },
                    WildCatMsgType.ResetPasswordWithCodeResponse, ticket);

            AssertFailed(await Reset(email, "WRONG1", "new-password"), GatewayErrors.Refused, WrongOrExpiredCode);
            AssertFailed(await Reset(email, code, ""), GatewayErrors.Refused, "The password cannot be empty.");
            var reset = await Reset(email, code, "new-password");
            Assert.True(reset.isSuccesful, reset.errorMsg);
            AssertFailed(await Reset(email, code, "third-password"), GatewayErrors.Refused, WrongOrExpiredCode);

            // The reset also activated the address.
            AssertFailed(await Authenticate(client, session, LoginType.Email, email, Password), GatewayErrors.Refused, "The login failed.");
            Assert.True((await Authenticate(client, session, LoginType.Email, email, "new-password")).isSuccesful);
        }

        [Fact]
        public async Task RecoveryAnswersDoNotRevealWhetherTheAddressExists()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string ticket = session.ToString();
            string unknown = NewEmail();

            foreach (var address in new[] { unknown, "", "not an email" })
            {
                var recover = await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                    new RecoverPasswordRequest { email = address }, WildCatMsgType.RecoverPasswordResponse, ticket);
                Assert.True(recover.isSuccesful, recover.errorMsg);
                var resend = await client.CallAsync<EmptyMessage>(WildCatMsgType.ResendActivationRequest,
                    new ResendActivationRequest { email = address }, WildCatMsgType.ResendActivationResponse, ticket);
                Assert.True(resend.isSuccesful, resend.errorMsg);
            }
            var reset = await client.CallAsync<EmptyMessage>(WildCatMsgType.ResetPasswordWithCodeRequest,
                new ResetPasswordWithCodeRequest { email = unknown, code = "ABC123", newPassword = "new-password" },
                WildCatMsgType.ResetPasswordWithCodeResponse, ticket);
            AssertFailed(reset, GatewayErrors.Refused, WrongOrExpiredCode);
            Assert.Empty(emails.SentTo(unknown));
        }

        [Fact]
        public async Task ResendActivationSendsANewCodeOnlyForAnInactiveAddress()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;
            string ticket = session.ToString();
            string email = NewEmail();
            var created = await CreateAccount(client, session, LoginType.Email, email, Password);
            Assert.True(created.isSuccesful, created.errorMsg);
            await emails.WaitForAsync(email);
            var player = GrainFactory.GetGrain<IPlayer>(created.msg.value);
            var first = (await player.GetState()).ActivationCodes[email];

            Task<OptionalResponse<EmptyMessage>> Resend() => client.CallAsync<EmptyMessage>(WildCatMsgType.ResendActivationRequest,
                new ResendActivationRequest { email = email }, WildCatMsgType.ResendActivationResponse, ticket);

            Assert.True((await Resend()).isSuccesful);
            var sent = await emails.WaitForAsync(email, 2);
            var second = (await player.GetState()).ActivationCodes[email];
            Assert.Contains(second.Code, sent.Body);
            Assert.True(second.IssuedAtUtc >= first.IssuedAtUtc);
            if (second.Code != first.Code)
                AssertFailed(await Activate(client, session, email, first.Code), GatewayErrors.Refused, WrongOrExpiredCode);
            Assert.True((await Activate(client, session, email, second.Code)).isSuccesful);

            // Activated: nothing more is sent, and the answer does not say so.
            Assert.True((await Resend()).isSuccesful);
            await Task.Delay(200);
            Assert.True(emails.SentTo(email).Count <= 2);
        }

        [Fact]
        public async Task RecoveryMessagesNeedASessionButNoLogin()
        {
            var (host, client, session) = await Start();
            await using var _ = host;
            await using var __ = client;

            var noTicket = await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                new RecoverPasswordRequest { email = NewEmail() }, WildCatMsgType.ErrorMsg);
            Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
            var unknownSession = await client.CallAsync<EmptyMessage>(WildCatMsgType.ResendActivationRequest,
                new ResendActivationRequest { email = NewEmail() }, WildCatMsgType.ErrorMsg, Guid.NewGuid().ToString());
            Assert.Equal(SessionNotInitializedException.DefaultMessage, unknownSession.errorMsg);
            var withSession = await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                new RecoverPasswordRequest { email = NewEmail() }, WildCatMsgType.RecoverPasswordResponse, session.ToString());
            Assert.True(withSession.isSuccesful, withSession.errorMsg);
        }

        [Fact]
        public async Task RecoveryMessagesShareTheSignInLimit()
        {
            var (host, client, session) = await Start(o => o.AuthAttemptBurst = 3);
            await using var _ = host;
            await using var __ = client;
            string ticket = session.ToString();

            // InitializeSession is not a sign-in attempt; these three use up the allowance.
            Assert.True((await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                new RecoverPasswordRequest { email = NewEmail() }, WildCatMsgType.RecoverPasswordResponse, ticket)).isSuccesful);
            Assert.True((await client.CallAsync<EmptyMessage>(WildCatMsgType.ResendActivationRequest,
                new ResendActivationRequest { email = NewEmail() }, WildCatMsgType.ResendActivationResponse, ticket)).isSuccesful);
            AssertFailed(await client.CallAsync<EmptyMessage>(WildCatMsgType.ResetPasswordWithCodeRequest,
                new ResetPasswordWithCodeRequest { email = NewEmail(), code = "ABC123", newPassword = "p" },
                WildCatMsgType.ResetPasswordWithCodeResponse, ticket), GatewayErrors.Refused, WrongOrExpiredCode);

            AssertFailed(await client.CallAsync<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest,
                new RecoverPasswordRequest { email = NewEmail() }, WildCatMsgType.RecoverPasswordResponse, ticket),
                GatewayErrors.Refused, GatewayErrors.TooManyAuthAttempts);
            AssertFailed(await client.CallAsync<EmptyMessage>(WildCatMsgType.ResetPasswordWithCodeRequest,
                new ResetPasswordWithCodeRequest { email = NewEmail(), code = "ABC123", newPassword = "p" },
                WildCatMsgType.ResetPasswordWithCodeResponse, ticket), GatewayErrors.Refused, GatewayErrors.TooManyAuthAttempts);
        }
    }
}
