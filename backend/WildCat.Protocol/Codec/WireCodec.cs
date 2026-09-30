using System;
using System.IO;

namespace WildCat.Protocol
{
    /// <summary>
    /// The one boundary between messages and bytes. The gateway, the grains that build server
    /// events and the Unity SDK encode and decode only through these methods, so the wire format
    /// can be replaced here without touching any of them.
    /// </summary>
    /// <remarks>
    /// A frame (one WebSocket binary message) is: int16 message type, byte header flags, then
    /// uint32 request id if <see cref="WildCatMsgHeader.HasRequestID"/>, string ticket if
    /// <see cref="WildCatMsgHeader.HasAuthData"/>, string game if
    /// <see cref="WildCatMsgHeader.HasGameTarget"/>, int32 count plus strings if
    /// <see cref="WildCatMsgHeader.HasPlayerTarget"/>, and last the payload as an int32 length plus
    /// bytes. Nothing may follow the payload. See <see cref="WireWriter"/> for the value encodings
    /// and docs/wire-protocol.md for the whole protocol.
    /// </remarks>
    public static class WireCodec
    {
        private static readonly byte[] EmptyPayload = new byte[0];

        /// <summary>Encodes a whole frame: header fields and payload bytes.</summary>
        public static byte[] Encode(WildCatMessageBase message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            using (MemoryStream stream = new MemoryStream())
            {
                WireWriter writer = new WireWriter(stream);
                writer.WriteInt16((short)message.msgType);
                writer.WriteByte((byte)message.header);
                if (message.hasRequestID)
                    writer.WriteUInt32(message.requestID);
                if (message.hasAuthData)
                    writer.WriteString(message.ticket);
                if (message.HasGameTarget)
                    writer.WriteString(message.game);
                if (message.hasPlayerTarget)
                    writer.WriteStringArray(message.players);
                writer.WriteBytes(message.RawData);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Decodes a whole frame. Throws <see cref="WireFormatException"/> for anything that is not one.</summary>
        public static WildCatMessageBase Decode(byte[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            return Decode(frame, 0, frame.Length);
        }

        /// <summary>Decodes the frame held in <paramref name="count"/> bytes of <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
        public static WildCatMessageBase Decode(byte[] buffer, int offset, int count)
        {
            WireReader reader = new WireReader(buffer, offset, count);
            WildCatMessageBase message = new WildCatMessageBase();
            message.msgType = (WildCatMsgType)reader.ReadInt16();
            message.header = (WildCatMsgHeader)reader.ReadByte();
            if (message.hasRequestID)
                message.requestID = reader.ReadUInt32();
            if (message.hasAuthData)
                message.ticket = reader.ReadString();
            if (message.HasGameTarget)
                message.game = reader.ReadString();
            if (message.hasPlayerTarget)
                message.players = reader.ReadStringArray();
            message.RawData = reader.ReadBytes();
            reader.EnsureFullyRead("frame");
            return message;
        }

        /// <summary>Encodes a payload on its own, as carried in <see cref="WildCatMessageBase.RawData"/>.</summary>
        public static byte[] EncodePayload<T>(T payload) where T : MessageBase
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            using (MemoryStream stream = new MemoryStream())
            {
                WireWriter writer = new WireWriter(stream);
                payload.Serialize(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Decodes a payload. Bytes left after the payload are ignored, so a type that reads less
        /// than was written (for example an OptionalResponse&lt;EmptyMessage&gt; read from a failed
        /// response of any type) still works. Throws <see cref="WireFormatException"/> when the
        /// bytes do not hold a <typeparamref name="T"/>.
        /// </summary>
        public static T DecodePayload<T>(byte[] payload) where T : MessageBase, new()
        {
            int consumed;
            return DecodePayload<T>(payload, out consumed);
        }

        /// <summary>
        /// Decodes a payload and reports how many bytes it used, which tests use to check that a
        /// type reads exactly what it writes.
        /// </summary>
        public static T DecodePayload<T>(byte[] payload, out int consumed) where T : MessageBase, new()
        {
            byte[] bytes = payload ?? EmptyPayload;
            WireReader reader = new WireReader(bytes, 0, bytes.Length);
            T message = new T();
            try
            {
                message.Deserialize(reader);
            }
            catch (WireFormatException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // A payload's own checks (an enum it cannot use, a cast of a value of the wrong
                // type) count as bad input too.
                throw new WireFormatException($"The payload is not a valid {typeof(T).Name}: {ex.Message}", ex);
            }
            consumed = bytes.Length - reader.Remaining;
            return message;
        }
    }
}
