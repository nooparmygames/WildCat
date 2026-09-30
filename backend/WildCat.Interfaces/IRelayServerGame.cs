using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IRelayServerGame : IGrainWithGuidCompoundKey
    {
        Task<Dictionary<IPlayer, Tuple<Guid,string>>> StartGame(IPlayer[] players,GameMode gameMode);

        /// <summary>
        /// Relays <paramref name="data"/> to the other players who are in the match. Dropped when
        /// the match is not running or <paramref name="player"/> is not in it (never joined, left,
        /// dropped out or was never a player).
        /// </summary>
        Task SendToAllBut(IPlayer player, int channelId, byte[] data);

        /// <summary>
        /// Sets a match property for everyone and pushes MatchPropertyChangedEvent to the players
        /// in the match, the setter included. Returns false and changes nothing when the key is
        /// null or longer than <see cref="RelayServerGameState.MaxPropertyKeyLength"/> characters,
        /// or when the match's properties, keys and values together, would reach 10 KB. Throws
        /// GameNotRunningException when <paramref name="player"/> is not a player of the match.
        /// </summary>
        Task<bool> SetGameProperty(IPlayer player, string key, byte[] value);

        /// <summary>
        /// Sets one of <paramref name="player"/>'s own properties in the match and pushes
        /// MatchPropertyChangedEvent to the other players. Same limit and refusal as
        /// <see cref="SetGameProperty"/>.
        /// </summary>
        Task<bool> SetPlayerProperty(IPlayer player, string key, byte[] data);

        /// <summary>
        /// The player's socket closed. Returns true when the player is done with the match (the
        /// disconnect ended it, it had finished already, or the player had left it) and should
        /// drop it from its running matches. Changes nothing in a finished match or for a player
        /// who left.
        /// </summary>
        Task<bool> PlayerDisconnected(IPlayer player);

        /// <summary>
        /// The player joins the match it was matched into, and every player in it gets
        /// GamePlayerAddedEvent with the new player's account id. Throws GameNotRunningException
        /// when the match has finished or the player has left it.
        /// </summary>
        Task PlayerStarted(IPlayer player);

        /// <summary>
        /// A player who dropped out (disconnected) comes back. Throws GameNotRunningException
        /// when the match has finished and for any player who did not drop out.
        /// </summary>
        Task PlayerRejoined(IPlayer player);
        Task<bool> IsValidGame();

        /// <summary>
        /// The player leaves the match; the last one out finishes it. The player is then told to
        /// drop the match (IPlayer.GameFinished, one way). Leaving a finished match changes
        /// nothing in it.
        /// </summary>
        Task PlayerFinished(IPlayer player);


        Task<RelayServerGameStatus> GetGameState();
        Task<List<string>> GetPlayerNames();
        Task<List<IPlayer>> GetPlayers();
        Task<RelayServerPlayerGameStatus> GetPlayerStatus(IPlayer player);
        Task<RelayServerGameState> GetState();

    }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.RelayServerGameState")]
    public class RelayServerGameState
    {
        [Id(0)] public Dictionary<string, byte[]> GameProperties { get; set; }
        // Keyed by the player's primary key string, not IPlayer: the JSON grain storage serializer
        // cannot read a grain reference back as a dictionary key.
        [Id(1)] public Dictionary<string, Dictionary<string, byte[]>> PlayerProperties { get; set; }
        [Id(2)] public RelayServerGameStatus State { get; set; }
        [Id(3)] public List<RelayServerGamePlayerState> Players { get; set; }
        [Id(4)] public int MaxPlayers { get; set; }
        [Id(5)] public DateTime StartTime { get; set; }

        /// <summary>
        /// The mode the match was started with; kept so a reactivated match still knows it. Null
        /// for a match that was never started.
        /// </summary>
        [Id(7)] public GameMode Mode { get; set; }

        // Id 6 held a private running total of the property sizes, which the JSON grain storage
        // did not keep; the total is now counted from the properties. Do not reuse it.

        /// <summary>
        /// The match and player properties together, keys (as UTF-8) and values, stay below this
        /// many bytes. (Keys used not to count, so a few calls with huge keys stored megabytes.)
        /// </summary>
        public const int MaxPropertyBytes = 10240;

        /// <summary>Property keys are at most this many characters long.</summary>
        public const int MaxPropertyKeyLength = 128;

        public const string NotAPlayer = "You are not a player in this match.";
        public const string MatchFinished = "The match has finished.";
        public const string LeftMatch = "You have left this match.";
        public const string NotDroppedOut = "Only a player who dropped out of the match can rejoin it.";

        private static int PropertySize(string key, byte[] value)
        {
            return Encoding.UTF8.GetByteCount(key) + (value?.Length ?? 0);
        }

        private int PropertyBytes()
        {
            int total = 0;
            foreach (var property in GameProperties)
                total += PropertySize(property.Key, property.Value);
            foreach (var bag in PlayerProperties.Values)
            {
                foreach (var property in bag)
                    total += PropertySize(property.Key, property.Value);
            }
            return total;
        }

        /// <summary>
        /// Whether <paramref name="key"/> may be set to <paramref name="value"/> in
        /// <paramref name="properties"/> (the match's or one player's): the key has at most
        /// <see cref="MaxPropertyKeyLength"/> characters, and all properties stay below
        /// <see cref="MaxPropertyBytes"/> once the old value of the key is replaced.
        /// </summary>
        private bool Fits(Dictionary<string, byte[]> properties, string key, byte[] value)
        {
            if (key == null || key.Length > MaxPropertyKeyLength)
                return false;
            int total = PropertyBytes();
            if (properties.TryGetValue(key, out var old))
                total -= PropertySize(key, old);
            return total + PropertySize(key, value) < MaxPropertyBytes;
        }

        public bool SetGameProperty(string key, byte[] value)
        {
            value ??= Array.Empty<byte>();
            if (!Fits(GameProperties, key, value))
                return false;
            GameProperties[key] = value;
            return true;
        }

        /// <summary>
        /// Sets a property of <paramref name="player"/>, who must be a player of the match. (The
        /// legacy version replaced the whole dictionary for a player without properties yet and
        /// then threw KeyNotFoundException, so no player property was ever stored.)
        /// </summary>
        public bool SetPlayerProperties(IPlayer player, string key, byte[] value)
        {
            GetPlayerData(player);
            value ??= Array.Empty<byte>();
            string playerKey = player.GetPrimaryKeyString();
            if (!PlayerProperties.TryGetValue(playerKey, out var bag))
                bag = new Dictionary<string, byte[]>();
            if (!Fits(bag, key, value))
                return false;
            bag[key] = value;
            PlayerProperties[playerKey] = bag;
            return true;
        }

        /// <summary>
        /// The player's entry in the match. Throws GameNotRunningException when the player is not
        /// in it (an unknown match has no players), which the gateway answers with error code 1.
        /// </summary>
        public RelayServerGamePlayerState GetPlayerData(IPlayer player)
        {
            return FindPlayerData(player) ?? throw new GameNotRunningException(NotAPlayer);
        }

        /// <summary>The player's entry in the match, or null when the player is not in it.</summary>
        public RelayServerGamePlayerState FindPlayerData(IPlayer player)
        {
            string playerKey = player.GetPrimaryKeyString();
            return Players.FirstOrDefault(x => x.player.GetPrimaryKeyString() == playerKey);
        }

        public RelayServerGameState()
        {
            Players = new List<RelayServerGamePlayerState>();
            State = RelayServerGameStatus.NotStarted;
            GameProperties = new Dictionary<string, byte[]>();
            PlayerProperties = new Dictionary<string, Dictionary<string, byte[]>>();
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.RelayServerGamePlayerState")]
    public class RelayServerGamePlayerState
    {
        [Id(0)] public IPlayer player { get; set; }
        [Id(1)] public RelayServerPlayerGameStatus state { get; set; }
        [Id(2)] public DateTime lastMessageTime { get; set; }
    }

    [GenerateSerializer, Alias("wildcat.RelayServerGameStatus")]
    public enum RelayServerGameStatus { NotStarted, Started, SomeJoined, AllJoined, SomeFinished, AllFinished, FinishedWithDisconnect }
    [GenerateSerializer, Alias("wildcat.RelayServerPlayerGameStatus")]
    public enum RelayServerPlayerGameStatus { NotStarted, InGame, Finished, Disconnected }

    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.GameNotRunningException")]
    public class GameNotRunningException : Exception
    {
        public GameNotRunningException()
        {

        }

        public GameNotRunningException(string message) : base(message)
        {

        }

        public GameNotRunningException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}