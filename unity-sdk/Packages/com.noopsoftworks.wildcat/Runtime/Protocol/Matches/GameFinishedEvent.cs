using System;

namespace WildCat.Protocol
{
    /// <summary>GameFinishedEvent (10), sent by the match to its players; not wrapped in an OptionalResponse.</summary>
    public class GameFinishedEvent : MessageBase
    {
        public Guid Game;
        public bool FinishedWithDisconnect;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(FinishedWithDisconnect);
            writer.WriteGuid(Game);
        }

        public override void Deserialize(WireReader reader)
        {
            FinishedWithDisconnect = reader.ReadBool();
            Game = reader.ReadGuid();
        }
    }
}
