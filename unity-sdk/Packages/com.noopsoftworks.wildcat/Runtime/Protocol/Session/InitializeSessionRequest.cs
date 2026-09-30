using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// InitializeSession (6): opens a session for a game of a developer. The answer (7) is an
    /// OptionalResponse&lt;GuidMessage&gt; holding the session ticket.
    /// </summary>
    public class InitializeSessionRequest : MessageBase
    {
        public Guid DeveloperKey;
        public Guid GameKey;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(DeveloperKey);
            writer.WriteGuid(GameKey);
        }

        public override void Deserialize(WireReader reader)
        {
            DeveloperKey = reader.ReadGuid();
            GameKey = reader.ReadGuid();
        }
    }
}
