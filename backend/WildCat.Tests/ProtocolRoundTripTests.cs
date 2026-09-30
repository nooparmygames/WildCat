using System.Globalization;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// Every payload type survives encode and decode field by field, reads exactly the bytes it
    /// wrote, and encodes to the same bytes again. The types are found by reflection, so a new
    /// payload is covered without touching this file (unless it needs sample data the generic
    /// filler cannot make).
    /// </summary>
    public class ProtocolRoundTripTests
    {
        public static TheoryData<string> PayloadTypeNames()
        {
            var data = new TheoryData<string>();
            foreach (var type in ProtocolSampleData.PayloadTypes())
                data.Add(type.FullName);
            return data;
        }

        private static Type PayloadType(string fullName)
        {
            return typeof(MessageBase).Assembly.GetType(fullName, throwOnError: true);
        }

        [Fact]
        public void TheProtocolHasTheExpectedNumberOfPayloadTypes()
        {
            // A guard against the reflection finding nothing; raise it when payloads are added.
            Assert.True(ProtocolSampleData.PayloadTypes().Count() >= 45);
        }

        [Theory]
        [MemberData(nameof(PayloadTypeNames))]
        public void PayloadRoundTripsFieldByField(string typeName)
        {
            Type type = PayloadType(typeName);
            MessageBase original = ProtocolSampleData.CreatePayload(type);

            byte[] bytes = WireCodec.EncodePayload(original);
            MessageBase copy = ProtocolSampleData.Decode(type, bytes, out int consumed);

            Assert.Equal(bytes.Length, consumed);
            ProtocolSampleData.AssertDeepEqual(original, copy, type.Name);
            Assert.Equal(bytes, WireCodec.EncodePayload(copy));
        }

        [Theory]
        [MemberData(nameof(PayloadTypeNames))]
        public void PayloadRoundTripsInsideASuccessfulOptionalResponseAndAFrame(string typeName)
        {
            Type type = PayloadType(typeName);
            if (type.GetConstructor(Type.EmptyTypes) == null)
                return;
            MessageBase payload = ProtocolSampleData.CreatePayload(type);
            Type responseType = typeof(OptionalResponse<>).MakeGenericType(type);
            var response = (MessageBase)Activator.CreateInstance(responseType);
            responseType.GetMethod(nameof(OptionalResponse<EmptyMessage>.Succeed)).Invoke(response, new object[] { payload });

            var frame = WildCatMessageBase.CreateWithAuthDataAndRequestID(response, WildCatMsgType.GetFriendsResponse,
                WildCatMsgHeader.normal, "ticket", 77);
            var decodedFrame = WireCodec.Decode(WireCodec.Encode(frame));
            MessageBase decoded = ProtocolSampleData.Decode(responseType, decodedFrame.RawData, out int consumed);

            Assert.Equal(decodedFrame.RawData.Length, consumed);
            Assert.True((bool)responseType.GetField("isSuccesful").GetValue(decoded));
            ProtocolSampleData.AssertDeepEqual(payload, responseType.GetField("msg").GetValue(decoded), type.Name);
        }

        [Theory]
        [MemberData(nameof(PayloadTypeNames))]
        public void AFailedResponseReadsTheSameAsAnyType(string typeName)
        {
            Type type = PayloadType(typeName);
            if (type.GetConstructor(Type.EmptyTypes) == null)
                return;
            Type responseType = typeof(OptionalResponse<>).MakeGenericType(type);
            var failed = (MessageBase)Activator.CreateInstance(responseType);
            responseType.GetMethod(nameof(OptionalResponse<EmptyMessage>.Fail)).Invoke(failed, new object[] { "it failed", 7 });

            byte[] bytes = WireCodec.EncodePayload(failed);
            var asEmpty = WireCodec.DecodePayload<OptionalResponse<EmptyMessage>>(bytes, out int consumed);

            Assert.Equal(bytes.Length, consumed);
            Assert.False(asEmpty.isSuccesful);
            Assert.Equal(7, asEmpty.errorCode);
            Assert.Equal("it failed", asEmpty.errorMsg);
            Assert.Equal(bytes, WireCodec.EncodePayload(OptionalResponse<EmptyMessage>.CreateFailed("it failed", 7)));
        }

        [Theory]
        [InlineData(ParamType.BoolParam)]
        [InlineData(ParamType.ByteParam)]
        [InlineData(ParamType.ShortParam)]
        [InlineData(ParamType.IntParam)]
        [InlineData(ParamType.LongParam)]
        [InlineData(ParamType.FloatParam)]
        [InlineData(ParamType.DoubleParam)]
        [InlineData(ParamType.StringParam)]
        [InlineData(ParamType.ByteArrayParam)]
        public void EveryScriptValueTypeRoundTripsAsAReturnValue(ParamType type)
        {
            object value = type switch
            {
                ParamType.BoolParam => true,
                ParamType.ByteParam => (byte)250,
                ParamType.ShortParam => (short)-300,
                ParamType.IntParam => int.MinValue,
                ParamType.LongParam => long.MaxValue,
                ParamType.FloatParam => float.Epsilon,
                ParamType.DoubleParam => Math.PI,
                ParamType.StringParam => "r\u00E9sultat",
                _ => new byte[] { 9, 8, 7 },
            };
            var response = new ScriptEventExecutionResponseMessage { hasReturnValue = true, returnType = type, returnValue = value };

            var copy = WireCodec.DecodePayload<ScriptEventExecutionResponseMessage>(WireCodec.EncodePayload(response));

            Assert.True(copy.hasReturnValue);
            Assert.Equal(type, copy.returnType);
            Assert.Equal(value, copy.returnValue);
        }

        [Fact]
        public void AScriptResponseWithoutAValueIsOneByte()
        {
            var bytes = WireCodec.EncodePayload(new ScriptEventExecutionResponseMessage { hasReturnValue = false });
            Assert.Equal(new byte[] { 0 }, bytes);
            Assert.False(WireCodec.DecodePayload<ScriptEventExecutionResponseMessage>(bytes).hasReturnValue);
        }

        [Fact]
        public void ScriptResponseGetValueChecksTheType()
        {
            var response = new ScriptEventExecutionResponseMessage { hasReturnValue = true, returnType = ParamType.IntParam, returnValue = 5 };
            Assert.Equal(5, response.GetValue<int>());
            Assert.Throws<ArgumentException>(() => response.GetValue<long>());
            Assert.Throws<InvalidOperationException>(() => new ScriptEventExecutionResponseMessage().GetValue<int>());
        }

        [Fact]
        public void EmptyMessageIsOneTrueByte()
        {
            Assert.Equal(new byte[] { 1 }, WireCodec.EncodePayload(new EmptyMessage()));
        }

        [Fact]
        public void TimeSpansKeepTheirMilliseconds()
        {
            var message = new GetInventoryUnlockTimeResponseMessage { time = new TimeSpan(2, 3, 4, 5, 678) };
            Assert.Equal(message.time, WireCodec.DecodePayload<GetInventoryUnlockTimeResponseMessage>(WireCodec.EncodePayload(message)).time);
        }

        [Fact]
        public void EveryMessageTypeIdIsUniqueAndPinned()
        {
            var values = Enum.GetValues<WildCatMsgType>().Select(v => (short)v).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
            Assert.Equal(6, (short)WildCatMsgType.InitializeSessionRequest);
            Assert.Equal(84, (short)WildCatMsgType.RecoverPasswordRequest);
            Assert.Equal(86, (short)WildCatMsgType.ResendActivationRequest);
            Assert.Equal(162, (short)WildCatMsgType.GetPlayerChallengesResponse);
            Assert.Equal(163, (short)WildCatMsgType.ResetPasswordWithCodeRequest);
            Assert.Equal(164, (short)WildCatMsgType.ResetPasswordWithCodeResponse);
            Assert.Equal(1023, (short)WildCatMsgType.HighestInternal);
        }
    }
}
