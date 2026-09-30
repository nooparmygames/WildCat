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



    public enum GameFinalState {FinishedWithWinner,FinishedInDraw,Started,NotStarted }

    public enum PlayerStateInGame { Playing,Finished,Disconnected}

    public class PlayerInGameData<T> where T : new()
    {
        public PlayerStateInGame state;
        public T value;
        public IPlayer player;
        public int Elo;
        public string name;

        public PlayerInGameData(IPlayer player,T value)
        {
            this.value = value;
            this.player = player;
            this.state = Interfaces.PlayerStateInGame.Playing;
        }
    }
}
