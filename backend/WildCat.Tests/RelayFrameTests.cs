using System;
using System.IO;
using System.Linq;
using System.Text;
using WildCat.Gateway;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The gateway relays user messages (ids above HighestInternal with a game target) to the other
    /// players of a match. The relayed copy must not carry the sender's session ticket.
    /// </summary>
    public class RelayFrameTests
    {
        private const WildCatMsgType UserMessageType = WildCatMsgType.HighestInternal + 7;
        private const string Ticket = "0f8fad5b-d9cb-469f-a165-70867728950e";
        private const string GameId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

        [Fact]
        public void RelayedFrameDropsTheTicketAndKeepsTheRest()
        {
            var original = WildCatMessageBase.CreateWithGameTarget(
                new StringMessage { value = "move 3 4" }, UserMessageType, WildCatMsgHeader.normal, GameId, Ticket);
            original.SetRequestID(42);
            byte[] frame = original.ToByteArray();
            Assert.True(ContainsSequence(frame, Encoding.UTF8.GetBytes(Ticket)));

            byte[] relayed = RelayFrame.WithoutTicket(frame);

            Assert.False(ContainsSequence(relayed, Encoding.UTF8.GetBytes(Ticket)));
            var parsed = new WildCatMessageBase(relayed);
            Assert.Equal(UserMessageType, parsed.msgType);
            Assert.False(parsed.hasAuthData);
            Assert.Equal("", parsed.ticket);
            Assert.True(parsed.hasRequestID);
            Assert.Equal(42u, parsed.requestID);
            Assert.True(parsed.HasGameTarget);
            Assert.Equal(GameId, parsed.game);
            Assert.Equal(original.RawData, parsed.RawData);
            Assert.Equal("move 3 4", parsed.GetData<StringMessage>().value);

            // Byte for byte: the original layout minus the ticket string and its flag.
            byte[] expected = Frame(UserMessageType, WildCatMsgHeader.HasGameTarget | WildCatMsgHeader.HasRequestID,
                42, GameId, original.RawData);
            Assert.Equal(expected, relayed);
        }

        [Fact]
        public void RelayedFrameWithoutRequestIdStaysWithoutOne()
        {
            byte[] payload = { 0, 1, 2, 250, 251, 252, 253, 254, 255 };
            var original = WildCatMessageBase.CreateWithGameTarget(
                new EmptyMessage(), UserMessageType, WildCatMsgHeader.normal, GameId, Ticket);
            original.RawData = payload;

            byte[] relayed = RelayFrame.WithoutTicket(original.ToByteArray());

            Assert.Equal(Frame(UserMessageType, WildCatMsgHeader.HasGameTarget, null, GameId, payload), relayed);
            var parsed = new WildCatMessageBase(relayed);
            Assert.False(parsed.hasRequestID);
            Assert.False(parsed.hasAuthData);
            Assert.Equal(payload, parsed.RawData);
        }

        /// <summary>Writes a frame by hand, following docs/wire-protocol.md.</summary>
        private static byte[] Frame(WildCatMsgType type, WildCatMsgHeader header, uint? requestId, string game, byte[] payload)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((short)type);
                writer.Write((byte)header);
                if (requestId.HasValue)
                    writer.Write(requestId.Value);
                writer.Write(game);
                writer.Write(payload.Length);
                writer.Write(payload);
            }
            return stream.ToArray();
        }

        private static bool ContainsSequence(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; ++i)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                    return true;
            }
            return false;
        }
    }
}
