using System;
using System.Collections.Generic;

namespace WildCat
{
    /// <summary>A match the matchmaker found.</summary>
    public sealed class MatchInfo
    {
        public MatchInfo(Guid id, IReadOnlyList<string> playerIds, string data)
        {
            Id = id;
            PlayerIds = playerIds ?? new string[0];
            Data = data ?? "";
        }

        /// <summary>The match id, used by Relay.Send and the Matches calls.</summary>
        public Guid Id { get; }

        /// <summary>The account ids of every player in the match, the logged in player included.</summary>
        public IReadOnlyList<string> PlayerIds { get; }

        /// <summary>Extra data from the matchmaker (empty for now).</summary>
        public string Data { get; }
    }
}
