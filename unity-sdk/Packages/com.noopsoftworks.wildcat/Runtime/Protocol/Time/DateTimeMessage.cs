using System;

namespace WildCat.Protocol
{
    /// <summary>
    /// A point in time, sent as UTC ticks (see <see cref="WireWriter.WriteDateTime"/>); the value
    /// read back has Kind Utc. GetServerTime (43) answers with one.
    /// </summary>
    public class DateTimeMessage : MessageBase
    {
        public DateTime value;

        public override void Serialize(WireWriter writer)
        {
            writer.WriteDateTime(value);
        }

        public override void Deserialize(WireReader reader)
        {
            value = reader.ReadDateTime();
        }
    }
}
