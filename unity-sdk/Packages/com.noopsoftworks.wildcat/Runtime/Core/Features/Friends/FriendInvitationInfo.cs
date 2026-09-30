using System;

namespace WildCat
{
    /// <summary>A friend invitation that is waiting for an answer.</summary>
    public sealed class FriendInvitationInfo
    {
        public FriendInvitationInfo(string playerId, string playerName, DateTime created, byte[] data)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Created = created;
            Data = data ?? new byte[0];
        }

        /// <summary>The other player: the sender of a received invitation, the receiver of a sent one.</summary>
        public string PlayerId { get; }

        public string PlayerName { get; }

        /// <summary>When the invitation was sent, in UTC.</summary>
        public DateTime Created { get; }

        /// <summary>The bytes the sender attached.</summary>
        public byte[] Data { get; }
    }
}
