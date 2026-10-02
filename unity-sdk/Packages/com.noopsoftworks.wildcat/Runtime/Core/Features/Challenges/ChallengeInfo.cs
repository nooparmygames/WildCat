using System;

namespace WildCat
{
    /// <summary>A challenge the logged in player sent or received.</summary>
    public sealed class ChallengeInfo
    {
        public ChallengeInfo(string playerId, string playerName, ChallengeStatus status, Guid matchId, byte[] data)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Status = status;
            MatchId = matchId;
            Data = data ?? new byte[0];
        }

        /// <summary>The other player.</summary>
        public string PlayerId { get; }

        public string PlayerName { get; }

        public ChallengeStatus Status { get; }

        /// <summary>The match of a started challenge, or Guid.Empty.</summary>
        public Guid MatchId { get; }

        public byte[] Data { get; }
    }
}
