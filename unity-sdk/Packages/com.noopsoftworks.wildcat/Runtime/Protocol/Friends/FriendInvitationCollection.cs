using System.Collections.Generic;

namespace WildCat.Protocol
{
    public class FriendInvitationCollection : MessageBase
    {
        public List<FriendInvitation> invitations = new List<FriendInvitation>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteList(invitations);
        }

        public override void Deserialize(WireReader reader)
        {
            invitations = reader.ReadList<FriendInvitation>();
        }
    }
}
