using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Accounts and logins: device ids, user names and emails with passwords, email activation and
    /// password reset codes. Needs an initialized session.
    /// </summary>
    /// <remarks>
    /// Passwords are sent as plain text (the server keeps only a salted hash), so the SDK sends them
    /// only over wss:// or to localhost unless
    /// <see cref="WildCatClientOptions.AllowPlainTextPasswordsWithoutTls"/> is set.
    /// </remarks>
    public sealed class AuthClient : FeatureClient
    {
        private readonly SessionClient session;
        private readonly object sync = new object();
        private StoredLogin lastLogin;
        private string playerId;
        private string playerName;

        internal AuthClient(WildCatConnection connection, SessionClient session)
            : base(connection)
        {
            this.session = session;
        }

        /// <summary>The logged in player's account id, or null.</summary>
        public string PlayerId
        {
            get
            {
                lock (sync)
                    return playerId;
            }
        }

        /// <summary>The logged in player's name as the login returned it, or null.</summary>
        public string PlayerName
        {
            get
            {
                lock (sync)
                    return playerName;
            }
        }

        /// <summary>True while a player is logged in on the current session.</summary>
        public bool IsLoggedIn
        {
            get { return PlayerId != null && Connection.IsLoggedIn; }
        }

        internal StoredLogin LastLogin
        {
            get
            {
                lock (sync)
                    return lastLogin;
            }
        }

        /// <summary>
        /// Logs in with a device id, creating the account on first use unless
        /// <paramref name="createAccount"/> is false. In Unity, LoginWithThisDeviceAsync passes
        /// SystemInfo.deviceUniqueIdentifier.
        /// </summary>
        public Task<LoginResult> LoginWithDeviceAsync(string deviceId, bool createAccount = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return LoginAsync(new StoredLogin(LoginType.Device, deviceId, "", createAccount), false, cancellationToken);
        }

        /// <summary>Logs in with a user name and password, optionally creating the account.</summary>
        public Task<LoginResult> LoginWithUserNameAsync(string userName, string password, bool createAccount = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return LoginAsync(new StoredLogin(LoginType.UserWithPassword, userName, password, createAccount), false, cancellationToken);
        }

        /// <summary>
        /// Logs in with an activated email address and its password. Create the account with
        /// <see cref="CreateAccountWithEmailAsync"/> and activate it with <see cref="ActivateEmailAsync"/> first.
        /// </summary>
        public Task<LoginResult> LoginWithEmailAsync(string email, string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return LoginAsync(new StoredLogin(LoginType.Email, email, password, false), false, cancellationToken);
        }

        internal async Task<LoginResult> LoginAsync(StoredLogin login, bool isInternal, CancellationToken cancellationToken)
        {
            RequireText(login.Id, "id");
            if (login.Type != LoginType.Device)
                EnsurePasswordsMaySend();
            Guid ticket = RequireSession();

            AuthenticationRequest request = new AuthenticationRequest
            {
                ticket = ticket.ToString(),
                LoginType = login.Type,
                Id = login.Id,
                PasswordHash = login.Password,
                AutoCreate = login.CreateAccount,
                data = new Dictionary<string, string>(),
            };
            AuthenticationResponse answer = await Connection.SendRequestAsync<AuthenticationResponse>(WildCatMsgType.AuthenticateRequest,
                request, WildCatMsgType.AuthenticateResponse, false, null, null, isInternal, cancellationToken).ConfigureAwait(false);

            lock (sync)
            {
                playerId = answer.userId;
                playerName = answer.name;
                lastLogin = Connection.Options.ResumeSession ? login : null;
            }
            Connection.SetSession(string.IsNullOrEmpty(answer.ticket) ? ticket.ToString() : answer.ticket, true);
            return new LoginResult(answer.userId, answer.name, answer.isNewlyCreated);
        }

        /// <summary>Creates a device account without logging in; returns the account id. Error code 2 when the device has one.</summary>
        public Task<string> CreateAccountWithDeviceAsync(string deviceId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreateAccountAsync(LoginType.Device, deviceId, "", cancellationToken);
        }

        /// <summary>Creates a user name account without logging in; returns the account id. Error code 2 when the name is taken.</summary>
        public Task<string> CreateAccountWithUserNameAsync(string userName, string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreateAccountAsync(LoginType.UserWithPassword, userName, password, cancellationToken);
        }

        /// <summary>
        /// Creates an email account; the server mails an activation code, which
        /// <see cref="ActivateEmailAsync"/> takes before the address can log in. Returns the account id.
        /// </summary>
        public Task<string> CreateAccountWithEmailAsync(string email, string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreateAccountAsync(LoginType.Email, email, password, cancellationToken);
        }

        private async Task<string> CreateAccountAsync(LoginType type, string id, string password, CancellationToken cancellationToken)
        {
            RequireText(id, "id");
            if (type != LoginType.Device)
                EnsurePasswordsMaySend();
            Guid ticket = RequireSession();
            CreateAccountRequest request = new CreateAccountRequest
            {
                ticket = ticket.ToString(),
                LoginType = type,
                Id = id,
                PasswordHash = password ?? "",
                data = new Dictionary<string, string>(),
            };
            StringMessage answer = await Connection.SendRequestAsync<StringMessage>(WildCatMsgType.CreateNewAccountRequest, request,
                WildCatMsgType.CreateNewAccountResponse, false, null, null, false, cancellationToken).ConfigureAwait(false);
            return answer.value;
        }

        /// <summary>Adds a user name and password login to the logged in account. Error code 1 when the name is taken.</summary>
        public Task AddUserNameAsync(string userName, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            return AddLoginAsync(LoginType.UserWithPassword, userName, password, cancellationToken);
        }

        /// <summary>
        /// Adds an email and password login to the logged in account. The server mails an
        /// activation code for <see cref="ActivateEmailAsync"/>.
        /// </summary>
        public Task AddEmailAsync(string email, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            return AddLoginAsync(LoginType.Email, email, password, cancellationToken);
        }

        private Task AddLoginAsync(LoginType type, string id, string password, CancellationToken cancellationToken)
        {
            RequireText(id, "id");
            EnsurePasswordsMaySend();
            return Request<EmptyMessage>(WildCatMsgType.AddAuthMechanismToAccountRequest,
                new AddAuthenticationToAccountRequest
                {
                    LoginType = type,
                    Id = id,
                    PasswordHash = password ?? "",
                    data = new Dictionary<string, string>(),
                },
                WildCatMsgType.AddAuthMechanismToAccountResponse, cancellationToken);
        }

        /// <summary>Activates an email address with the code the server mailed. Error code 1 for an unknown address or a wrong code.</summary>
        public Task ActivateEmailAsync(string email, string code, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(email, "email");
            RequireText(code, "code");
            Guid ticket = RequireSession();
            return Connection.SendRequestAsync<EmptyMessage>(WildCatMsgType.EmailActivationRequest,
                new EmailActivationRequest { ticket = ticket.ToString(), email = email, code = code },
                WildCatMsgType.EmailActivationResponse, false, null, null, false, cancellationToken);
        }

        /// <summary>Mails a new activation code to an address that is not activated yet. Always succeeds.</summary>
        public Task ResendActivationCodeAsync(string email, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(email, "email");
            return Request<EmptyMessage>(WildCatMsgType.ResendActivationRequest, new ResendActivationRequest { email = email },
                WildCatMsgType.ResendActivationResponse, cancellationToken);
        }

        /// <summary>Mails a password reset code for <see cref="ResetPasswordAsync"/>. Always succeeds, so it does not reveal which addresses exist.</summary>
        public Task RecoverPasswordAsync(string email, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(email, "email");
            return Request<EmptyMessage>(WildCatMsgType.RecoverPasswordRequest, new RecoverPasswordRequest { email = email },
                WildCatMsgType.RecoverPasswordResponse, cancellationToken);
        }

        /// <summary>Sets a new password with the code from <see cref="RecoverPasswordAsync"/> (and activates the address).</summary>
        public Task ResetPasswordAsync(string email, string code, string newPassword,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(email, "email");
            RequireText(code, "code");
            RequireText(newPassword, "newPassword");
            EnsurePasswordsMaySend();
            return Request<EmptyMessage>(WildCatMsgType.ResetPasswordWithCodeRequest,
                new ResetPasswordWithCodeRequest { email = email, code = code, newPassword = newPassword },
                WildCatMsgType.ResetPasswordWithCodeResponse, cancellationToken);
        }

        private Guid RequireSession()
        {
            Guid ticket = session.Ticket;
            if (ticket == Guid.Empty)
                throw new InvalidOperationException("Initialize a session (Session.InitializeAsync) before using accounts.");
            return ticket;
        }

        /// <summary>
        /// Passwords travel as plain text, so they may only go over TLS, or to this machine. Throws
        /// InvalidOperationException otherwise, unless the options allow it for a test server.
        /// </summary>
        private void EnsurePasswordsMaySend()
        {
            Uri server = Connection.ServerUri;
            if (server.Scheme == "wss" || server.IsLoopback)
                return;
            if (Connection.Options.AllowPlainTextPasswordsWithoutTls)
            {
                Connection.Log(WildCatLogLevel.Warning, "Sending a password as plain text over ws:// to " + server.Host
                    + ". Anyone on the network can read it; use wss:// for anything but a test server on a network you trust.", null);
                return;
            }
            throw new InvalidOperationException("Passwords are sent to the server as plain text, so they need an encrypted "
                + "connection: use a wss:// server URL (ws:// is accepted only for localhost). For a test server on a network you "
                + "trust, set WildCatClientOptions.AllowPlainTextPasswordsWithoutTls.");
        }

        /// <summary>The socket dropped: the login is gone, the credentials stay for resuming.</summary>
        internal void ForgetPlayer()
        {
            lock (sync)
            {
                playerId = null;
                playerName = null;
            }
        }

        internal void Clear()
        {
            lock (sync)
            {
                playerId = null;
                playerName = null;
                lastLogin = null;
            }
        }
    }
}
