using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Reads leaderboards of the session's game. Clients cannot set scores: since roadmap step 4
    /// the server refuses SetLeaderboard from clients because players could post any score. Set
    /// scores from a server script event (WildCat.SetScoreInLeaderBoard in the script API) and
    /// run it with <see cref="ScriptsClient.ExecuteAsync(string, object[])"/>.
    /// </summary>
    public sealed class LeaderboardsClient : FeatureClient
    {
        internal LeaderboardsClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>The best <paramref name="count"/> entries from <paramref name="offset"/> (0 is the top).</summary>
        public async Task<LeaderboardPage> GetTopAsync(string leaderboard, int offset, int count,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireName(leaderboard, "leaderboard");
            if (offset < 0 || count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "The offset must not be negative and the count must be positive.");
            GetLeaderBoardResponse answer = await Get(leaderboard, GetLeaderBoardRequest.RequestType.Top, offset, count, cancellationToken).ConfigureAwait(Connection.CaptureContext);
            return new LeaderboardPage(Entries(answer), answer.count, -1);
        }

        /// <summary>About <paramref name="count"/> entries around the logged in player, with their rank.</summary>
        public async Task<LeaderboardPage> GetAroundMeAsync(string leaderboard, int count,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireName(leaderboard, "leaderboard");
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "The count must be positive.");
            GetLeaderBoardResponse answer = await Get(leaderboard, GetLeaderBoardRequest.RequestType.Around, 0, count, cancellationToken).ConfigureAwait(Connection.CaptureContext);
            return new LeaderboardPage(Entries(answer), -1, answer.playerRank);
        }

        /// <summary>The logged in player's rank in the leaderboard.</summary>
        public async Task<int> GetRankAsync(string leaderboard, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireName(leaderboard, "leaderboard");
            GetLeaderBoardResponse answer = await Get(leaderboard, GetLeaderBoardRequest.RequestType.Me, 0, 0, cancellationToken).ConfigureAwait(Connection.CaptureContext);
            return answer.playerRank;
        }

        private Task<GetLeaderBoardResponse> Get(string leaderboard, GetLeaderBoardRequest.RequestType type, int offset, int count,
            CancellationToken cancellationToken)
        {
            return Request<GetLeaderBoardResponse>(WildCatMsgType.GetLeaderboardRequest,
                new GetLeaderBoardRequest { leaderBoardName = leaderboard, type = type, offset = offset, count = count },
                WildCatMsgType.GetLeaderboardResponse, cancellationToken);
        }

        private static List<LeaderboardEntry> Entries(GetLeaderBoardResponse answer)
        {
            List<LeaderboardEntry> result = new List<LeaderboardEntry>();
            if (answer.entries == null)
                return result;
            foreach (WildCat.Protocol.LeaderBoardEntry entry in answer.entries)
                result.Add(new LeaderboardEntry(entry.playerId, entry.userName, entry.score, entry.entryTime));
            return result;
        }
    }
}
