namespace WildCat.Protocol
{
    /// <summary>
    /// The payload of the friend events (92, 95, 98, 99), wrapped in an OptionalResponse.
    /// </summary>
    public class FriendlistMessage : MessageBase
    {
        public string friend = "";
        public string friendName = "";
        public byte[] data = new byte[0];
        public OnlineStatus status;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteByte((byte)status);
            writer.WriteString(friend);
            writer.WriteString(friendName);
            bool hasData = data != null && data.Length != 0;
            writer.WriteBool(hasData);
            if (hasData)
                writer.WriteBytes(data);
        }

        public override void Deserialize(WireReader reader)
        {
            status = (OnlineStatus)reader.ReadByte();
            friend = reader.ReadString();
            friendName = reader.ReadString();
            data = reader.ReadBool() ? reader.ReadBytes() : new byte[0];
        }
    }
}
