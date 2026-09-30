namespace WildCat.Protocol
{
    /// <summary>InviteFriend (90): the other player, identified as <see cref="type"/> says, plus data for them.</summary>
    public class InviteFriendRequest : MessageBase
    {
        public InvitationType type;
        public string value = "";
        public byte[] data = new byte[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(value);
            writer.WriteByte((byte)type);
            writer.WriteBytes(data);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadString();
            type = (InvitationType)reader.ReadByte();
            data = reader.ReadBytes();
        }
    }
}
