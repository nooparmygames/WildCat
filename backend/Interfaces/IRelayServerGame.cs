using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IRelayServerGame : IGrainWithGuidCompoundKey
    {
        Task<Dictionary<IPlayer, Tuple<Guid,string>>> StartGame(IPlayer[] players,GameMode gameMode);

        Task SendToAllBut(IPlayer player, int channelId, byte[] data);
        Task SetGameProperty(string key, byte[] value);
        Task SetPlayerProperty(IPlayer player, string key, byte[] data);

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

        private const int MAX_DATA_SIZE = 10240;
        [Id(6)] private int storedDataSize;

        public bool SetGameProperty(string key, byte[] value)
        {
            int afterPotentialRemovalSize = storedDataSize;
            if (GameProperties.ContainsKey(key))
                afterPotentialRemovalSize -= GameProperties[key].Length;

            if (afterPotentialRemovalSize + value.Length < MAX_DATA_SIZE)
            {
                GameProperties[key] = value;
                storedDataSize = afterPotentialRemovalSize + value.Length;
                return true;
            }
            else
                return false;
        }

        public bool SetPlayerProperties(IPlayer player, string key, byte[] value)
        {
            string playerKey = player.GetPrimaryKeyString();
            if (!PlayerProperties.ContainsKey(playerKey))
                PlayerProperties = new Dictionary<string, Dictionary<string, byte[]>>();
            int afterPotentialRemovalSize = storedDataSize;
            if (PlayerProperties[playerKey].ContainsKey(key))
                afterPotentialRemovalSize -= PlayerProperties[playerKey][key].Length;
            if (afterPotentialRemovalSize + value.Length < MAX_DATA_SIZE)
            {
                PlayerProperties[playerKey][key] = value;
                storedDataSize = afterPotentialRemovalSize + value.Length;
                return true;
            }
            else
                return false;
        }

        public RelayServerGamePlayerState GetPlayerData(IPlayer player)
        {
            return Players.First(x => x.player.GetPrimaryKeyString() == player.GetPrimaryKeyString());
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