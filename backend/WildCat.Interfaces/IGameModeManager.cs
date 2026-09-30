using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface IGameModeManager : IGrainWithGuidKey
    {
        Task<GameModeManagerState> GetAllGameModes();
        Task<GameMode> GetGameModeData(string name);
        Task AddOrUpdateGameMode(GameMode mode);
        Task Remove(string name);
    }

    [GenerateSerializer]
    [Alias("wildcat.GameModeManagerState")]
    public class GameModeManagerState
    {
        [Id(0)] public List<GameMode> GameModes { get; set; }

        public GameModeManagerState()
        {
            GameModes = new List<GameMode>();
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.GameMode")]
    public class GameMode
    {
        [Id(0)] public string Name { get; set; }
        [Id(1)] public int MinPlayers { get; set; }
        [Id(2)] public int MaxPlayers { get; set; }
        [Id(3)] public bool FinishGameWithOneDisconnect { get; set; }
        [Id(4)] public string Script { get; set; }
        public bool HasScript { get { return !string.IsNullOrEmpty(Script); } }
        [Id(5)] public List<MatchMakingCriteria> Criterias { get; set; }

        public GameMode()
        {
            Criterias = new List<MatchMakingCriteria>();
            FinishGameWithOneDisconnect = true;
        }

        [GenerateSerializer]
        [Alias("wildcat.GameMode.MatchMakingCriteria")]
        public class MatchMakingCriteria
        {
            [Id(0)] public bool IsMinPlayersEnough { get; set; }
            [Id(1)] public int AcceptableThreshold { get; set; }
            [Id(2)] public int StartTimeInSeconds { get; set; }
        }

        public static GameMode CreateDefault()
        {
            return new GameMode
            {
                FinishGameWithOneDisconnect = true,
                MinPlayers = 2,
                MaxPlayers = 2,
                Name = "Default",
                Criterias = new List<GameMode.MatchMakingCriteria> { new GameMode.MatchMakingCriteria {
                        AcceptableThreshold=10000,
                        StartTimeInSeconds=0
                    } }
            };
        }
    }
}