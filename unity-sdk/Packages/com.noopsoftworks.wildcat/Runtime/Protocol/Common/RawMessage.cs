namespace WildCat.Protocol
{
    /// <summary>A payload of opaque bytes, for example a relayed game message.</summary>
    public class RawMessage : MessageBase
    {
        public byte[] value = new byte[0];

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBytes(value);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadBytes();
        }
    }
}
