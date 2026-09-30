using System;
using System.Collections.Generic;

namespace WildCat
{
    /// <summary>A match the logged in player was in when they disconnected, which they can rejoin.</summary>
    public sealed class PendingMatch
    {
        public PendingMatch(Guid id, DateTime startTime, IReadOnlyList<string> playerIds)
        {
            Id = id;
            StartTime = startTime;
            PlayerIds = playerIds ?? new string[0];
        }

        public Guid Id { get; }

        /// <summary>When the match started, in UTC.</summary>
        public DateTime StartTime { get; }

        public IReadOnlyList<string> PlayerIds { get; }
    }
}
