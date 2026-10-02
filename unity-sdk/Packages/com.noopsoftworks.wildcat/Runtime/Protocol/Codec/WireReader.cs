using System;
using System.Collections.Generic;
using System.Text;

namespace WildCat.Protocol
{
    /// <summary>
    /// Reads the fields of a payload in the order <see cref="WireWriter"/> wrote them. Payloads get
    /// one in <see cref="MessageBase.Deserialize"/>; only <see cref="WireCodec"/> creates them.
    /// </summary>
    /// <remarks>
    /// Every read checks the bytes that are left, so a frame cut short, a negative length or a
    /// count larger than the rest of the frame can hold throws <see cref="WireFormatException"/>
    /// before anything is allocated for it. Collections are pre-sized for at most
    /// <see cref="MaxPresize"/> items, and the collections clients send have counts limited by
    /// <see cref="WireLimits"/>, so decoding a hostile frame allocates about as much as the frame.
    /// </remarks>
    public sealed class WireReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly byte[] buffer;
        private readonly int end;
        private int position;

        internal WireReader(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count), "The range is outside the buffer.");
            this.buffer = buffer;
            position = offset;
            end = offset + count;
        }

        /// <summary>Bytes not read yet.</summary>
        public int Remaining
        {
            get { return end - position; }
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public byte ReadByte()
        {
            Require(1);
            return buffer[position++];
        }

        public short ReadInt16()
        {
            Require(2);
            short value = (short)(buffer[position] | buffer[position + 1] << 8);
            position += 2;
            return value;
        }

        public int ReadInt32()
        {
            Require(4);
            int value = buffer[position]
                        | buffer[position + 1] << 8
                        | buffer[position + 2] << 16
                        | buffer[position + 3] << 24;
            position += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            return unchecked((uint)ReadInt32());
        }

        public long ReadInt64()
        {
            uint low = ReadUInt32();
            uint high = ReadUInt32();
            return unchecked((long)((ulong)high << 32 | low));
        }

        public float ReadSingle()
        {
            return BitConverter.Int32BitsToSingle(ReadInt32());
        }

        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble(ReadInt64());
        }

        /// <summary>Reads a string written by <see cref="WireWriter.WriteString"/>; never returns null.</summary>
        public string ReadString()
        {
            int length = Read7BitEncodedLength();
            Require(length);
            string value = Utf8.GetString(buffer, position, length);
            position += length;
            return value;
        }

        /// <summary>Reads an int32 length and that many bytes; never returns null.</summary>
        public byte[] ReadBytes()
        {
            int length = ReadInt32();
            if (length < 0)
                throw new WireFormatException($"A byte array has a negative length ({length}).");
            Require(length);
            byte[] value = new byte[length];
            Buffer.BlockCopy(buffer, position, value, 0, length);
            position += length;
            return value;
        }

        public Guid ReadGuid()
        {
            byte[] bytes = ReadBytes();
            if (bytes.Length != 16)
                throw new WireFormatException($"A Guid must be 16 bytes long, not {bytes.Length}.");
            return new Guid(bytes);
        }

        /// <summary>Reads a DateTime written by <see cref="WireWriter.WriteDateTime"/>; its Kind is Utc.</summary>
        public DateTime ReadDateTime()
        {
            long ticks = ReadInt64();
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new WireFormatException($"{ticks} is not a valid DateTime tick count.");
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Reads a double of milliseconds, rounded to the nearest tick (the same on .NET and on
        /// Unity's runtime, unlike TimeSpan.FromMilliseconds).
        /// </summary>
        public TimeSpan ReadTimeSpan()
        {
            double milliseconds = ReadDouble();
            double ticks = Math.Round(milliseconds * TimeSpan.TicksPerMillisecond);
            if (double.IsNaN(ticks) || ticks >= long.MaxValue || ticks < long.MinValue)
                throw new WireFormatException($"{milliseconds} milliseconds is not a valid TimeSpan.");
            return new TimeSpan((long)ticks);
        }

        /// <summary>
        /// Collections are pre-sized for at most this many items; bigger ones grow as their items
        /// are actually read, so a count alone never makes the reader allocate much.
        /// </summary>
        internal const int MaxPresize = 64;

        /// <summary>
        /// Reads the int32 count of a collection. Every item takes at least one byte on the wire,
        /// so a count larger than the bytes left is refused before anything is allocated.
        /// </summary>
        public int ReadCount()
        {
            return ReadCount(1, int.MaxValue);
        }

        /// <summary>
        /// Reads the int32 count of a collection whose items take at least
        /// <paramref name="minItemBytes"/> bytes each on the wire, and refuses a count the bytes
        /// left cannot hold or one above <paramref name="maxCount"/> before anything is allocated.
        /// </summary>
        public int ReadCount(int minItemBytes, int maxCount)
        {
            if (minItemBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(minItemBytes), "An item takes at least one byte.");
            int count = ReadInt32();
            if (count < 0)
                throw new WireFormatException($"A collection has a negative count ({count}).");
            if (count > maxCount)
                throw new WireFormatException($"A collection has {count} items; at most {maxCount} are allowed here.");
            if (count > Remaining / minItemBytes)
                throw new WireFormatException($"A collection claims {count} items but only {Remaining} bytes are left.");
            return count;
        }

        /// <summary>Reads a nested payload written by <see cref="WireWriter.WriteMessage{T}"/>.</summary>
        public T ReadMessage<T>() where T : MessageBase, new()
        {
            T message = new T();
            message.Deserialize(this);
            return message;
        }

        /// <summary>Reads a string array; <paramref name="maxCount"/> limits its length.</summary>
        public string[] ReadStringArray(int maxCount = int.MaxValue)
        {
            int count = ReadCount(1, maxCount);
            string[] values = new string[count];
            for (int i = 0; i < count; ++i)
                values[i] = ReadString();
            return values;
        }

        /// <summary>Reads a list of payloads; <paramref name="maxCount"/> limits its length.</summary>
        public List<T> ReadList<T>(int maxCount = int.MaxValue) where T : MessageBase, new()
        {
            int count = ReadCount(1, maxCount);
            List<T> items = new List<T>(Math.Min(count, MaxPresize));
            for (int i = 0; i < count; ++i)
                items.Add(ReadMessage<T>());
            return items;
        }

        /// <summary>
        /// Reads a map written by <see cref="WireWriter.WriteStringMap"/>; a repeated key keeps the
        /// last value. An entry takes at least two bytes (two empty strings).
        /// </summary>
        public Dictionary<string, string> ReadStringMap(int maxCount = int.MaxValue)
        {
            int count = ReadCount(2, maxCount);
            Dictionary<string, string> map = new Dictionary<string, string>(Math.Min(count, MaxPresize));
            for (int i = 0; i < count; ++i)
            {
                string key = ReadString();
                map[key] = ReadString();
            }
            return map;
        }

        /// <summary>
        /// Reads a map written by <see cref="WireWriter.WriteBytesMap"/>; a repeated key keeps the
        /// last value. An entry takes at least five bytes (an empty string and an empty array).
        /// </summary>
        public Dictionary<string, byte[]> ReadBytesMap(int maxCount = int.MaxValue)
        {
            int count = ReadCount(5, maxCount);
            Dictionary<string, byte[]> map = new Dictionary<string, byte[]>(Math.Min(count, MaxPresize));
            for (int i = 0; i < count; ++i)
            {
                string key = ReadString();
                map[key] = ReadBytes();
            }
            return map;
        }

        /// <summary>Throws unless every byte has been read.</summary>
        internal void EnsureFullyRead(string what)
        {
            if (Remaining != 0)
                throw new WireFormatException($"The {what} has {Remaining} unexpected bytes at the end.");
        }

        private void Require(int count)
        {
            if (count > end - position)
                throw new WireFormatException($"The data ends early: {count} more bytes were expected but only {end - position} are left.");
        }

        /// <summary>The length prefix BinaryWriter writes before a string: 7 bits per byte, low bits first.</summary>
        private int Read7BitEncodedLength()
        {
            int result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                if (shift == 28 && b > 0x07)
                    throw new WireFormatException("A string length does not fit in 31 bits.");
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new WireFormatException("A string length is not a valid 7-bit encoded number.");
        }
    }
}
