namespace WildCat.Protocol
{
    public class IntegerMessage : MessageBase
    {
        public int value;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteInt32(value);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadInt32();
        }
    }
}
