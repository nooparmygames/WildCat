using System.Buffers.Binary;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Bad input: whatever bytes a client sends, decoding either works or throws
    /// WireFormatException, and never allocates for a length the bytes cannot back.
    /// </summary>
    public class ProtocolDecodingTests
    {
        private static byte[] SampleFrame()
        {
            var frame = WildCatMessageBase.CreateWithPlayerTargets(
                OptionalResponse<FriendlistCollection>.CreateSuccessful(
                    (FriendlistCollection)ProtocolSampleData.CreatePayload(typeof(FriendlistCollection))),
                WildCatMsgType.GetFriendsResponse, WildCatMsgHeader.HasGameTarget, new[] { "p1" }, "ticket");
            frame.game = Guid.NewGuid().ToString();
            frame.SetRequestID(5);
            return WireCodec.Encode(frame);
        }

        [Fact]
        public void EveryTruncatedFrameIsAFormatError()
        {
            byte[] frame = SampleFrame();
            for (int length = 0; length < frame.Length; ++length)
                Assert.Throws<WireFormatException>(() => WireCodec.Decode(frame, 0, length));
            Assert.NotNull(WireCodec.Decode(frame));
        }

        [Fact]
        public void TrailingBytesAfterThePayloadAreAFormatError()
        {
            byte[] frame = SampleFrame();
            byte[] longer = frame.Concat(new byte[] { 0 }).ToArray();
            var ex = Assert.Throws<WireFormatException>(() => WireCodec.Decode(longer));
            Assert.Contains("unexpected bytes", ex.Message);
        }

        [Fact]
        public void DecodeReadsTheGivenRangeOnly()
        {
            byte[] frame = SampleFrame();
            byte[] padded = new byte[] { 9, 9 }.Concat(frame).Concat(new byte[] { 9 }).ToArray();
            var decoded = WireCodec.Decode(padded, 2, frame.Length);
            Assert.Equal(WildCatMsgType.GetFriendsResponse, decoded.msgType);
            Assert.Equal(5u, decoded.requestID);
        }

        [Fact]
        public void NegativeAndOversizedLengthsAreRefusedBeforeAllocating()
        {
            // A payload length of int.MaxValue with nothing behind it.
            byte[] hugePayload = { 0x06, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x7F };
            Assert.Throws<WireFormatException>(() => WireCodec.Decode(hugePayload));

            byte[] negativePayload = { 0x06, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
            Assert.Throws<WireFormatException>(() => WireCodec.Decode(negativePayload));

            // A player count of a billion.
            byte[] hugeCount = { 0x06, 0x00, 0x02, 0x00, 0xCA, 0x9A, 0x3B, 0x00, 0x00, 0x00, 0x00 };
            Assert.Throws<WireFormatException>(() => WireCodec.Decode(hugeCount));

            // A string whose 7-bit length does not fit in 31 bits.
            byte[] badStringLength = { 0x06, 0x00, 0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };
            Assert.Throws<WireFormatException>(() => WireCodec.Decode(badStringLength));

            // A list of a billion friends inside a payload.
            var payload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, 1_000_000_000);
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<FriendlistCollection>(payload));
            BinaryPrimitives.WriteInt32LittleEndian(payload, -1);
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<GetFriendsRequestMessage>(payload));
        }

        [Fact]
        public void AGuidMustBeSixteenBytes()
        {
            byte[] payload = { 0x03, 0x00, 0x00, 0x00, 1, 2, 3 };
            var ex = Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<GuidMessage>(payload));
            Assert.Contains("16 bytes", ex.Message);
        }

        [Fact]
        public void ADateTimeOutOfRangeIsAFormatError()
        {
            var payload = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(payload, long.MaxValue);
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<DateTimeMessage>(payload));
            BinaryPrimitives.WriteInt64LittleEndian(payload, -1);
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<DateTimeMessage>(payload));
        }

        [Fact]
        public void AnUnknownScriptValueTypeIsAFormatError()
        {
            byte[] payload = { 1, 20 };
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<ScriptEventExecutionResponseMessage>(payload));
        }

        [Fact]
        public void InvalidUtf8BecomesReplacementCharacters()
        {
            byte[] payload = { 0x02, 0xC3, 0x28 };
            Assert.Equal("\uFFFD(", WireCodec.DecodePayload<StringMessage>(payload).value);
        }

        [Fact]
        public void EmptyAndNullPayloadsAreFormatErrorsForTypesWithFields()
        {
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<StringMessage>(new byte[0]));
            Assert.Throws<WireFormatException>(() => WireCodec.DecodePayload<StringMessage>(null));
            Assert.NotNull(WireCodec.DecodePayload<MessageBase>(null));
        }

        [Fact]
        public void RandomBytesNeverThrowAnythingButFormatErrors()
        {
            var random = new Random(20260930);
            var payloadTypes = ProtocolSampleData.PayloadTypes().Where(t => t.GetConstructor(Type.EmptyTypes) != null).ToList();
            byte[] sample = SampleFrame();
            for (int i = 0; i < 3000; ++i)
            {
                byte[] bytes;
                if (i % 2 == 0)
                {
                    bytes = new byte[random.Next(0, 64)];
                    random.NextBytes(bytes);
                }
                else
                {
                    // A real frame with a few bytes flipped reaches deeper into the payloads.
                    bytes = (byte[])sample.Clone();
                    for (int flips = random.Next(1, 4); flips > 0; --flips)
                        bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                }

                AssertOnlyFormatErrors(() => WireCodec.Decode(bytes));
                var type = payloadTypes[i % payloadTypes.Count];
                AssertOnlyFormatErrors(() => ProtocolSampleData.Decode(type, bytes, out _));
            }
        }

        private static void AssertOnlyFormatErrors(Action decode)
        {
            try
            {
                decode();
            }
            catch (WireFormatException)
            {
            }
        }
    }
}
