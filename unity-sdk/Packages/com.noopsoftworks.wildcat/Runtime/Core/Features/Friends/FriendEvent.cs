using WildCat.Protocol;

namespace WildCat
{
    /// <summary>What the friend events report: the other player, and the bytes they attached.</summary>
    public sealed class FriendEvent
    {
        public FriendEvent(string playerId, string playerName, OnlineStatus status, byte[] data)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Status = status;
            Data = data ?? new byte[0];
        }

        public string PlayerId { get; }

        public string PlayerName { get; }

        /// <summary>The friend's new status (meaningful for <see cref="FriendsClient.StatusChanged"/>).</summary>
        public OnlineStatus Status { get; }

        public byte[] Data { get; }
    }
}
