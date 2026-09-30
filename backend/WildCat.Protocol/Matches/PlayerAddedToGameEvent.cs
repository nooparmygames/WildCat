using System;

namespace WildCat.Protocol
{
    /// <summary>GamePlayerAddedEvent (9), sent by the match to its players; not wrapped in an OptionalResponse.</summary>
    public class PlayerAddedToGameEvent : MessageBase
    {
        public Guid Game;
        public string Player = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(Player);
            writer.WriteGuid(Game);
        }

        public override void Deserialize(WireReader reader)
        {
            Player = reader.ReadString();
            Game = reader.ReadGuid();
        }
    }
}
