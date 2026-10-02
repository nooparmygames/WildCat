using System.Collections.Generic;

namespace WildCat
{
    /// <summary>Rows of a leaderboard, best first.</summary>
    public sealed class LeaderboardPage
    {
        public LeaderboardPage(IReadOnlyList<LeaderboardEntry> entries, int totalCount, int playerRank)
        {
            Entries = entries;
            TotalCount = totalCount;
            PlayerRank = playerRank;
        }

        public IReadOnlyList<LeaderboardEntry> Entries { get; }

        /// <summary>For GetTopAsync: how many entries the leaderboard has. -1 otherwise.</summary>
        public int TotalCount { get; }

        /// <summary>For GetAroundMeAsync: the logged in player's rank. -1 otherwise.</summary>
        public int PlayerRank { get; }
    }
}
