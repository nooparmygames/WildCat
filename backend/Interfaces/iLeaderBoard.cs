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

    [GenerateSerializer]
    [Alias("wildcat.LeaderBoardEntry")]
    public class LeaderBoardEntry
    {
        [Id(0)] public DateTime EntryTime { get; set; }
        [Id(1)] public string PlayerID { get; set; }
        [Id(2)] public string Name { get; set; }
        [Id(3)] public int Score { get; set; }

        public LeaderBoardEntry(string player, string name, int score, DateTime dateTime)
        {
            this.PlayerID = player;
            this.EntryTime = dateTime;
            this.Score = score;
            this.Name = name;
        }
    }

    /// <summary>
    /// One leaderboard reward entry. Stored state uses this class instead of a (string, int)
    /// tuple because the JSON grain storage serializer does not allow ValueTuple types.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.LeaderBoardReward")]
    public class LeaderBoardReward
    {
        [Id(0)] public string Item { get; set; }
        [Id(1)] public int Count { get; set; }
    }

    [GenerateSerializer]
    [Alias("wildcat.LeaderBoardState")]
    public class LeaderBoardState
    {
        [GenerateSerializer, Alias("wildcat.LeaderBoardState.LeaderBoardType")]
        public enum LeaderBoardType { Weekly, Monthly, Yearly, Permanent }
        [Id(0)] public LeaderBoardType Type { get; set; }
        [Id(1)] public List<LeaderBoardReward> rewards { get; set; }
        [Id(2)] public string Name { get; set; }
        // public List<LeaderBoardEntry> entries { get; set; }
        [Id(3)] public int NumberOfLists { get; set; }
        [Id(4)] public List<string> StorageGrainKeys;
        [Id(5)] public int MaxListLength { get; }
        [Id(6)] public DateTime start;
        [Id(7)] public DateTime end;
        [Id(8)] public bool AcceptBetterScoresOnly { get; set; }

        public LeaderBoardState()
        {
            // entries = new List<LeaderBoardEntry>();
            StorageGrainKeys = new List<string>();
            AcceptBetterScoresOnly = true;
            MaxListLength = 500000;
        }
    }
}
