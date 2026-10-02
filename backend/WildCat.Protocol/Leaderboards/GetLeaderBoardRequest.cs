namespace WildCat.Protocol
{
    /// <summary>
    /// GetLeaderboard (26). <see cref="count"/> is sent for Top and Around, <see cref="offset"/>
    /// only for Top.
    /// </summary>
    public class GetLeaderBoardRequest : MessageBase
    {
        public enum RequestType : byte
        {
            Top,
            Around,
            Me
        }

        public string leaderBoardName = "";
        public RequestType type;
        public int count;
        public int offset;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteByte((byte)type);
            writer.WriteString(leaderBoardName);
            if (type != RequestType.Me)
                writer.WriteInt32(count);
            if (type == RequestType.Top)
                writer.WriteInt32(offset);
        }

        public override void Deserialize(WireReader reader)
        {
            type = (RequestType)reader.ReadByte();
            leaderBoardName = reader.ReadString();
            if (type != RequestType.Me)
                count = reader.ReadInt32();
            if (type == RequestType.Top)
                offset = reader.ReadInt32();
        }
    }
}
