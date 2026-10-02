using System;

namespace WildCat
{
    /// <summary>A match ended.</summary>
    public sealed class MatchFinished
    {
        public MatchFinished(Guid matchId, bool byDisconnect)
        {
            MatchId = matchId;
            ByDisconnect = byDisconnect;
        }

        public Guid MatchId { get; }

        /// <summary>True when it ended because a player disconnected (game modes that end on one disconnect).</summary>
        public bool ByDisconnect { get; }
    }
}
