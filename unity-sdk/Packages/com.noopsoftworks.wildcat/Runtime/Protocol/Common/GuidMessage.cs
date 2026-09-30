using System;

namespace WildCat.Protocol
{
    public class GuidMessage : MessageBase
    {
        public Guid value = Guid.Empty;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(value);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadGuid();
        }
    }
}
