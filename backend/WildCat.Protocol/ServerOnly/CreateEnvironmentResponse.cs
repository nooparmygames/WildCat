using System;

namespace WildCat.Protocol
{
    /// <summary>The answer to CreateEnvironment (45): the new developer and game keys. Not served; see <see cref="CreateEnvironmentRequest"/>.</summary>
    public class CreateEnvironmentResponse : MessageBase
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
