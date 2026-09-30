namespace WildCat.Protocol
{
    /// <summary>
    /// The answer to GetPlayerPublicCloudData (71). The value is on the wire only when
    /// <see cref="hasValue"/> is true, and reads back as "" otherwise.
    /// </summary>
    public class GetPlayerPublicCloudDataResponse : MessageBase
    {
        public bool hasValue;
        public string value = "";

        public override void Serialize(WireWriter writer)
        {
            writer.WriteBool(hasValue);
            if (hasValue)
                writer.WriteString(value);
        }

        public override void Deserialize(WireReader reader)
        {
            hasValue = reader.ReadBool();
            value = hasValue ? reader.ReadString() : "";
        }
    }
}
