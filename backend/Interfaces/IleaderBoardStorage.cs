using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IleaderBoardStorage : IGrainWithStringKey
    {
        Task<List<LeaderBoardEntry>> GetEntries();

        Task WriteEntries(List<LeaderBoardEntry> data);
        // Task AddPlayerScore(IPlayer player, int score);
        // Task UpdatePlayerScore(IPlayer player, int score, int index);
    }
    
    [GenerateSerializer]
    [Alias("wildcat.LeaderBoardStorageState")]
    public class LeaderBoardStorageState
    {
        [Id(0)] public List<LeaderBoardEntry> entries { get; set; }
        public LeaderBoardStorageState()
        {
            entries = new List<LeaderBoardEntry>();
        }
    }
}