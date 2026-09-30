namespace WildCat.Protocol
{
    public class StringAndByteArrayMessage : MessageBase
    {
        public string stringValue = "";
        public byte[] byteArrayValue = new byte[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteString(stringValue);
            writer.WriteBytes(byteArrayValue);
        }

        public override void Deserialize(WireReader reader)
        {
            stringValue = reader.ReadString();
            byteArrayValue = reader.ReadBytes();
        }
    }
}
