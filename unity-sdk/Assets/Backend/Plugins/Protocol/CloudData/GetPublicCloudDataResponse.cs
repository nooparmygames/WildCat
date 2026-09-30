namespace WildCat.Protocol
{
    /// <summary>
    /// The answer to GetPublicCloudData (75). The value is on the wire only when
    /// <see cref="hasValue"/> is true, and reads back as "" otherwise. (It used to be written even
    /// when missing, and a missing value was null, so the write threw and no answer went out.)
    /// </summary>
    public class GetPublicCloudDataResponse : MessageBase
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
