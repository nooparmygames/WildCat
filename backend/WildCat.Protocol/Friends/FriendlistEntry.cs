using System.Collections.Generic;

namespace WildCat.Protocol
{
    /// <summary>One friend in a GetFriends answer (89), with the public data asked for.</summary>
    public class FriendlistEntry : MessageBase
    {
        public string userId = "";
        public string name = "";
        public OnlineStatus status;
        public Dictionary<string, string> data = new Dictionary<string, string>();

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(userId);
            writer.WriteString(name);
            writer.WriteByte((byte)status);
            writer.WriteStringMap(data);
        }

        public override void Deserialize(WireReader reader)
        {
            userId = reader.ReadString();
            name = reader.ReadString();
            status = (OnlineStatus)reader.ReadByte();
            data = reader.ReadStringMap();
        }
    }
}
