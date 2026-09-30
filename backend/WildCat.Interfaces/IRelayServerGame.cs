using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IRelayServerGame : IGrainWithGuidCompoundKey
    {
        Task<Dictionary<IPlayer, Tuple<Guid,string>>> StartGame(IPlayer[] players,GameMode gameMode);

        Task SendToAllBut(IPlayer player, int channelId, byte[] data);

        /// <summary>
        /// Sets a match property for everyone and pushes MatchPropertyChangedEvent to the players
        /// in the match, the setter included. Returns false and changes nothing when the match's
        /// properties would reach 10 KB. Throws GameNotRunningException when
        /// <paramref name="player"/> is not a player of the match.
        /// </summary>
        Task<bool> SetGameProperty(IPlayer player, string key, byte[] value);

        /// <summary>
        /// Sets one of <paramref name="player"/>'s own properties in the match and pushes
        /// MatchPropertyChangedEvent to the other players. Same limit and refusal as
        /// <see cref="SetGameProperty"/>.
        /// </summary>
        Task<bool> SetPlayerProperty(IPlayer player, string key, byte[] data);

        Task<bool> PlayerDisconnected(IPlayer player);
        Task PlayerStarted(IPlayer player);
        Task PlayerRejoined(IPlayer player);
        Task<bool> IsValidGame();
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

        /// <summary>The match and player properties together (values only) stay below this many bytes.</summary>
        public const int MaxPropertyBytes = 10240;

        public const string NotAPlayer = "You are not a player in this match.";
        public const string MatchFinished = "The match has finished.";

        private int PropertyBytes()
        {
            int total = 0;
            foreach (var value in GameProperties.Values)
                total += value?.Length ?? 0;
            foreach (var bag in PlayerProperties.Values)
            {
                foreach (var value in bag.Values)
                    total += value?.Length ?? 0;
            }
            return total;
        }

        public bool SetGameProperty(string key, byte[] value)
        {
            value ??= Array.Empty<byte>();
            int afterPotentialRemovalSize = PropertyBytes();
            if (GameProperties.TryGetValue(key, out var old))
                afterPotentialRemovalSize -= old?.Length ?? 0;
            if (afterPotentialRemovalSize + value.Length >= MaxPropertyBytes)
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
            int afterPotentialRemovalSize = PropertyBytes();
            if (bag.TryGetValue(key, out var old))
                afterPotentialRemovalSize -= old?.Length ?? 0;
            if (afterPotentialRemovalSize + value.Length >= MaxPropertyBytes)
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
            string playerKey = player.GetPrimaryKeyString();
            var found = Players.FirstOrDefault(x => x.player.GetPrimaryKeyString() == playerKey);
            if (found == null)
                throw new GameNotRunningException(NotAPlayer);
            return found;
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