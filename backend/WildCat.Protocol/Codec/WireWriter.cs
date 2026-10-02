using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WildCat.Protocol
{
    /// <summary>
    /// Writes the fields of a payload in the WildCat binary format. Payloads get one in
    /// <see cref="MessageBase.Serialize"/>; only <see cref="WireCodec"/> creates them.
    /// </summary>
    /// <remarks>
    /// The encoding rules live here and in <see cref="WireReader"/> and nowhere else, so the
    /// format can change in one place (see docs/wire-protocol.md):
    /// numbers are little-endian; bool is one byte; a string is its UTF-8 byte count as a 7-bit
    /// varint followed by the bytes, and null is written as ""; a byte array is an int32 length
    /// followed by the bytes, and null is written as empty; a Guid is written as a 16-byte array;
    /// a DateTime is the int64 tick count of the UTC instant (Local values are converted,
    /// Unspecified values are taken as UTC); a TimeSpan is a double of milliseconds; a collection is
    /// an int32 count followed by its items, and null is written as empty.
    /// </remarks>
    public sealed class WireWriter
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly BinaryWriter writer;

        internal WireWriter(Stream stream)
        {
            writer = new BinaryWriter(stream, Utf8, true);
        }

        internal void Flush()
        {
            writer.Flush();
        }

        public void WriteBool(bool value)
        {
            writer.Write(value);
        }

        public void WriteByte(byte value)
        {
            writer.Write(value);
        }

        public void WriteInt16(short value)
        {
            writer.Write(value);
        }

        public void WriteInt32(int value)
        {
            writer.Write(value);
        }

        public void WriteUInt32(uint value)
        {
            writer.Write(value);
        }

        public void WriteInt64(long value)
        {
            writer.Write(value);
        }

        public void WriteSingle(float value)
        {
            writer.Write(value);
        }

        public void WriteDouble(double value)
        {
            writer.Write(value);
        }

        /// <summary>Writes a string; null is written as "".</summary>
        public void WriteString(string value)
        {
            writer.Write(value ?? "");
        }

        /// <summary>Writes an int32 length and the bytes; null is written as an empty array.</summary>
        public void WriteBytes(byte[] value)
        {
            if (value == null)
            {
                writer.Write(0);
                return;
            }
            writer.Write(value.Length);
            writer.Write(value);
        }

        /// <summary>Writes the 16 bytes of <see cref="Guid.ToByteArray"/> as a byte array.</summary>
        public void WriteGuid(Guid value)
        {
            WriteBytes(value.ToByteArray());
        }

        /// <summary>
        /// Writes the int64 tick count of the UTC instant. Local values are converted to UTC;
        /// Unspecified values are written as they are, that is, taken as UTC. The result never
        /// depends on the culture or the time zone of the machine that writes it.
        /// </summary>
        public void WriteDateTime(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
            writer.Write(utc.Ticks);
        }

        /// <summary>Writes a TimeSpan as a double of milliseconds.</summary>
        public void WriteTimeSpan(TimeSpan value)
        {
            writer.Write(value.TotalMilliseconds);
        }

        /// <summary>Writes the int32 count that precedes the items of a collection.</summary>
        public void WriteCount(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "A count cannot be negative.");
            writer.Write(count);
        }

        /// <summary>Writes a nested payload inline, without a length or type tag.</summary>
        public void WriteMessage<T>(T message) where T : MessageBase, new()
        {
            (message ?? new T()).Serialize(this);
        }

        /// <summary>Writes a count and the strings; a null array is empty and null items are "".</summary>
        public void WriteStringArray(IList<string> values)
        {
            int count = values == null ? 0 : values.Count;
            WriteCount(count);
            for (int i = 0; i < count; ++i)
                WriteString(values[i]);
        }

        /// <summary>Writes a count and the nested payloads; null items are written as new instances.</summary>
        public void WriteList<T>(IList<T> items) where T : MessageBase, new()
        {
            int count = items == null ? 0 : items.Count;
            WriteCount(count);
            for (int i = 0; i < count; ++i)
                WriteMessage(items[i]);
        }

        /// <summary>Writes a count and the key and value of each entry; null values are "".</summary>
        public void WriteStringMap(IDictionary<string, string> map)
        {
            WriteCount(map == null ? 0 : map.Count);
            if (map == null)
                return;
            foreach (var entry in map)
            {
                WriteString(entry.Key);
                WriteString(entry.Value);
            }
        }

        /// <summary>Writes a count and the key and value of each entry; null values are empty arrays.</summary>
        public void WriteBytesMap(IDictionary<string, byte[]> map)
        {
            WriteCount(map == null ? 0 : map.Count);
            if (map == null)
                return;
            foreach (var entry in map)
            {
                WriteString(entry.Key);
                WriteBytes(entry.Value);
            }
        }
    }
}
