namespace WildCat.Protocol
{
    /// <summary>SetCloudData (32) and SetPublicCloudData (72): a key and its string value.</summary>
    public class SetCloudDataRequest : MessageBase
    {
        public string key = "";
        public string value = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        public override void Deserialize(WireReader reader)
        {
            key = reader.ReadString();
            value = reader.ReadString();
        }
    }
}
