namespace WildCat.Protocol
{
    public class StringMessage : MessageBase
    {
        public string value = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(value);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadString();
        }
    }
}
