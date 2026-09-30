using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface ILeaderBoard : IManagedResource
    {
        Task Initialize(string name, DateTime startAt, DateTime EndTime, bool acceptBetterScoresOnly);
        Task AddPlayerScore(IPlayer player, int score);
        Task<int> GetPlayerRank(IPlayer playerID);
        Task<Tuple<int, List<LeaderBoardEntry>>> GetAroundMe(IPlayer playerID, int count);
        Task<Tuple<int, List<LeaderBoardEntry>>> GetTopEntries(int count, int offset);
        Task<List<LeaderBoardEntry>> GetEntries();
        Task FinishLeaderBoard();
        Task SetRewards(string[] items, int[] itemCounts);
        Task<(string, int)[]> GetRewards();
    }

    public class LeaderBoardEntry
    {
        public DateTime EntryTime { get; set; }
        public string PlayerID { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }

        public LeaderBoardEntry(string player, string name, int score, DateTime dateTime)
        {
            this.PlayerID = player;
            this.EntryTime = dateTime;
            this.Score = score;
            this.Name = name;
        }
    }

    public class LeaderBoardState
    {
        public enum LeaderBoardType { Weekly, Monthly, Yearly, Permanent }
        public LeaderBoardType Type { get; set; }
        public (string, int)[] rewards { get; set; }
        public string Name { get; set; }
        // public List<LeaderBoardEntry> entries { get; set; }
        public int NumberOfLists { get; set; }
        public List<string> StorageGrainKeys;
        public int MaxListLength { get; }
        public DateTime start;
        public DateTime end;
        public bool AcceptBetterScoresOnly { get; set; }

        public LeaderBoardState()
        {
            // entries = new List<LeaderBoardEntry>();
            StorageGrainKeys = new List<string>();
            AcceptBetterScoresOnly = true;
            MaxListLength = 500000;
        }
    }
}
