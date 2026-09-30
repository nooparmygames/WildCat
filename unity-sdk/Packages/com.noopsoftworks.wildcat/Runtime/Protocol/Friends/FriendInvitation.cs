using System;

namespace WildCat.Protocol
{
    /// <summary>A sent or received friend invitation in a GetFriends answer (89).</summary>
    public class FriendInvitation : MessageBase
    {
        public DateTime creationTime;
        public string otherPlayerName = "";
        public string OtherPlayerId = "";
        public byte[] data = new byte[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(OtherPlayerId);
            writer.WriteString(otherPlayerName);
            writer.WriteDateTime(creationTime);
            writer.WriteBytes(data);
        }

        public override void Deserialize(WireReader reader)
        {
            OtherPlayerId = reader.ReadString();
            otherPlayerName = reader.ReadString();
            creationTime = reader.ReadDateTime();
            data = reader.ReadBytes();
        }

        /// <summary>Decodes the invitation's data as a <typeparamref name="T"/>.</summary>
        public T GetMessage<T>() where T : MessageBase, new()
        {
            return WireCodec.DecodePayload<T>(data);
        }
    }
}
