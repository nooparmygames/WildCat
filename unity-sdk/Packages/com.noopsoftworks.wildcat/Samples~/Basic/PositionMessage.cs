using UnityEngine;
using WildCat.Protocol;

namespace WildCat.Samples.Basic
{
    /// <summary>
    /// A game message the server relays between the players of a match without reading it. Game
    /// messages derive from MessageBase and write their fields in Serialize, reading them back in
    /// the same order in Deserialize.
    /// </summary>
    public sealed class PositionMessage : MessageBase
    {
        public Vector3 Position;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteSingle(Position.x);
            writer.WriteSingle(Position.y);
            writer.WriteSingle(Position.z);
        }

        public override void Deserialize(WireReader reader)
        {
            Position.x = reader.ReadSingle();
            Position.y = reader.ReadSingle();
            Position.z = reader.ReadSingle();
        }
    }
}
