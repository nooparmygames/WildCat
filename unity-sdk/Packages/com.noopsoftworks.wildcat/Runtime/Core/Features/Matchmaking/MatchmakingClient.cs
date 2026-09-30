using System;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Matchmaking per game mode. The server groups players of a game mode by skill, relaxing the
    /// skill range over time, and starts a relay match for them.
    /// </summary>
    public sealed class MatchmakingClient : FeatureClient
    {
        /// <summary>How long FindMatchAsync lets the matchmaker look when the caller does not say.</summary>
        public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(15);

        internal MatchmakingClient(WildCatConnection connection)
            : base(connection)
        {
        }

        /// <summary>
        /// Looks for a match in <paramref name="gameMode"/> for up to <paramref name="maxWait"/>
        /// (15 seconds by default, whole seconds; the server caps it at 20 seconds). Returns the
        /// match, or null when the matchmaker found nobody in time. The player is in the match
        /// when this returns: relay messages can be sent to it, and
        /// <see cref="MatchesClient.PlayerJoined"/> reports the other players.
        /// </summary>
        /// <remarks>
        /// Cancelling <paramref name="cancellationToken"/> only stops waiting for the answer: the
        /// protocol has no message to leave matchmaking, so the server keeps looking until its
        /// wait ends and may still put the player in a match. After a cancelled search, check
        /// <see cref="MatchesClient.GetPendingAsync"/> (and leave with
        /// <see cref="MatchesClient.LeaveAsync"/>) before searching again.
        /// </remarks>
        public async Task<MatchInfo> FindMatchAsync(string gameMode, uint skill = 0, TimeSpan? maxWait = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireText(gameMode, "gameMode");
            TimeSpan wait = maxWait ?? DefaultMaxWait;
            if (wait <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maxWait), "The matchmaking time must be positive.");
            uint waitSeconds = (uint)Math.Max(1, Math.Ceiling(wait.TotalSeconds));

            FindMatchResponse answer;
            try
            {
                answer = await Request<FindMatchResponse>(WildCatMsgType.FindMatchRequest,
                    new FindMatchRequest(gameMode, skill, waitSeconds), WildCatMsgType.FindMatchResponse, cancellationToken,
                    TimeSpan.FromSeconds(waitSeconds) + Connection.Options.RequestTimeout).ConfigureAwait(false);
            }
            catch (WildCatException ex) when (ex.ErrorCode == WildCatErrorCodes.Refused && !ex.IsGatewayRefusal)
            {
                // "Match not found": nobody to play with within the time.
                return null;
            }
            return new MatchInfo(answer.gameId, answer.players, answer.data);
        }
    }
}
