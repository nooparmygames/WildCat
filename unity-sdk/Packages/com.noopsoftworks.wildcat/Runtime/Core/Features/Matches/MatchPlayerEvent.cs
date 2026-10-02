using System;

namespace WildCat
{
    /// <summary>A player joined, left or rejoined a match.</summary>
    public sealed class MatchPlayerEvent
    {
        public MatchPlayerEvent(Guid matchId, string player)
        {
            MatchId = matchId;
            Player = player;
        }

        public Guid MatchId { get; }

        /// <summary>The player's account id.</summary>
        public string Player { get; }
    }
}
