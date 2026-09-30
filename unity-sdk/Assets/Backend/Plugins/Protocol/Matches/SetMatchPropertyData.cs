using System;

namespace WildCat.Protocol
{
    /// <summary>SetMatchProperty (129): a match property, or the sender's own property in the match.</summary>
    public class SetMatchPropertyData : MessageBase
    {
        public Guid Game;
        public bool isPlayerProperty;
        public string key = "";
        public byte[] value = new byte[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(Game);
            writer.WriteBool(isPlayerProperty);
            writer.WriteString(key);
            writer.WriteBytes(value);
        }

        public override void Deserialize(WireReader reader)
        {
            Game = reader.ReadGuid();
            isPlayerProperty = reader.ReadBool();
            key = reader.ReadString();
            value = reader.ReadBytes();
        }
    }
}
