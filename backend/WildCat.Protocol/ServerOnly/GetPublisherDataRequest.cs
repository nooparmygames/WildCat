using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// GetPublisherData_S (64): server to server, carries the developer secret. Clients read
    /// publisher data with GetPublisherData (34) and a StringMessage key instead.
    /// </summary>
    public class GetPublisherDataRequest : MessageBase
    {
        public string key = "";
        public Guid developerKey = Guid.Empty;
        public Guid developerSecret = Guid.Empty;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(developerKey);
            writer.WriteGuid(developerSecret);
            writer.WriteString(key);
        }

        public override void Deserialize(WireReader reader)
        {
            developerKey = reader.ReadGuid();
            developerSecret = reader.ReadGuid();
            key = reader.ReadString();
        }
    }
}
