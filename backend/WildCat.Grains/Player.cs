using System;
using System.Collections.Generic;
using System.Linq;
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
    public class Player : Grain<PlayerState>, IPlayer
    {
        private const string PASSWORD_KEY = "password";
        private const string CODE_KEY = "code";
        private const string STATUS_KEY = "status";
        private const string STATUS_NOT_ACTIVE = "not active";
        private const int RANDOM_TOKEN_LENGTH = 6;
        private const string STATUS_ACTIVE = "active";


        private IGrainTimer timer;

        private IAsyncStream<PlayerConnectionMessage> connectionStream;
        private IStreamProvider streamProvider;

        private readonly IEmailSender emailSender;
        private readonly ILogger<Player> logger;

        public Player(IEmailSender emailSender, ILogger<Player> logger)
        {
            this.emailSender = emailSender;
            this.logger = logger;
        }

        public async override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            streamProvider = this.GetStreamProvider(StreamNames.PROVIDER_NAME);
            connectionStream = streamProvider.GetStream<PlayerConnectionMessage>(StreamNames.PlayerConnection(this.GetPrimaryKeyString()));
            await base.OnActivateAsync(cancellationToken);
        }

        public Task SendDataToConnection(IPlayer player, int channel, byte[] data)
        {
            if (this.GetPrimaryKeyString() == player.GetPrimaryKeyString())
            {
                connectionStream.OnNextAsync(new PlayerConnectionMessage { player = player, channelId = channel, data = data });
            }
            return Task.CompletedTask;
        }
        
        private async Task OnTimer(object state)
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
            return WriteStateAsync();
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

            return WriteStateAsync();
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
            for (int i = State.RunningGames.Count - 1; i >= 0; i--)
            {
                bool result = await State.RunningGames[i].PlayerDisconnected(this.AsReference<IPlayer>());
                if (result)
                    State.RunningGames.RemoveAt(i);
            }
            await Logout();
            await WriteStateAsync();
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
            await GrainFactory.GetGrain<ISession>(State.sessionToken).EndSession();
        }

        public async Task SetPresenceStatus(OnlineStatus status)
        {
            State.OnlineStatus = status;
            if (State.OnlineStatus == OnlineStatus.Offline)
                timer?.Dispose();
            await GrainFactory.GetGrain<IPlayerFriendList>(State.lastGameKey, this.GetPrimaryKeyString(), null)
                .SetStatus(State.Name, status);
            await WriteStateAsync();
        }

        private async Task AuthorizeSession(Guid sessionKey, Guid loginTicket)
        {
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            await session.StartAuthenticatedUserSession(State.sessionToken.ToString(), this.GetPrimaryKeyString());
            // Change the login state only once the session accepted the user, so a login on an
            // expired or unknown session leaves the current login and its ticket alone.
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
            await WriteStateAsync();
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
                await WriteStateAsync();
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
                State.UserNamePasswordPairs.Add(user,
                    new Dictionary<string, string> {{PASSWORD_KEY, PasswordHasher.Hash(password)}});
                await WriteStateAsync();
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
                         && PasswordHasher.Verify(password, account[PASSWORD_KEY]);
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
                string activationCode = VerificationCode.Create(RANDOM_TOKEN_LENGTH);
                State.Emails.Add(email,
                    new Dictionary<string, string>
                    {
                        {PASSWORD_KEY, PasswordHasher.Hash(password)},
                        {STATUS_KEY, STATUS_NOT_ACTIVE},
                        {CODE_KEY, activationCode}
                    });
                await WriteStateAsync();
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
                         && PasswordHasher.Verify(password, account[PASSWORD_KEY]);
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

            await WriteStateAsync();
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
            if (State.Emails.ContainsKey(email))
            {
                State.Emails[email][PASSWORD_KEY] = PasswordHasher.Hash(newPassword);
                await WriteStateAsync();
                return true;
            }

            return false;
        }

        public async Task<string> SendPasswordChangeCodeToEmail(string email)
        {
            if (!State.Emails.ContainsKey(email))
                throw new ArgumentException("The email doesn't exist");
            string code = VerificationCode.Create(RANDOM_TOKEN_LENGTH);
            State.Emails[email][CODE_KEY] = code;
            await WriteStateAsync();
            // Unlike activation, a failed send fails the call: the caller asked for this email.
            await emailSender.SendAsync(email, EmailTexts.ResetSubject, EmailTexts.ResetBody(code));
            return code;
        }

        public async Task<string> Activate(string code)
        {
            // A used reset code is stored as "", so an empty code must never match.
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("The code doesn't exist");
            var email = State.Emails.FirstOrDefault(x => x.Value.ContainsKey(CODE_KEY) && x.Value[CODE_KEY] == code);
            if (string.IsNullOrEmpty(email.Key))
                throw new ArgumentException("The code doesn't exist");
            email.Value[STATUS_KEY] = STATUS_ACTIVE;
            await WriteStateAsync();
            return email.Key;
        }

        public async Task<bool> ChangePasswordForEmailByCode(string code, string newPassword)
        {
            // A used code is stored as "", so an empty code must never match.
            if (string.IsNullOrWhiteSpace(code))
                return false;
            var emailKVP = State.Emails.FirstOrDefault(x => x.Value.ContainsKey(CODE_KEY) && x.Value[CODE_KEY] == code);
            if (!string.IsNullOrEmpty(emailKVP.Key))
            {
                State.Emails[emailKVP.Key][PASSWORD_KEY] = PasswordHasher.Hash(newPassword);
                State.Emails[emailKVP.Key][CODE_KEY] = "";
                await WriteStateAsync();
                return true;
            }

            return false;
        }

        public async Task<bool> ChangePasswordForUserId(string user, string newPassword)
        {
            if (State.UserNamePasswordPairs.ContainsKey(user))
            {
                State.UserNamePasswordPairs[user][PASSWORD_KEY] = PasswordHasher.Hash(newPassword);
                await WriteStateAsync();
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