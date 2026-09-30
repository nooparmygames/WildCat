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
        /// For a received challenge: how long it stays open, in seconds. The server does not pass
        /// the challenger's value on yet, so this is always 15 for now.
        /// </summary>
        public uint TimeoutSeconds { get; }

        /// <summary>For an accepted challenge: the match both players are now in. Guid.Empty otherwise.</summary>
        public Guid MatchId { get; }

        /// <summary>The bytes the other player attached.</summary>
        public byte[] Data { get; }
    }
}
