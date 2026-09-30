using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// SetPublisherData_S (62): server to server, carries the developer secret. It lives in
    /// ServerOnly/, which the Unity SDK copy leaves out, because a game client must never hold a
    /// developer secret.
    /// </summary>
    public class SetPublisherDataRequest : MessageBase
    {
        public string key = "";
        public string value = "";
        public Guid developerKey = Guid.Empty;
        public Guid developerSecret = Guid.Empty;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteGuid(developerKey);
            writer.WriteGuid(developerSecret);
            writer.WriteString(key);
            writer.WriteString(value);
        }

        public override void Deserialize(WireReader reader)
        {
            developerKey = reader.ReadGuid();
            developerSecret = reader.ReadGuid();
            key = reader.ReadString();
            value = reader.ReadString();
        }
    }
}
