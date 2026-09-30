namespace WildCat.Protocol
{
    /// <summary>A payload with no fields. It still writes one byte (true), as it always has.</summary>
    public class EmptyMessage : MessageBase
    {
        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(true);
        }

        public override void Deserialize(WireReader reader)
        {
            reader.ReadBool();
        }
    }
}
