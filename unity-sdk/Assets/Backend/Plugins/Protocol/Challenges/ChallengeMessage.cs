using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// StartChallenge (17), AcceptChallenge (20), DenyChallenge (23) and the challenge events
    /// (19, 22, 25, wrapped in an OptionalResponse). On ChallengeAcceptedEvent (22)
    /// <see cref="GameKey"/> carries the id of the match that was created.
    /// </summary>
    public class ChallengeMessage : MessageBase
    {
        public string Player = "";
        public string PlayerName = "";
        public byte[] data = new byte[0];
        public Guid GameKey;
        public uint TimeOut = 15;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteUInt32(TimeOut);
            writer.WriteString(Player);
            writer.WriteString(PlayerName);
            bool hasData = data != null && data.Length != 0;
            writer.WriteBool(hasData);
            if (hasData)
                writer.WriteBytes(data);
            writer.WriteGuid(GameKey);
        }

        public override void Deserialize(WireReader reader)
        {
            TimeOut = reader.ReadUInt32();
            Player = reader.ReadString();
            PlayerName = reader.ReadString();
            data = reader.ReadBool() ? reader.ReadBytes() : new byte[0];
            GameKey = reader.ReadGuid();
        }
    }
}
