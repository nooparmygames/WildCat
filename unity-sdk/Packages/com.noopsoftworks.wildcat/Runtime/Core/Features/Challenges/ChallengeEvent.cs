using System;

namespace WildCat
{
    /// <summary>What the challenge events report.</summary>
    public sealed class ChallengeEvent
    {
        public ChallengeEvent(string playerId, string playerName, uint timeoutSeconds, Guid matchId, byte[] data)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            TimeoutSeconds = timeoutSeconds;
            MatchId = matchId;
            Data = data ?? new byte[0];
        }

        /// <summary>The other player: who challenged, accepted or denied.</summary>
        public string PlayerId { get; }

        public string PlayerName { get; }

        /// <summary>
        /// How long the challenge stays open, in seconds, as the challenger asked (for an accepted
        /// or denied challenge, what it was sent with). Servers before this fix always sent 15.
        /// </summary>
        public uint TimeoutSeconds { get; }

        /// <summary>For an accepted challenge: the match both players are now in. Guid.Empty otherwise.</summary>
        public Guid MatchId { get; }

        /// <summary>The bytes the other player attached.</summary>
        public byte[] Data { get; }
    }
}
