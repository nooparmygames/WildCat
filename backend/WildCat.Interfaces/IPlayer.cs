using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace WildCat.Interfaces
{
    public interface IPlayer : IGrainWithStringKey
    {
        [AlwaysInterleave]
        Task<PlayerState> GetState();
        Task<bool> ChangePasswordForEmail(string email, string newPassword);
        Task<string> SendPasswordChangeCodeToEmail(string email);

        /// <summary>
        /// Sends a new activation code to <paramref name="email"/> when it is one of this
        /// player's email logins and is not activated yet. The new code replaces the outstanding
        /// one. Returns false, and sends nothing, for any other address.
        /// </summary>
        Task<bool> ResendActivationCode(string email);
        Task<string> Activate(string code);
        Task<bool> ChangePasswordForEmailByCode(string code, string newPassword);
        Task<bool> ChangePasswordForUserId(string user, string newPassword);
        Task<AdditionResult> CreateAccountWithDevice(string device);
        Task<LoginResult> LoginWithDeviceID(string deviceID, Guid sessionKey, Guid sentLoginTimeTicket);
        Task Logout();
        Task<bool> IsLoggedIn();
        Task<AdditionResult> AddPlayerDevice(string deviceID);
        Task<List<string>> GetPlayerDevices();
        [AlwaysInterleave]
        Task<IPlayerStorage> GetStorageGrain();
        Task<LoginResult> LoginWithUserNameAndPassword(string userName, string password, Guid sessionKey, Guid sentLoginTimeTicket);
        Task<Dictionary<string, Dictionary<string, string>>> GetPlayerUserPasswordPairs();
        Task<LoginResult> LoginWithEmail(string email, string password, Guid sessionKey, Guid sentLoginTimeTicket);
        Task<Dictionary<string, Dictionary<string, string>>> GetPlayerEmailAccounts();

        /// <summary>
        /// Tells the player that the socket of one of its logins closed. The call is ignored unless
        /// <paramref name="sentLoginTimeTicket"/> is the ticket passed to the current login, so a
        /// stale connection cannot log out a newer one.
        /// </summary>
        Task Disconnected(Guid sentLoginTimeTicket);
        Task<AdditionResult> AddPlayerUserAndPassword(string user, string password);
        Task<AdditionResult> CreateAccountWithUserAndPassword(string user, string password);
        Task<AdditionResult> AddPlayerEmail(string email, string password);
        Task<AdditionResult> CreateAccountWithEmail(string email, string password);

        Task<string> GetPlayerName();
        Task SetPlayerName(string name);

        Task<Tuple<bool, string>> GetPlayerString(string key);
        Task<Tuple<bool, string>> GetPlayerPublicString(string key);
        Task<Tuple<bool, byte[]>> GetPlayerBinary(string key);
        Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key);
        Task SetPlayerString(string key, string data);
        Task SetPlayerPublicString(string key, string data);
        Task SetPlayerBinary(string key, byte[] data);
        Task SetPlayerPublicBinary(string key, byte[] data);

        /// <summary>
        /// The player's online status and the public values it has for <paramref name="keys"/> in
        /// the game <paramref name="gameKey"/>. There is deliberately no variant without a game: the
        /// game the player last logged in to can belong to another developer.
        /// </summary>
        [AlwaysInterleave]
        Task<Tuple<OnlineStatus, Dictionary<string, string>>> GetPublicStringsInGame(Guid gameKey, string[] keys);

        Task<int> GetEloScore();
        Task SetEloScore(int score);
        Task GameStarted(IRelayServerGame game);

        /// <summary>
        /// The match is over for this player: drops it from the running matches. One way, because
        /// the match sends it while the player may be waiting on that match (Disconnected calls
        /// PlayerDisconnected), which deadlocked when the match waited for the answer.
        /// </summary>
        [OneWay]
        Task GameFinished(IRelayServerGame game);

        // Task<IUNetMessageProcessor> GetConnectionInfo();
        Task<Guid> GetGameTitleKey();

        Task<List<PlayerRunningMatch>> GetPendingMatches();
        Task RejoinMatch(IRelayServerGame game);

        Task HeartBeat();
        Task SetPresenceStatus(OnlineStatus status);

        /// <summary>
        /// Publishes an encoded frame to the player's socket. Interleaves with every other call:
        /// it only publishes to a stream, and matches call it while the player may be waiting on
        /// the same match (Disconnected, GetPendingMatches), which deadlocked both.
        /// </summary>
        [AlwaysInterleave]
        public Task SendDataToConnection(IPlayer player, int channel, byte[] data);
    }

    [GenerateSerializer]
    [Alias("wildcat.PlayerRunningMatch")]
    public class PlayerRunningMatch
    {
        [Id(0)] public Guid MatchId;
        [Id(1)] public DateTime StartTime;
        [Id(2)] public List<IPlayer> players;
    }

    [GenerateSerializer, Alias("wildcat.AdditionResult")]
    public enum AdditionResult : byte { Success, Failure, AlreadyExisting }

    [GenerateSerializer]
    [Alias("wildcat.LoginResult")]
    public class LoginResult
    {
        [Id(0)] public string name;
        [Id(1)] public string userID;
        [Id(2)] public string ticket;

        public LoginResult(string id, string name, string ticket)
        {
            this.userID = id;
            this.name = name;
            this.ticket = ticket;
        }

        public override string ToString()
        {
            return $"{name} {userID}";
        }
    }

    /// <summary>
    /// A code sent to an email address for activation or a password reset.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.EmailCode")]
    public class EmailCode
    {
        [Id(0)] public string Code { get; set; } = "";

        /// <summary>UTC time the code was sent; it expires after Player.EmailCodeLifetime.</summary>
        [Id(1)] public DateTime IssuedAtUtc { get; set; }

        /// <summary>Wrong codes tried while this one was outstanding.</summary>
        [Id(2)] public int FailedAttempts { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.PlayerState")]
    public class PlayerState
    {
        /// <summary>
        /// Key of the last title loggedin with the player
        /// </summary>
        [Id(0)] public Guid lastGameKey { get; set; }
        [Id(1)] public List<IRelayServerGame> RunningGames { get; set; }
        [Id(2)] public List<string> Devices { get; set; }
        [Id(4)] public Dictionary<string, Dictionary<string, string>> Emails { get; set; }
        [Id(5)] public Dictionary<string, Dictionary<string, string>> UserNamePasswordPairs { get; set; }

        [Id(6)] public string Name { get; set; }
        [Id(7)] public Dictionary<Guid, string> PerTitleName { get; set; }
        [Id(8)] public int EloScore { get; set; }
        [Id(9)] public Dictionary<Guid, int> EloScores { get; set; }

        [Id(10)] public bool IsCreated { get; set; }
        [Id(11)] public bool IsLoggedIn { get; set; }
        [Id(12)] public Guid sessionToken { get; set; }
        [Id(13)] public OnlineStatus OnlineStatus { get; set; }
        [Id(14)] public DateTime LastHeartBeat { get; set; }

        /// <summary>
        /// The gateway's ticket for the current login. <see cref="IPlayer.Disconnected"/> ignores
        /// a disconnect that carries any other ticket.
        /// </summary>
        [Id(15)] public Guid LoginTicket { get; set; }

        /// <summary>
        /// Outstanding email activation codes by email address. A code is removed when it is used,
        /// when it expires and after too many wrong tries.
        /// </summary>
        [Id(16)] public Dictionary<string, EmailCode> ActivationCodes { get; set; }

        /// <summary>
        /// Outstanding password reset codes by email address, kept apart from the activation codes
        /// so an activation email cannot be used to reset the password.
        /// </summary>
        [Id(17)] public Dictionary<string, EmailCode> PasswordResetCodes { get; set; }

        public PlayerState()
        {
            ActivationCodes = new Dictionary<string, EmailCode>();
            PasswordResetCodes = new Dictionary<string, EmailCode>();
            RunningGames = new List<IRelayServerGame>();
            Devices = new List<string>();
            Emails = new Dictionary<string, Dictionary<string, string>>();
            UserNamePasswordPairs = new Dictionary<string, Dictionary<string, string>>();
            EloScore = 1500;
            EloScores = new Dictionary<Guid, int>();
            IsCreated = false;
            IsLoggedIn = false;
            sessionToken = Guid.Empty;
            Name = "";
            PerTitleName = new Dictionary<Guid, string>();
            lastGameKey = Guid.Empty;
        }
    }
}
