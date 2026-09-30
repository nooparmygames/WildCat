using System.Buffers.Binary;
using WildCat.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace WildCat.Tests
{
    /// <summary>
    /// Hostile collection counts: a frame of the largest size a client may send
    /// (Gateway:MaxMessageBytes, 1 MiB) whose collection count is as large as the bytes behind it
    /// allow must not make the decoder allocate much more than the frame itself. Before the limits,
    /// a 1 MiB AuthenticationRequest made the reader pre-size a dictionary of about 32 MB, and a
    /// GetFriends request of empty strings decoded into an 8 MB array.
    /// </summary>
    public class ProtocolAllocationTests
    {
        private const int Size = 1024 * 1024;

        private readonly ITestOutputHelper output;

        public ProtocolAllocationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// <paramref name="prefix"/>, then an int32 count, then zero bytes up to <see cref="Size"/>.
        /// The count is the bytes left divided by <paramref name="divisor"/>, so with divisor 1 it
        /// claims one item per byte and with the item's real minimum size every item is there.
        /// </summary>
        private static byte[] Payload(byte[] prefix, int divisor)
        {
            byte[] payload = new byte[Size];
            prefix.CopyTo(payload, 0);
            int left = Size - prefix.Length - 4;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(prefix.Length), left / divisor);
            return payload;
        }

        public static IEnumerable<object[]> HostilePayloads()
        {
            foreach (int divisor in new[] { 1, 2, 5 })
            {
                // ticket, login type, id, password, auto create; then the extra data map.
                yield return new object[] { typeof(AuthenticationRequest), new byte[] { 0, 0, 0, 0, 0 }, divisor };
                // ticket, login type, id, password; then the map.
                yield return new object[] { typeof(CreateAccountRequest), new byte[] { 0, 0, 0, 0 }, divisor };
                // login type, id, password; then the map.
                yield return new object[] { typeof(AddAuthenticationToAccountRequest), new byte[] { 0, 0, 0 }, divisor };
                yield return new object[] { typeof(GetFriendsRequestMessage), new byte[0], divisor };
                // inventory name; then the (item, count) pairs.
                yield return new object[] { typeof(RemoveItemInventoryRequestMessage), new byte[] { 0 }, divisor };
                yield return new object[] { typeof(BuyItemInventoryRequestMessage), new byte[] { 0 }, divisor };
                // typed flag, event name; then (type, value) pairs, type 0 being a one-byte bool.
                yield return new object[] { typeof(ScriptEventExecutionRequestMessage), new byte[] { 1, 0 }, divisor };
            }
        }

        [Theory]
        [MemberData(nameof(HostilePayloads))]
        public void HostilePayloadsAllocateAtMostTwiceTheirSize(Type type, byte[] prefix, int divisor)
        {
            byte[] payload = Payload(prefix, divisor);
            var decode = typeof(ProtocolAllocationTests).GetMethod(nameof(Decode), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type);
            decode.Invoke(null, new object[] { new byte[16] }); // JIT and first-use allocations out of the way

            long allocated = (long)decode.Invoke(null, new object[] { payload })!;
            output.WriteLine($"{type.Name}, count = bytes left / {divisor}: allocated {allocated:N0} bytes for {payload.Length:N0}");
            Assert.True(allocated <= 2L * payload.Length,
                $"Decoding a hostile {type.Name} of {payload.Length:N0} bytes allocated {allocated:N0} bytes.");
        }

        [Fact]
        public void AFrameWithAMillionPlayerTargetsAllocatesAtMostTwiceItsSize()
        {
            // type, header HasPlayerTarget, count, that many empty strings, an empty payload.
            byte[] frame = new byte[Size];
            BinaryPrimitives.WriteInt16LittleEndian(frame, (short)WildCatMsgType.GetNameRequest);
            frame[2] = (byte)WildCatMsgHeader.HasPlayerTarget;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(3), Size - 3 - 4 - 4);
            WireCodec.Decode(WireCodec.Encode(WildCatMessageBase.Create(new EmptyMessage(), WildCatMsgType.GetNameRequest, WildCatMsgHeader.normal)));

            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<WireFormatException>(() => WireCodec.Decode(frame));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"allocated {allocated:N0} bytes for {frame.Length:N0}");
            Assert.True(allocated <= 2L * frame.Length, $"Decoding the frame allocated {allocated:N0} bytes.");
        }

        /// <summary>Decodes <paramref name="payload"/> and returns the bytes this thread allocated meanwhile.</summary>
        private static long Decode<T>(byte[] payload) where T : MessageBase, new()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                WireCodec.DecodePayload<T>(payload);
            }
            catch (WireFormatException)
            {
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void AssertLimit<T>(int limit, Func<int, T> create, Func<T, byte[]> encode, Action<byte[]> decode)
        {
            decode(encode(create(limit)));
            var ex = Assert.Throws<WireFormatException>(() => decode(encode(create(limit + 1))));
            Assert.Contains($"at most {limit}", ex.Message);
        }

        private static string[] Keys(int count)
        {
            return Enumerable.Range(0, count).Select(i => "k" + i).ToArray();
        }

        [Fact]
        public void TheCollectionsClientsSendHaveCountLimits()
        {
            AssertLimit(WireLimits.MaxPublicDataKeys, n => new GetFriendsRequestMessage { publicKeys = Keys(n) },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<GetFriendsRequestMessage>(p));
            AssertLimit(WireLimits.MaxScriptArguments, n =>
                {
                    var request = new ScriptEventExecutionRequestMessage { eventName = "e" };
                    request.SetParametersAndTypes(Enumerable.Range(0, n).Select(i => (object)i).ToArray());
                    return request;
                },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<ScriptEventExecutionRequestMessage>(p));
            AssertLimit(WireLimits.MaxLoginDataEntries,
                n => new AuthenticationRequest { data = Keys(n).ToDictionary(k => k, k => k) },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<AuthenticationRequest>(p));
            AssertLimit(WireLimits.MaxLoginDataEntries,
                n => new CreateAccountRequest { data = Keys(n).ToDictionary(k => k, k => k) },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<CreateAccountRequest>(p));
            AssertLimit(WireLimits.MaxLoginDataEntries,
                n => new AddAuthenticationToAccountRequest { data = Keys(n).ToDictionary(k => k, k => k) },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<AddAuthenticationToAccountRequest>(p));
            AssertLimit(WireLimits.MaxInventoryItemsPerRequest,
                n => new RemoveItemInventoryRequestMessage { items = Keys(n).Select(k => (k, 1)).ToArray() },
                WireCodec.EncodePayload, p => WireCodec.DecodePayload<RemoveItemInventoryRequestMessage>(p));
            AssertLimit(WireLimits.MaxPlayerTargets,
                n => WildCatMessageBase.CreateWithPlayerTargets(new EmptyMessage(), WildCatMsgType.GetNameRequest,
                    WildCatMsgHeader.normal, Keys(n), "ticket"),
                WireCodec.Encode, f => WireCodec.Decode(f));
        }

        [Fact]
        public void CollectionsTheServerSendsAreNotLimited()
        {
            // Big answers still decode: the limits are for what clients send.
            var inventory = new GetInventoryResponseMessage { items = Keys(5000).Select(k => (k, 2)).ToArray() };
            Assert.Equal(5000, WireCodec.DecodePayload<GetInventoryResponseMessage>(WireCodec.EncodePayload(inventory)).items.Length);

            var sync = new MatchSyncEventData { Game = Guid.NewGuid() };
            foreach (var key in Keys(1000))
            {
                sync.gameProperties[key] = new byte[] { 1 };
                sync.playerProperties[key] = new Dictionary<string, byte[]> { { key, new byte[] { 2 } } };
            }
            var decoded = WireCodec.DecodePayload<MatchSyncEventData>(WireCodec.EncodePayload(sync));
            Assert.Equal(1000, decoded.gameProperties.Count);
            Assert.Equal(1000, decoded.playerProperties.Count);

            var board = new GetLeaderBoardResponse { type = GetLeaderBoardRequest.RequestType.Top };
            for (int i = 0; i < 500; ++i)
                board.entries.Add(new LeaderBoardEntry { playerId = "p" + i, userName = "n", score = i });
            Assert.Equal(500, WireCodec.DecodePayload<GetLeaderBoardResponse>(WireCodec.EncodePayload(board)).entries.Count);
        }
    }
}
