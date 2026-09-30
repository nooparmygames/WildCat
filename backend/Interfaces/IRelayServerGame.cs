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
        Task ReceivePlayerData(IPlayer player, int channelId, byte[] data);
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
    public class RelayServerGameState
    {
        public Dictionary<string, byte[]> GameProperties { get; set; }
        public Dictionary<IPlayer, Dictionary<string, byte[]>> PlayerProperties { get; set; }
        public RelayServerGameStatus State { get; set; }
        public List<RelayServerGamePlayerState> Players { get; set; }
        public int MaxPlayers { get; set; }
        public DateTime StartTime { get; set; }

        private const int MAX_DATA_SIZE = 10240;
        private int storedDataSize;

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
            if (!PlayerProperties.ContainsKey(player))
                PlayerProperties = new Dictionary<IPlayer, Dictionary<string, byte[]>>();
            int afterPotentialRemovalSize = storedDataSize;
            if (PlayerProperties[player].ContainsKey(key))
                afterPotentialRemovalSize -= PlayerProperties[player][key].Length;
            if (afterPotentialRemovalSize + value.Length < MAX_DATA_SIZE)
            {
                PlayerProperties[player][key] = value;
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
            PlayerProperties = new Dictionary<IPlayer, Dictionary<string, byte[]>>();
        }
    }

    public class RelayServerGamePlayerState
    {
        public IPlayer player { get; set; }
        public RelayServerPlayerGameStatus state { get; set; }
        public DateTime lastMessageTime { get; set; }
    }

    public enum RelayServerGameStatus { NotStarted, Started, SomeJoined, AllJoined, SomeFinished, AllFinished, FinishedWithDisconnect }
    public enum RelayServerPlayerGameStatus { NotStarted, InGame, Finished, Disconnected }

    [Serializable]
    public struct RelayServerGameEventData
    {
        public enum EventType {GameFinished,MessageBroadcast,PlayerJoined }
        public EventType Type { get; }

    }

    [Serializable]
    public class GameNotRunningException : Exception
    {
        public GameNotRunningException()
        {

        }

        public GameNotRunningException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext ctx):base(info,ctx)
        {

        }
    }
}