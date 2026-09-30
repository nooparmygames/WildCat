using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The protocol bugs fixed in roadmap step 6 (docs/wire-protocol.md lists them), the value
    /// encodings that are now pinned (culture-free DateTime, null-safe strings and collections),
    /// and the frame layout byte for byte.
    /// </summary>
    public class ProtocolFixTests
    {
        private const WildCatMsgType UserMessageType = WildCatMsgType.HighestInternal + 3;

        [Fact]
        public void FrameLayoutIsPinnedByteForByte()
        {
            var frame = WildCatMessageBase.CreateWithPlayerTargets(new StringMessage { value = "hi" }, UserMessageType,
                WildCatMsgHeader.HasGameTarget, new[] { "p1", "p2" }, "tk");
            frame.game = "g";
            frame.SetRequestID(0x01020304);

            byte[] expected =
            {
                0x02, 0x04,                         // int16 message type 1026
                0x17,                               // flags: game 1 + players 2 + auth 4 + request id 16
                0x04, 0x03, 0x02, 0x01,             // uint32 request id
                0x02, (byte)'t', (byte)'k',         // ticket
                0x01, (byte)'g',                    // game
                0x02, 0x00, 0x00, 0x00,             // int32 player count
                0x02, (byte)'p', (byte)'1',
                0x02, (byte)'p', (byte)'2',
                0x03, 0x00, 0x00, 0x00,             // int32 payload length
                0x02, (byte)'h', (byte)'i',         // StringMessage
            };
            Assert.Equal(expected, WireCodec.Encode(frame));

            var decoded = WireCodec.Decode(expected);
            Assert.Equal(UserMessageType, decoded.msgType);
            Assert.Equal(0x01020304u, decoded.requestID);
            Assert.Equal("tk", decoded.ticket);
            Assert.Equal("g", decoded.game);
            Assert.Equal(new[] { "p1", "p2" }, decoded.players);
            Assert.Equal("hi", decoded.GetPayload<StringMessage>().value);
        }

        [Fact]
        public void PlayerTargetCountIsInt32BothWays()
        {
            // It used to be written as int32 and read as int16, so no frame with players decoded.
            var frame = WildCatMessageBase.CreateWithPlayerTargets(new EmptyMessage(), UserMessageType,
                WildCatMsgHeader.normal, new[] { "a", "b", "c" }, "t");
            byte[] bytes = WireCodec.Encode(frame);

            // type (2) + flags (1) + ticket (1 + 1), then the count.
            Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5)));
            Assert.Equal(new[] { "a", "b", "c" }, WireCodec.Decode(bytes).players);
        }

        [Fact]
        public void GetFriendsRequestWithKeysDecodes()
        {
            // Deserialize used to write into an empty array, so any key threw.
            var request = new GetFriendsRequestMessage { publicKeys = new[] { "level", "avatar" } };
            var copy = WireCodec.DecodePayload<GetFriendsRequestMessage>(WireCodec.EncodePayload(request));
            Assert.Equal(new[] { "level", "avatar" }, copy.publicKeys);
        }

        [Fact]
        public void MissingPublicCloudValueEncodes()
        {
            // A missing value was null and the write threw, so no answer went out.
            var response = OptionalResponse<GetPublicCloudDataResponse>.CreateSuccessful(
                new GetPublicCloudDataResponse { hasValue = false, value = null });
            var copy = WireCodec.DecodePayload<OptionalResponse<GetPublicCloudDataResponse>>(WireCodec.EncodePayload(response));
            Assert.True(copy.isSuccesful);
            Assert.False(copy.msg.hasValue);
            Assert.Equal("", copy.msg.value);

            var other = new GetPlayerPublicCloudDataResponse { hasValue = false, value = null };
            var otherBytes = WireCodec.EncodePayload(other);
            Assert.Equal(new byte[] { 0 }, otherBytes);
            Assert.Equal("", WireCodec.DecodePayload<GetPlayerPublicCloudDataResponse>(otherBytes).value);
        }

        [Fact]
        public void FindMatchResponseConstructorKeepsTheMatchId()
        {
            var matchId = Guid.NewGuid();
            var response = new FindMatchResponse(matchId, "", new[] { "p1", "p2" });
            Assert.Equal(matchId, response.gameId);
            Assert.Equal(matchId, WireCodec.DecodePayload<FindMatchResponse>(WireCodec.EncodePayload(response)).gameId);
        }

        [Fact]
        public void MatchSyncKeepsEmptyPlayerBags()
        {
            // Empty bags used to be skipped while the count still included them.
            var sync = new MatchSyncEventData { Game = Guid.NewGuid() };
            sync.gameProperties["score"] = new byte[] { 3 };
            sync.playerProperties["quiet"] = new Dictionary<string, byte[]>();
            sync.playerProperties["loud"] = new Dictionary<string, byte[]> { ["hat"] = new byte[] { 1, 2 } };
            sync.playerProperties["null"] = null;

            byte[] bytes = WireCodec.EncodePayload(sync);
            var copy = WireCodec.DecodePayload<MatchSyncEventData>(bytes, out int consumed);

            Assert.Equal(bytes.Length, consumed);
            Assert.Equal(3, copy.playerProperties.Count);
            Assert.Empty(copy.playerProperties["quiet"]);
            Assert.Empty(copy.playerProperties["null"]);
            Assert.Equal(new byte[] { 1, 2 }, copy.playerProperties["loud"]["hat"]);
            Assert.Equal(new byte[] { 3 }, copy.gameProperties["score"]);
        }

        [Fact]
        public void ResponsesCarryOnlyTheRequestId()
        {
            var request = WildCatMessageBase.CreateWithPlayerTargets(new EmptyMessage(), WildCatMsgType.GetNameRequest,
                WildCatMsgHeader.HasGameTarget | WildCatMsgHeader.buffered, new[] { "p" }, "ticket");
            request.game = "g";
            request.SetRequestID(9);

            var response = WildCatMessageBase.CreateResponse(new StringMessage { value = "n" }, WildCatMsgType.GetNameResponse, request);
            Assert.Equal(WildCatMsgHeader.HasRequestID, response.header);
            Assert.Equal(9u, response.requestID);
            var decoded = WireCodec.Decode(WireCodec.Encode(response));
            Assert.Equal(WildCatMsgHeader.HasRequestID, decoded.header);
            Assert.Equal("", decoded.ticket);
            Assert.Null(decoded.players);

            var noId = WildCatMessageBase.CreateWithAuthData(new EmptyMessage(), WildCatMsgType.GetNameRequest, WildCatMsgHeader.normal, "t");
            Assert.Equal(WildCatMsgHeader.normal, WildCatMessageBase.CreateResponse(new EmptyMessage(), WildCatMsgType.GetNameResponse, noId).header);
        }

        public static TheoryData<string, string> CulturePairs => new TheoryData<string, string>
        {
            { "fa-IR", "en-US" },
            { "en-US", "fa-IR" },
            { "th-TH", "de-DE" },
            { "ar-SA", "ja-JP" },
            { "", "fa-IR" },
        };

        [Theory]
        [MemberData(nameof(CulturePairs))]
        public void DateTimesDoNotDependOnTheCulture(string writerCulture, string readerCulture)
        {
            // DateTime used to go out as ToString() and come back through DateTime.Parse, so a
            // Persian (or Thai, or Arabic) calendar on either side broke it.
            var entry = new LeaderBoardEntry { playerId = "p", userName = "u", score = 1, entryTime = ProtocolSampleData.SampleTime };
            byte[] bytes = WithCulture(writerCulture, () => WireCodec.EncodePayload(entry));
            byte[] invariantBytes = WithCulture("", () => WireCodec.EncodePayload(entry));
            var copy = WithCulture(readerCulture, () => WireCodec.DecodePayload<LeaderBoardEntry>(bytes));

            Assert.Equal(invariantBytes, bytes);
            Assert.Equal(ProtocolSampleData.SampleTime.Ticks, copy.entryTime.Ticks);
            Assert.Equal(DateTimeKind.Utc, copy.entryTime.Kind);
        }

        [Fact]
        public void DateTimeIsTheUtcTickCount()
        {
            var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            byte[] bytes = WireCodec.EncodePayload(new DateTimeMessage { value = utc });
            Assert.Equal(utc.Ticks, BinaryPrimitives.ReadInt64LittleEndian(bytes));

            // A local time is converted; an unspecified one is taken as UTC.
            var local = utc.ToLocalTime();
            Assert.Equal(bytes, WireCodec.EncodePayload(new DateTimeMessage { value = local }));
            var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
            Assert.Equal(bytes, WireCodec.EncodePayload(new DateTimeMessage { value = unspecified }));

            var copy = WireCodec.DecodePayload<DateTimeMessage>(bytes);
            Assert.Equal(utc, copy.value);
            Assert.Equal(DateTimeKind.Utc, copy.value.Kind);
        }

        [Fact]
        public void PendingMatchesAndInvitationsKeepTheirTimes()
        {
            var matches = new PendingMatchesCollectionData
            {
                matches = new[] { new PendingMatchesCollectionData.PendingMatchInfo { matchId = Guid.NewGuid(), startTime = ProtocolSampleData.SampleTime, players = new[] { "a" } } }
            };
            var invitation = new FriendInvitation { OtherPlayerId = "o", creationTime = ProtocolSampleData.SampleTime };

            var matchesCopy = WithCulture("fa-IR", () => WireCodec.DecodePayload<PendingMatchesCollectionData>(WireCodec.EncodePayload(matches)));
            var invitationCopy = WithCulture("th-TH", () => WireCodec.DecodePayload<FriendInvitation>(WireCodec.EncodePayload(invitation)));

            Assert.Equal(ProtocolSampleData.SampleTime, matchesCopy.matches[0].startTime);
            Assert.Equal(ProtocolSampleData.SampleTime, invitationCopy.creationTime);
        }

        [Fact]
        public void NullStringsAndCollectionsAreWrittenAsEmpty()
        {
            var cases = new MessageBase[]
            {
                new StringMessage { value = null },
                new FindMatchResponse { data = null, players = null },
                new InviteFriendRequest { value = null, data = null },
                new AuthenticationRequest { ticket = null, Id = null, PasswordHash = null, data = null },
                new CreateAccountRequest { ticket = null, Id = null, data = null },
                new AddAuthenticationToAccountRequest { Id = null, data = null },
                new FriendlistEntry { userId = null, data = null },
                new FriendlistCollection { friends = null, sentInvitation = null, receivedInvitations = null },
                new ChallengeMessage { Player = null, PlayerName = null, data = null },
                new ChallengeRequestsMessage { sent = null, received = new List<ChallengeRequestsMessage.Request> { null } },
                new GetInventoryResponseMessage { items = null },
                new RemoveItemInventoryRequestMessage { inventoryName = null, items = new (string, int)[] { (null, 1) } },
                new SetMatchPropertyData { key = null, value = null },
                new MatchPropertyChangeData { Player = null, key = null, value = null },
                new GetLeaderBoardResponse { entries = new List<LeaderBoardEntry> { null } },
                new PendingMatchesCollectionData { matches = null },
                new ScriptEventExecutionRequestMessage { eventName = null, parameterTypess = null, parameters = null },
                OptionalResponse<StringMessage>.CreateFailed(null, 1),
                OptionalResponse<StringMessage>.CreateSuccessful(null),
            };
            foreach (var message in cases)
            {
                byte[] bytes = WireCodec.EncodePayload(message);
                var copy = ProtocolSampleData.Decode(message.GetType(), bytes, out int consumed);
                Assert.Equal(bytes.Length, consumed);
                Assert.Equal(bytes, WireCodec.EncodePayload(copy));
            }

            Assert.Equal("", WireCodec.DecodePayload<StringMessage>(WireCodec.EncodePayload(new StringMessage { value = null })).value);
            Assert.Empty(WireCodec.DecodePayload<FindMatchResponse>(WireCodec.EncodePayload(new FindMatchResponse { players = null })).players);
            Assert.Equal("", WireCodec.DecodePayload<OptionalResponse<StringMessage>>(
                WireCodec.EncodePayload(OptionalResponse<StringMessage>.CreateFailed(null, 1))).errorMsg);
        }

        [Fact]
        public void StringMapsOfAnySizeRoundTrip()
        {
            // The count used to be written as one byte, so a map of 256 entries or more broke.
            var request = new AuthenticationRequest { Id = "d" };
            for (int i = 0; i < 300; ++i)
                request.data[$"k{i}"] = $"v{i}";
            var copy = WireCodec.DecodePayload<AuthenticationRequest>(WireCodec.EncodePayload(request));
            Assert.Equal(300, copy.data.Count);
            Assert.Equal("v299", copy.data["k299"]);
        }

        [Fact]
        public void ScriptArgumentsAndTypesMustMatchToEncode()
        {
            var request = new ScriptEventExecutionRequestMessage("e", 2);
            request.SetParameter(0, 1);
            Assert.Throws<ArgumentException>(() => request.SetParameter(2, 1));
            request.parameters = new object[] { 1, 2, 3 };
            Assert.Throws<InvalidOperationException>(() => WireCodec.EncodePayload(request));
        }

        private static T WithCulture<T>(string culture, Func<T> action)
        {
            var previous = CultureInfo.CurrentCulture;
            var previousUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                return action();
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.CurrentUICulture = previousUi;
            }
        }
    }
}
