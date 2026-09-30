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

        /// <summary>
        /// The player's account id, except for <see cref="MatchesClient.PlayerJoined"/>, where the
        /// server sends the player's display name (empty when none is set).
        /// </summary>
        public string Player { get; }
    }
}
