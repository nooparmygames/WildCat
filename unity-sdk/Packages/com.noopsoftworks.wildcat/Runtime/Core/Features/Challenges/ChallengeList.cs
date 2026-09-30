using System.Collections.Generic;

namespace WildCat
{
    /// <summary>The challenges the logged in player sent and received.</summary>
    /// <remarks>
    /// Known server bug (docs/wire-protocol.md, GetPlayerChallenges): the server currently puts
    /// received challenges into the sent list, so <see cref="Received"/> is empty and
    /// <see cref="Sent"/> holds both until the challenges handler is ported.
    /// </remarks>
    public sealed class ChallengeList
    {
        public ChallengeList(IReadOnlyList<ChallengeInfo> sent, IReadOnlyList<ChallengeInfo> received)
        {
            Sent = sent;
            Received = received;
        }

        public IReadOnlyList<ChallengeInfo> Sent { get; }

        public IReadOnlyList<ChallengeInfo> Received { get; }
    }
}
