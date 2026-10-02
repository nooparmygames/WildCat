using System.Collections.Generic;

namespace WildCat
{
    /// <summary>The friends of the logged in player and the invitations waiting for an answer.</summary>
    public sealed class FriendList
    {
        public FriendList(IReadOnlyList<Friend> friends, IReadOnlyList<FriendInvitationInfo> sentInvitations,
            IReadOnlyList<FriendInvitationInfo> receivedInvitations)
        {
            Friends = friends;
            SentInvitations = sentInvitations;
            ReceivedInvitations = receivedInvitations;
        }

        public IReadOnlyList<Friend> Friends { get; }

        public IReadOnlyList<FriendInvitationInfo> SentInvitations { get; }

        public IReadOnlyList<FriendInvitationInfo> ReceivedInvitations { get; }
    }
}
