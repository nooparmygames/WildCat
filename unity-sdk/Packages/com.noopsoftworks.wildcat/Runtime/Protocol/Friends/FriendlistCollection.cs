using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>The answer to GetFriends (89): friends plus sent and received invitations.</summary>
    public class FriendlistCollection : MessageBase
    {
        public List<FriendlistEntry> friends = new List<FriendlistEntry>();
        public FriendInvitationCollection sentInvitation = new FriendInvitationCollection();
        public FriendInvitationCollection receivedInvitations = new FriendInvitationCollection();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteList(friends);
            writer.WriteMessage(sentInvitation);
            writer.WriteMessage(receivedInvitations);
        }

        public override void Deserialize(WireReader reader)
        {
            friends = reader.ReadList<FriendlistEntry>();
            sentInvitation = reader.ReadMessage<FriendInvitationCollection>();
            receivedInvitations = reader.ReadMessage<FriendInvitationCollection>();
        }
    }
}
