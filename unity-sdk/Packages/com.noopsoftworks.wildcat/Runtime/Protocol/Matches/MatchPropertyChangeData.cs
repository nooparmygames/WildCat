using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// MatchPropertyChangedEvent (128), sent by the match to its players; not wrapped in an
    /// OptionalResponse. <see cref="Player"/> is empty for a match (game) property; on the wire it
    /// is a presence flag followed by the id when there is one, and reads back as null when there
    /// is none.
    /// </summary>
    public class MatchPropertyChangeData : MessageBase
    {
        public Guid Game;
        public string Player;
        public string key = "";
        public byte[] value = new byte[0];

        public bool isGameProperty
        {
            get { return string.IsNullOrEmpty(Player); }
        }

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(Game);
            bool hasPlayer = !string.IsNullOrEmpty(Player);
            writer.WriteBool(hasPlayer);
            if (hasPlayer)
                writer.WriteString(Player);
            writer.WriteString(key);
            writer.WriteBytes(value);
        }

        public override void Deserialize(WireReader reader)
        {
            Game = reader.ReadGuid();
            Player = reader.ReadBool() ? reader.ReadString() : null;
            key = reader.ReadString();
            value = reader.ReadBytes();
        }
    }
}
