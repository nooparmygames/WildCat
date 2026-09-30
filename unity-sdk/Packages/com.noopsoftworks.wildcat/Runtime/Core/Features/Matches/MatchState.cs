using System;
using System.Collections.Generic;

namespace WildCat
{
    /// <summary>Every property of a match, sent to a player who rejoined it.</summary>
    public sealed class MatchState
    {
        public MatchState(Guid matchId, IReadOnlyDictionary<string, byte[]> globalProperties,
            IReadOnlyDictionary<string, Dictionary<string, byte[]>> playerProperties)
        {
            MatchId = matchId;
            GlobalProperties = globalProperties ?? new Dictionary<string, byte[]>();
            PlayerProperties = playerProperties ?? new Dictionary<string, Dictionary<string, byte[]>>();
        }

        public Guid MatchId { get; }

        /// <summary>The global properties by key.</summary>
        public IReadOnlyDictionary<string, byte[]> GlobalProperties { get; }

        /// <summary>The player properties by player id, then key.</summary>
        public IReadOnlyDictionary<string, Dictionary<string, byte[]>> PlayerProperties { get; }
    }
}
