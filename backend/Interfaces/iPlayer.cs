using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace Interfaces
{
    public interface IPlayer : IGrainWithStringKey
    {
        [AlwaysInterleave]
        Task<PlayerState> GetState();
        Task<bool> ChangePasswordForEmail(string email, string newPassword);
        Task<string> SendPasswordChangeCodeToEmail(string email);
        Task<string> Activate(string code);
        Task<bool> ChangePasswordForEmailByCode(string code, string newPassword);
        Task<bool> ChangePasswordForUserId(string user, string newPassword);
        Task<AdditionResult> CreateAccountWithDevice(string device);
        Task<LoginResult> LoginWithDeviceID(string deviceID, Guid sessionKey, Guid sentLoginTimeTicket);
        [AlwaysInterleave]
        Task<(string id, string type, string game)> GetPlayfabEntityAndGame();
        Task Logout();
        Task<bool> IsLoggedIn();
        Task<AdditionResult> AddPlayerDevice(string deviceID);
        Task<List<string>> GetPlayerDevices();
        [AlwaysInterleave]
        Task<IPlayerStorage> GetStorageGrain();
        Task<LoginResult> LoginWithUserNameAndPassword(string userName, string password, Guid sessionKey, Guid sentLoginTimeTicket);
        Task<Dictionary<string, Dictionary<string, string>>> GetPlayerUserPassowrdPairs();
        Task<LoginResult> LoginWithEmail(string email, string password, Guid sessionKey, Guid sentLoginTimeTicket);
        Task<Dictionary<string, Dictionary<string, string>>> GetPlayerEmailAccounts();
        Task<LoginResult> LoginWithProvider(string providerName, Dictionary<string, string> data, Guid sessionKey, Guid sentLoginTimeTicket);
        Task<Dictionary<string, Dictionary<string, string>>> GetPlayerProviderAccounts();
        Task Disconnected(Guid sentLoginTimeTicket);
        Task<AdditionResult> AddPlayerUserAndPassword(string user, string password);
        Task<AdditionResult> CreateAccountWithUserAndPassword(string user, string password);
        Task<AdditionResult> AddPlayerEmail(string email, string password);
        Task<AdditionResult> CreateAccountWithEmail(string email, string password);
        Task<AdditionResult> AddPlayerProvider(string provider, Dictionary<string, string> data);
        Task<AdditionResult> CreateAccountWithProvider(string provider, Dictionary<string, string> data);

        Task<string> GetPlayerName();
        Task SetPlayerName(string name);

        Task<Tuple<bool, string>> GetPlayerString(string key);
        Task<Tuple<bool, string>> GetPlayerPublicString(string key);
        Task<Tuple<OnlineStatus, Dictionary<string, string>>> GetPlayerPublicStrings(string[] keys);
        Task<Tuple<bool, byte[]>> GetPlayerBinary(string key);
        Task<Tuple<bool, byte[]>> GetPlayerPublicBinary(string key);
        Task SetPlayerString(string key, string data);
        Task SetPlayerPublicString(string key, string data);
        Task SetPlayerBinary(string key, byte[] data);
        Task SetPlayerPublicBinary(string key, byte[] data);

        Task<int> GetEloScore();
        Task SetEloScore(int score);
        Task AddGameFinalResult(IGame match, GameResult result, int cost, int opponentElo);
        Task GameStarted(IRelayServerGame game);
        Task GameFinished(IRelayServerGame game);

        // Task<IUNetMessageProcessor> GetConnectionInfo();
        Task<Guid> GetGameTitleKey();

        Task<List<PlayerRunningMatch>> GetPendingMatches();
        Task RejoinMatch(IRelayServerGame game);

        Task HeartBeat();
        Task SetPresenseStatus(OnlineStatus status);
        public Task SendDataToConnection(IPlayer player, int channel, byte[] data);
    }

    public class PlayerRunningMatch
    {
        public Guid MatchId;
        public DateTime StartTime;
        public List<IPlayer> players;
    }

    public enum GameResult { Winner, Loser, Draw }
    public enum AdditionResult : byte { Success, Failure, AlreadyExisting }

    public class LoginResult
    {
        public string name;
        public string userID;
        public string ticket;

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

    [Serializable]
    public class PlayerState
    {
        /// <summary>
        /// Key of the last title loggedin with the player
        /// </summary>
        public Guid lastGameKey { get; set; }
        public List<IRelayServerGame> RunningGames { get; set; }
        public List<string> Devices { get; set; }
        public Dictionary<string, Dictionary<string, string>> Providers { get; set; }
        public Dictionary<string, Dictionary<string, string>> Emails { get; set; }
        public Dictionary<string, Dictionary<string, string>> UserNamePasswordPairs { get; set; }

        public string Name { get; set; }
        public Dictionary<Guid, string> PerTitleName { get; set; }
        public int EloScore { get; set; }
        public Dictionary<Guid, int> EloScores { get; set; }

        public bool IsCreated { get; set; }
        public bool IsLoggedIn { get; set; }
        public Guid sessionToken { get; set; }
        public OnlineStatus OnlineStatus { get; set; }
        public DateTime LastHeartBeat { get; set; }

        public PlayerState()
        {
            RunningGames = new List<IRelayServerGame>();
            Devices = new List<string>();
            Providers = new Dictionary<string, Dictionary<string, string>>();
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
