using System.Collections.Generic;

namespace WildCat
{
    /// <summary>The challenges the logged in player sent and received.</summary>
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
