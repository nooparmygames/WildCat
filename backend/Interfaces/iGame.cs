using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IGame : IGrainWithGuidCompoundKey
    {
        
        Task<int> GetCost();
        Task<Dictionary<IPlayer,string>> StartGame(IPlayer[] players);
        Task<List<IPlayer>> GetPlayers();
        Task SetPlayerValue(IPlayer playerID,float value);
        Task<float> GetPlayerValue(IPlayer playerID);
        Task<float> GetOtherPlayerValue(IPlayer player);

        /// <summary>
        /// Declares the match finished and returns the guid of the winning player, empty in case of a draw
        /// </summary>
        /// <returns>index of the winning player, empty if draw</returns>
        Task<IPlayer> FinishGame();
        Task PlayerDisconnected(IPlayer player);
        Task PlayerFinished(IPlayer player, float finalValue);
        Task SetGameProperties(int cost);
        Task<GameFinalState> GetGameState();
        Task<PlayerStateInGame> GetPlayerState(IPlayer player);
        Task<List<string>> GetPlayerNames();
        
        
    }



    [GenerateSerializer, Alias("wildcat.GameFinalState")]
    public enum GameFinalState {FinishedWithWinner,FinishedInDraw,Started,NotStarted }

    [GenerateSerializer, Alias("wildcat.PlayerStateInGame")]
    public enum PlayerStateInGame { Playing,Finished,Disconnected}

    [GenerateSerializer]
    [Alias("wildcat.PlayerInGameData`1")]
    public class PlayerInGameData<T> where T : new()
    {
        [Id(0)] public PlayerStateInGame state;
        [Id(1)] public T value;
        [Id(2)] public IPlayer player;
        [Id(3)] public int Elo;
        [Id(4)] public string name;

        public PlayerInGameData(IPlayer player,T value)
        {
            this.value = value;
            this.player = player;
            this.state = Interfaces.PlayerStateInGame.Playing;
        }
    }
}
