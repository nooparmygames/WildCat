using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Grains.Resources;
using WildCat.Interfaces;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Streams;

namespace WildCat.Grains
{
    public class Player : Grain, IPlayer
    {
        private readonly IPersistentState<PlayerState> state;

        private PlayerState State => state.State;

        private const string PASSWORD_KEY = "password";
        private const string STATUS_KEY = "status";
        private const string STATUS_NOT_ACTIVE = "not active";
        private const int RANDOM_TOKEN_LENGTH = 6;
        private const string STATUS_ACTIVE = "active";


        private IGrainTimer timer;

        private IAsyncStream<PlayerConnectionMessage> connectionStream;
        private IStreamProvider streamProvider;

        private readonly IEmailSender emailSender;
        private readonly ILogger<Player> logger;

        public Player(
            [PersistentState(GrainStateNames.Player, GrainStorageNames.Default)] IPersistentState<PlayerState> state,
            IEmailSender emailSender,
            ILogger<Player> logger)
        {
            this.state = state;
            this.emailSender = emailSender;
            this.logger = logger;
        }

        public async override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            streamProvider = this.GetStreamProvider(StreamNames.PROVIDER_NAME);
            connectionStream = streamProvider.GetStream<PlayerConnectionMessage>(StreamNames.PlayerConnection(this.GetPrimaryKeyString()));
            await base.OnActivateAsync(cancellationToken);
        }

        /// <summary>
        /// Publishes an encoded frame on the player's PlayerConnection stream, which the gateway
        /// node holding the player's socket pushes to the client (WildCat.Gateway/Push). The
        /// publish is awaited, so frames a caller sends one after another are queued in that order
        /// and a failed publish reaches the caller (it used to be fire and forget).
        /// </summary>
        public Task SendDataToConnection(IPlayer player, int channel, byte[] data)
        {
            if (this.GetPrimaryKeyString() != player.GetPrimaryKeyString())
                return Task.CompletedTask;
            return connectionStream.OnNextAsync(new PlayerConnectionMessage { player = player, channelId = channel, data = data });
        }
        
        private async Task OnTimer(object timerState)
        {
            if ((DateTime.Now - State.LastHeartBeat).TotalSeconds > 20)
            {
                await SetPresenceStatus(OnlineStatus.Offline);
            }
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            timer?.Dispose();
            return base.OnDeactivateAsync(reason, cancellationToken);
        }

        #region MATCH

        public Task GameStarted(IRelayServerGame game)
        {
            State.RunningGames.Add(game);
            return state.WriteStateAsync();
        }

        public Task GameFinished(IRelayServerGame game)
        {
            string s1, s2;
            var foundGame =
                State.RunningGames.FirstOrDefault(
                    x => x.GetPrimaryKey(out s1) == game.GetPrimaryKey(out s2) && s1 == s2);
            if (foundGame != null)
            {
                State.RunningGames.Remove(foundGame);
            }

            return state.WriteStateAsync();
        }

        public Task<int> GetEloScore()
        {
            return Task.FromResult(State.EloScore);
        }

        public Task SetEloScore(int score)
        {
            State.EloScore = score;
            return Task.CompletedTask;
        }

        #endregion

        public async Task Disconnected(Guid sentLoginTimeTicket)
        {
            // The gateway passes the ticket of the login that owned the closed socket. When the
            // player has logged in again since (another device, or a reconnect that beat the old
            // socket's close), that ticket is stale and must not log out the newer login.
            if (sentLoginTimeTicket != State.LoginTicket)
                return;
            // A match that fails must not stop the logout (the first failure used to skip it and
            // every match after it).
            foreach (var match in State.RunningGames.AsEnumerable().Reverse().ToList())
            {
                try
                {
                    if (await match.PlayerDisconnected(this.AsReference<IPlayer>()))
                        State.RunningGames.Remove(match);
                }
                catch (GameNotRunningException)
                {
                    // The match does not know this player: a stale entry.
                    State.RunningGames.Remove(match);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not tell match {MatchId} that player {PlayerId} disconnected",
                        match.GetPrimaryKey(out string _), this.GetPrimaryKeyString());
                }
            }
            await Logout();
            await state.WriteStateAsync();
        }

        #region AUTH

        private async Task<bool> AddMapping(string customIdForKey, string customId)
        {
            return await GrainFactory.GetGrain<ICustomIDToAccountIDMap>(customIdForKey)
                .SetAccountID(this.GetPrimaryKeyString(), customId);
        }

        public Task<bool> IsLoggedIn()
        {
            return Task.FromResult(State.IsLoggedIn);
        }

        /// <summary>
        /// Finishes a login attempt. A failed attempt changes nothing, so a wrong password cannot
        /// flip the state of a player who is logged in elsewhere. A successful one binds the session
        /// and stores the gateway's login ticket, which <see cref="Disconnected"/> checks.
        /// </summary>
        private async Task<LoginResult> CompleteLogin(bool credentialsValid, Guid sessionKey, Guid sentLoginTimeTicket)
        {
            if (!credentialsValid)
                return null;

            await AuthorizeSession(sessionKey, sentLoginTimeTicket);
            return new LoginResult(this.GetPrimaryKeyString(), State.Name, State.sessionToken.ToString());
        }

        public async Task Logout()
        {
            await SetPresenceStatus(OnlineStatus.Offline);
            State.IsLoggedIn = false;
            await EndOwnSession(State.sessionToken);
        }

        /// <summary>
        /// Ends a session this player logged in with, unless another player has logged in on it
        /// since (a socket that switches accounts keeps its session), and ignores a session that
        /// is already gone.
        /// </summary>
        private async Task EndOwnSession(Guid sessionKey)
        {
            if (sessionKey == Guid.Empty)
                return;
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            try
            {
                var info = await session.GetDeveloperKey();
                if (info.UserID == this.GetPrimaryKeyString())
                    await session.EndSession();
            }
            catch (SessionNotInitializedException)
            {
            }
        }

        public async Task SetPresenceStatus(OnlineStatus status)
        {
            State.OnlineStatus = status;
            if (State.OnlineStatus == OnlineStatus.Offline)
                timer?.Dispose();
            await GrainFactory.GetGrain<IPlayerFriendList>(State.lastGameKey, this.GetPrimaryKeyString(), null)
                .SetStatus(State.Name, status);
            await state.WriteStateAsync();
        }

        private async Task AuthorizeSession(Guid sessionKey, Guid loginTicket)
        {
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            await session.StartAuthenticatedUserSession(State.sessionToken.ToString(), this.GetPrimaryKeyString());
            // Change the login state only once the session accepted the user, so a login on an
            // expired or unknown session leaves the current login and its ticket alone.
            Guid previousSession = State.sessionToken;
            State.IsLoggedIn = true;
            State.LoginTicket = loginTicket;
            State.sessionToken = sessionKey;
            State.lastGameKey = (await session.GetDeveloperKey()).GameKey;
            State.LastHeartBeat = DateTime.Now;
            await SetPresenceStatus(OnlineStatus.Online);
            for (int i = State.RunningGames.Count - 1; i >= 0; --i)
            {
                bool isValid = await State.RunningGames[i].IsValidGame();
                if (!isValid)
                {
                    State.RunningGames.RemoveAt(i);
                }
            }

            timer?.Dispose();
            timer = this.RegisterGrainTimer<object>(OnTimer, null, new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(0),
                Period = TimeSpan.FromSeconds(10),
                Interleave = true
            });
            await state.WriteStateAsync();

            // The new login replaces the previous one, so the ticket of the previous session must
            // stop working instead of acting as this player until it idles out.
            if (previousSession != sessionKey)
                await EndOwnSession(previousSession);
        }

        private void AssertIsCreated()
        {
            if (!State.IsCreated)
                throw new InvalidOperationException("Account should be created before logging in.");
        }

        //---

        public async Task<AdditionResult> AddPlayerDevice(string deviceID)
        {
            if (State.Devices.Contains(deviceID))
                return AdditionResult.AlreadyExisting;

            bool isMapAdded = await AddMapping(UserMappingUtility.GetDeviceIdMappingId(deviceID), deviceID);
            if (isMapAdded)
            {
                State.Devices.Add(deviceID);
                await state.WriteStateAsync();
                return AdditionResult.Success;
            }
            else
            {
                return AdditionResult.AlreadyExisting;
            }
        }

        public async Task<AdditionResult> CreateAccountWithDevice(string device)
        {
            if (State.IsCreated)
            {
                throw new InvalidOperationException("Already exists");
            }

            State.IsCreated = true;
            State.Name = "";
            State.Devices = new List<string>();
            return await AddPlayerDevice(device);
        }

        public async Task<LoginResult> LoginWithDeviceID(string deviceID, Guid sessionKey, Guid sentLoginTimeTicket)
        {
            AssertIsCreated();
            return await CompleteLogin(State.Devices.Contains(deviceID), sessionKey, sentLoginTimeTicket);
        }

        public Task<List<string>> GetPlayerDevices()
        {
            return Task.FromResult(State.Devices);
        }

        public async Task<AdditionResult> AddPlayerUserAndPassword(string user, string password)
        {
            if (!user.All(x => IsValidCharacter(x)))
                throw new ArgumentException($"User name {user} contains invalid characters");
            PasswordHasher.EnsureAcceptable(password);

            if (State.UserNamePasswordPairs.ContainsKey(user))
                return AdditionResult.AlreadyExisting;


            bool isMappingAdded = await GrainFactory.GetGrain<IUserIdToAccountIdMap>(user)
                .SetAccountID(this.GetPrimaryKeyString());
            if (isMappingAdded)
            {
                string passwordHash = await PasswordHasher.HashAsync(password);
                State.UserNamePasswordPairs.Add(user,
                    new Dictionary<string, string> {{PASSWORD_KEY, passwordHash}});
                await state.WriteStateAsync();
                return AdditionResult.Success;
            }
            else
            {
                return AdditionResult.AlreadyExisting;
            }
        }

        public async Task<AdditionResult> CreateAccountWithUserAndPassword(string user, string password)
        {
            if (State.IsCreated)
            {
                throw new InvalidOperationException("Already created");
            }
            PasswordHasher.EnsureAcceptable(password);

            State.IsCreated = true;
            State.Name = "";
            State.UserNamePasswordPairs = new Dictionary<string, Dictionary<string, string>>();
            return await AddPlayerUserAndPassword(user, password);
        }

        public async Task<LoginResult> LoginWithUserNameAndPassword(string userName, string password, Guid sessionKey, Guid sentLoginTimeTicket)
        {
            AssertIsCreated();
            bool valid = State.UserNamePasswordPairs.TryGetValue(userName, out var account)
                         && await PasswordHasher.VerifyAsync(password, account[PASSWORD_KEY]);
            return await CompleteLogin(valid, sessionKey, sentLoginTimeTicket);
        }

        public Task<Dictionary<string, Dictionary<string, string>>> GetPlayerUserPasswordPairs()
        {
            return Task.FromResult(State.UserNamePasswordPairs);
        }

        public async Task<AdditionResult> AddPlayerEmail(string email, string password)
        {
            email = email.ToLower();
            if (!email.All(x => IsValidCharacter(x)))
                throw new ArgumentException($"Email {email} contains invalid characters");
            if (!email.Contains("@"))
                throw new ArgumentException($"email {email} is not a valid email address");
            PasswordHasher.EnsureAcceptable(password);
            if (State.Emails.ContainsKey(email))
                return AdditionResult.AlreadyExisting;

            bool isMappingAdded = await GrainFactory.GetGrain<IEmailToAccountIdMap>(email)
                .SetAccountID(this.GetPrimaryKeyString());
            if (isMappingAdded)
            {
                string passwordHash = await PasswordHasher.HashAsync(password);
                string activationCode = VerificationCode.Create(RANDOM_TOKEN_LENGTH);
                State.Emails.Add(email,
                    new Dictionary<string, string>
                    {
                        {PASSWORD_KEY, passwordHash},
                        {STATUS_KEY, STATUS_NOT_ACTIVE}
                    });
                State.ActivationCodes[email] = NewEmailCode(activationCode);
                await state.WriteStateAsync();
                // The account exists once the state is written, so a failed send is logged rather
                // than failing the call. A reset code (SendPasswordChangeCodeToEmail) also
                // activates the address, which is the way to get a new code.
                try
                {
                    await emailSender.SendAsync(email, EmailTexts.ActivationSubject, EmailTexts.ActivationBody(activationCode));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not send the activation email for player {PlayerId}", this.GetPrimaryKeyString());
                }
                return AdditionResult.Success;
            }
            else
            {
                return AdditionResult.AlreadyExisting;
            }
        }

        private static bool IsValidCharacter(char x)
        {
            return (x >= 'a' && x <= 'z')
                   || (x >= 'A' && x <= 'Z')
                   || (x >= '0' && x <= '9')
                   || x == '_' || x == '-' || x == '@' || x == '.';
        }

        public async Task<AdditionResult> CreateAccountWithEmail(string email, string password)
        {
            email = email.ToLower();
            if (State.IsCreated)
            {
                throw new InvalidOperationException("Already created");
            }
            PasswordHasher.EnsureAcceptable(password);

            State.IsCreated = true;
            State.Name = "";
            State.Emails = new Dictionary<string, Dictionary<string, string>>();
            return await AddPlayerEmail(email, password);
        }


        public async Task<LoginResult> LoginWithEmail(string email, string password, Guid sessionKey, Guid sentLoginTimeTicket)
        {
            email = email.ToLower();
            AssertIsCreated();
            bool valid = State.Emails.TryGetValue(email, out var account)
                         && account[STATUS_KEY] == STATUS_ACTIVE
                         && await PasswordHasher.VerifyAsync(password, account[PASSWORD_KEY]);
            return await CompleteLogin(valid, sessionKey, sentLoginTimeTicket);
        }

        public Task<Dictionary<string, Dictionary<string, string>>> GetPlayerEmailAccounts()
        {
            return Task.FromResult(State.Emails);
        }

        #endregion

        #region NAME

        public Task<string> GetPlayerName()
        {
            return Task.FromResult(State.Name);
        }

        public async Task SetPlayerName(string name)
        {
            if (string.IsNullOrEmpty(name))
                State.Name = name;
            else
            {
                //await GrainFactory.GetGrain<INameToAccountIdMap>(name).SetId(this.GetPrimaryKeyString());
                State.Name = name;
            }

            await state.WriteStateAsync();
        }

        #endregion

        #region STORAGE

        public async Task<Tuple<bool, string>> GetPlayerString(string key)
        {
            IPlayerStorage playerStorage = GrainFactory.GetGrain<IPlayerStorage>(
                ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(), this.GetPrimaryKeyString()));
            return await playerStorage.GetPlayerString(key);
        }

        public Task<IPlayerStorage> GetStorageGrain()
        {
            IPlayerStorage playerStorage = GrainFactory.GetGrain<IPlayerStorage>(
                ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(), this.GetPrimaryKeyString()));
            return Task.FromResult(playerStorage);
        }


        public async Task<Tuple<bool, byte[]>> GetPlayerBinary(string key)
        {
            return await GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).GetPlayerBinary(key);
        }

        public Task<Tuple<bool, string>> GetPlayerPublicString(string key)
        {
            return GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).GetPlayerPublicString(key);
        }

        public async Task<Tuple<OnlineStatus, Dictionary<string, string>>> GetPlayerPublicStrings(string[] keys)
        {
            var data = await GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).GetPlayerPublicStrings(keys);
            return Tuple.Create(State.OnlineStatus, data);
        }

        public async Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key)
        {
            return await GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).GetPlayerPublicBinary(key);
        }

        public Task SetPlayerString(string key, string data)
        {
            return GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).SetPlayerString(key, data);
        }

        public Task SetPlayerBinary(string key, byte[] data)
        {
            return GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).SetPlayerBinary(key, data);
        }

        public Task SetPlayerPublicString(string key, string data)
        {
            return GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).SetPlayerPublicString(key, data);
        }

        public Task SetPlayerPublicBinary(string key, byte[] data)
        {
            return GrainFactory
                .GetGrain<IPlayerStorage>(ResourcesUtility.GetPlayPlayerStorageKey(State.lastGameKey.ToString(),
                    this.GetPrimaryKeyString())).SetPlayerPublicBinary(key, data);
        }

        #endregion

        // public Task<IUNetMessageProcessor> GetConnectionInfo()
        // {
        //     return Task.FromResult(connection);
        // }

        public Task<Guid> GetGameTitleKey()
        {
            return Task.FromResult(State.lastGameKey);
        }

        public async Task<bool> ChangePasswordForEmail(string email, string newPassword)
        {
            if (State.Emails.TryGetValue(email, out var account))
            {
                account[PASSWORD_KEY] = await PasswordHasher.HashAsync(newPassword);
                await state.WriteStateAsync();
                return true;
            }

            return false;
        }

        public async Task<string> SendPasswordChangeCodeToEmail(string email)
        {
            if (!State.Emails.ContainsKey(email))
                throw new ArgumentException("The email doesn't exist");
            string code = VerificationCode.Create(RANDOM_TOKEN_LENGTH);
            State.PasswordResetCodes[email] = NewEmailCode(code);
            await state.WriteStateAsync();
            // Unlike activation, a failed send fails the call: the caller asked for this email.
            await emailSender.SendAsync(email, EmailTexts.ResetSubject, EmailTexts.ResetBody(code));
            return code;
        }

        public async Task<bool> ResendActivationCode(string email)
        {
            email = (email ?? "").ToLowerInvariant();
            if (!State.Emails.TryGetValue(email, out var account) || account[STATUS_KEY] == STATUS_ACTIVE)
                return false;
            string code = VerificationCode.Create(RANDOM_TOKEN_LENGTH);
            State.ActivationCodes[email] = NewEmailCode(code);
            await state.WriteStateAsync();
            // Like a reset code, the caller asked for this email, so a failed send fails the call.
            await emailSender.SendAsync(email, EmailTexts.ActivationSubject, EmailTexts.ActivationBody(code));
            return true;
        }

        /// <summary>
        /// Activates the email whose activation code matches. The code is used up, so it cannot be
        /// used again, and it was never valid as a password reset code. A wrong code counts
        /// against the account's outstanding activation codes (see <see cref="MatchEmailCode"/>).
        /// </summary>
        public async Task<string> Activate(string code)
        {
            string email = MatchEmailCode(State.ActivationCodes, code, out bool codesChanged);
            if (email == null)
            {
                if (codesChanged)
                    await state.WriteStateAsync();
                throw new ArgumentException("The code is wrong or has expired.");
            }

            State.ActivationCodes.Remove(email);
            if (State.Emails.TryGetValue(email, out var account))
                account[STATUS_KEY] = STATUS_ACTIVE;
            await state.WriteStateAsync();
            return email;
        }

        /// <summary>
        /// Sets a new password for the email whose reset code (from
        /// <see cref="SendPasswordChangeCodeToEmail"/>) matches, and uses the code up. Returns false
        /// for a wrong, used or expired code, which counts against the outstanding reset codes.
        /// An unacceptable new password throws before the code is used up.
        /// </summary>
        public async Task<bool> ChangePasswordForEmailByCode(string code, string newPassword)
        {
            string email = MatchEmailCode(State.PasswordResetCodes, code, out bool codesChanged);
            if (email == null || !State.Emails.TryGetValue(email, out var account))
            {
                if (email != null)
                    codesChanged = State.PasswordResetCodes.Remove(email);
                if (codesChanged)
                    await state.WriteStateAsync();
                return false;
            }

            account[PASSWORD_KEY] = await PasswordHasher.HashAsync(newPassword);
            // The code arrived by email, which proves the address, so a reset also activates it.
            // That is the way back for a player whose activation code expired or was used up.
            account[STATUS_KEY] = STATUS_ACTIVE;
            State.ActivationCodes.Remove(email);
            State.PasswordResetCodes.Remove(email);
            await state.WriteStateAsync();
            return true;
        }

        /// <summary>
        /// How long an activation or password reset code stays valid after it is sent.
        /// </summary>
        public static readonly TimeSpan EmailCodeLifetime = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Wrong codes an account takes before its outstanding codes of that kind (activation or
        /// password reset) are thrown away and a new one has to be sent.
        /// </summary>
        public const int MaxEmailCodeAttempts = 5;

        private static EmailCode NewEmailCode(string code)
        {
            return new EmailCode { Code = code, IssuedAtUtc = DateTime.UtcNow };
        }

        /// <summary>
        /// Returns the email whose code in <paramref name="codes"/> matches <paramref name="code"/>,
        /// or null. Expired codes are removed first. The activation and reset calls carry only the
        /// code, so a miss counts as a failed attempt against every code still outstanding, and a
        /// code that reaches <see cref="MaxEmailCodeAttempts"/> is removed. Codes are compared in
        /// constant time. <paramref name="codesChanged"/> tells the caller to write state.
        /// </summary>
        private static string MatchEmailCode(Dictionary<string, EmailCode> codes, string code, out bool codesChanged)
        {
            codesChanged = false;
            DateTime now = DateTime.UtcNow;
            foreach (var expired in codes.Where(x => now - x.Value.IssuedAtUtc > EmailCodeLifetime).Select(x => x.Key).ToList())
            {
                codes.Remove(expired);
                codesChanged = true;
            }

            string match = null;
            if (!string.IsNullOrEmpty(code))
            {
                byte[] given = Encoding.UTF8.GetBytes(code);
                foreach (var entry in codes)
                {
                    bool equal = CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(entry.Value.Code ?? ""));
                    if (equal && match == null)
                        match = entry.Key;
                }
            }
            if (match != null)
                return match;

            foreach (var entry in codes.ToList())
            {
                entry.Value.FailedAttempts++;
                if (entry.Value.FailedAttempts >= MaxEmailCodeAttempts)
                    codes.Remove(entry.Key);
                codesChanged = true;
            }
            return null;
        }

        public async Task<bool> ChangePasswordForUserId(string user, string newPassword)
        {
            if (State.UserNamePasswordPairs.TryGetValue(user, out var account))
            {
                account[PASSWORD_KEY] = await PasswordHasher.HashAsync(newPassword);
                await state.WriteStateAsync();
                return true;
            }

            return false;
        }

        public Task<PlayerState> GetState()
        {
            AssertIsCreated();
            return Task.FromResult(State);
        }

        public Task HeartBeat()
        {
            State.LastHeartBeat = DateTime.Now;
            return Task.CompletedTask;
        }

        public async Task<List<PlayerRunningMatch>> GetPendingMatches()
        {
            List<PlayerRunningMatch> matches = new List<PlayerRunningMatch>();
            foreach (var match in State.RunningGames)
            {
                var result = await match.GetState();
                string ext;
                var id = match.GetPrimaryKey(out ext);
                List<IPlayer> players = new List<IPlayer>();
                RelayServerPlayerGameStatus playerStatus = RelayServerPlayerGameStatus.NotStarted;
                foreach (var p in result.Players)
                {
                    players.Add(p.player);
                    if (p.player.GetPrimaryKeyString() == this.GetPrimaryKeyString())
                    {
                        playerStatus = p.state;
                    }
                }

                if (playerStatus == RelayServerPlayerGameStatus.Disconnected)
                    matches.Add(new PlayerRunningMatch
                    {
                        MatchId = id,
                        StartTime = result.StartTime,
                        players = players,
                    });
            }

            return matches;
        }

        public Task RejoinMatch(IRelayServerGame game)
        {
            return game.PlayerRejoined(this.AsReference<IPlayer>());
        }
    }
}