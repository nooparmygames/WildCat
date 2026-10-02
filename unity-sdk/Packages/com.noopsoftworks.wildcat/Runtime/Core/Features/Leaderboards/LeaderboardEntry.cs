using System;

namespace WildCat
{
    /// <summary>One row of a leaderboard.</summary>
    public sealed class LeaderboardEntry
    {
        public LeaderboardEntry(string playerId, string playerName, int score, DateTime entryTime)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Score = score;
            EntryTime = entryTime;
        }

        public string PlayerId { get; }

        public string PlayerName { get; }

        public int Score { get; }

        /// <summary>When the score was set, in UTC.</summary>
        public DateTime EntryTime { get; }
    }
}
