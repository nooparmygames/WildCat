using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The dispatcher and the transport over real WebSockets: the error envelope, session
    /// requirements, concurrency, relay order, the outbound queue and the handler registry. The
    /// handlers used here are test handlers on ids no real message uses (1000 and up).
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayDispatcherTests
    {
        private const WildCatMsgType Throwing = (WildCatMsgType)1000;
        private const WildCatMsgType Failing = (WildCatMsgType)1001;
        private const WildCatMsgType Blocking = (WildCatMsgType)1002;
        private const WildCatMsgType SessionEcho = (WildCatMsgType)1003;
        private const WildCatMsgType UserMessageType = WildCatMsgType.HighestInternal + 9;

        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayDispatcherTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        /// <summary>Throws an exception nobody expects, with a detail the client must not see.</summary>
        private sealed class ThrowingHandler : RequestHandler<EmptyMessage, EmptyMessage>
        {
            public override WildCatMsgType RequestType => Throwing;
            public override WildCatMsgType ResponseType => Throwing + 1000;
            public override SessionRequirement Session => SessionRequirement.None;

            protected override Task<EmptyMessage> HandleAsync(MessageContext context, EmptyMessage request)
            {
                throw new InvalidOperationException("secret detail");
            }
        }

        private sealed class FailingHandler : RequestHandler<EmptyMessage, EmptyMessage>
        {
            public override WildCatMsgType RequestType => Failing;
            public override WildCatMsgType ResponseType => Failing + 1000;
            public override SessionRequirement Session => SessionRequirement.None;

            protected override Task<EmptyMessage> HandleAsync(MessageContext context, EmptyMessage request)
            {
                throw new RequestFailedException(7, "Nope");
            }
        }

        /// <summary>Waits for <see cref="Release"/>, counting how many run at once.</summary>
        private sealed class BlockingHandler : RequestHandler<EmptyMessage, IntegerMessage>
        {
            private readonly TaskCompletionSource gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private int running;
            private int maxRunning;

            public override WildCatMsgType RequestType => Blocking;
            public override WildCatMsgType ResponseType => Blocking + 1000;
            public override SessionRequirement Session => SessionRequirement.None;

            public int Running => Volatile.Read(ref running);
            public int MaxRunning => Volatile.Read(ref maxRunning);

            public void Release() => gate.TrySetResult();

            protected override async Task<IntegerMessage> HandleAsync(MessageContext context, EmptyMessage request)
            {
                int now = Interlocked.Increment(ref running);
                int max;
                while (now > (max = Volatile.Read(ref maxRunning)))
                    Interlocked.CompareExchange(ref maxRunning, now, max);
                try
                {
                    await gate.Task.WaitAsync(TestSetup.Timeout);
                    return new IntegerMessage { value = now };
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                }
            }
        }

        /// <summary>Needs only a session; answers with what the dispatcher found.</summary>
        private sealed class SessionEchoHandler : RequestHandler<StringMessage, StringMessage>
        {
            public override WildCatMsgType RequestType => SessionEcho;
            public override WildCatMsgType ResponseType => SessionEcho + 1000;
            public override SessionRequirement Session => SessionRequirement.Session;

            protected override Task<StringMessage> HandleAsync(MessageContext context, StringMessage request)
            {
                return Task.FromResult(new StringMessage { value = $"{context.Session.GameKey}|{context.Player?.GetPrimaryKeyString()}" });
            }
        }

        private Task<GatewayTestHost> StartWith(Action<GameGatewayOptions> configure, params IMessageHandler[] handlers)
        {
            return GatewayTestHost.StartAsync(cluster.Client, configure, configureServices: services =>
            {
                foreach (var handler in handlers)
                    services.AddSingleton(handler);
            });
        }

        [Fact]
        public async Task AnUnexpectedExceptionIsAnInternalErrorAndIsLoggedWithItsStackTrace()
        {
            await using var host = await StartWith(null, new ThrowingHandler());
            await using var client = await host.ConnectAsync();

            var response = await client.CallAsync<EmptyMessage>(Throwing, new EmptyMessage(), Throwing + 1000);

            Assert.False(response.isSuccesful);
            Assert.Equal(GatewayErrors.Internal, response.errorCode);
            Assert.Equal(GatewayErrors.InternalError, response.errorMsg);
            var logged = Assert.Single(host.Logs.Entries, e => e.Exception?.Message == "secret detail");
            Assert.Equal(LogLevel.Error, logged.Level);
            Assert.Equal(typeof(MessageDispatcher).FullName, logged.Category);
            Assert.Contains(nameof(ThrowingHandler), logged.Exception.StackTrace);
        }

        [Fact]
        public async Task ARequestFailedExceptionKeepsItsCodeAndMessage()
        {
            await using var host = await StartWith(null, new FailingHandler());
            await using var client = await host.ConnectAsync();

            var response = await client.CallAsync<EmptyMessage>(Failing, new EmptyMessage(), Failing + 1000);

            Assert.False(response.isSuccesful);
            Assert.Equal(7, response.errorCode);
            Assert.Equal("Nope", response.errorMsg);
            Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Error);
        }

        [Fact]
        public async Task AnUnreadablePayloadIsAnsweredOnTheResponseType()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var request = client.Request(WildCatMsgType.InitializeSessionRequest, new EmptyMessage());
            request.RawData = new byte[] { 0xFF };
            var reply = await client.RequestAsync(request);

            Assert.Equal(WildCatMsgType.InitializeSessionResponse, reply.msgType);
            var response = reply.GetPayload<OptionalResponse<EmptyMessage>>();
            Assert.Equal(GatewayErrors.Refused, response.errorCode);
            Assert.Equal(GatewayErrors.MalformedMessage, response.errorMsg);
        }

        [Fact]
        public async Task AnUnreadableFrameGetsAnErrorMsgAndTheConnectionGoesOn()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            await client.SendRawAsync(new byte[] { 6, 0, 16, 1 });
            var error = await client.WaitForTypeAsync(WildCatMsgType.ErrorMsg);

            Assert.Equal(WildCatMsgHeader.normal, error.header);
            Assert.Equal(GatewayErrors.MalformedMessage, error.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg);
            Assert.NotEqual(Guid.Empty, await client.InitializeSessionAsync(developerKey, gameKey));
        }

        [Fact]
        public async Task SessionRequirementAcceptsASessionWithoutALogin()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await StartWith(null, new SessionEchoHandler());
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            var anonymous = await client.CallAsync<StringMessage>(SessionEcho, new StringMessage(), SessionEcho + 1000, session.ToString());
            Assert.Equal($"{gameKey}|", anonymous.msg.value);

            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            var loggedIn = await client.CallAsync<StringMessage>(SessionEcho, new StringMessage(), SessionEcho + 1000, session.ToString());
            Assert.Equal($"{gameKey}|{login.userId}", loggedIn.msg.value);

            var noTicket = await client.CallAsync<EmptyMessage>(SessionEcho, new StringMessage(), WildCatMsgType.ErrorMsg);
            Assert.Equal(GatewayErrors.SessionRequired, noTicket.errorMsg);
        }

        [Fact]
        public async Task ASlowRequestDoesNotHoldUpHeartBeats()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var blocking = new BlockingHandler();
            await using var host = await StartWith(null, blocking);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));

            var slow = client.Request(Blocking, new EmptyMessage());
            await client.SendAsync(slow);
            await TestSetup.WaitUntilAsync(() => blocking.Running == 1, "the slow request to start");

            var beat = await client.CallAsync<EmptyMessage>(WildCatMsgType.HeartBeatRequest, new EmptyMessage(),
                WildCatMsgType.HeartBeatResponse, session.ToString());
            Assert.True(beat.isSuccesful, beat.errorMsg);
            Assert.Equal(1, blocking.Running);

            blocking.Release();
            var reply = await client.WaitForAsync(m => m.requestID == slow.requestID, "the slow reply");
            Assert.True(reply.GetPayload<OptionalResponse<IntegerMessage>>().isSuccesful);
        }

        [Fact]
        public async Task RequestsInFlightAreCappedPerConnection()
        {
            var blocking = new BlockingHandler();
            await using var host = await StartWith(o => o.MaxConcurrentRequests = 2, blocking);
            await using var client = await host.ConnectAsync();

            var requests = Enumerable.Range(0, 5).Select(_ => client.Request(Blocking, new EmptyMessage())).ToList();
            foreach (var request in requests)
                await client.SendAsync(request);
            await TestSetup.WaitUntilAsync(() => blocking.Running == 2, "two requests to run");
            await Task.Delay(300);
            Assert.Equal(2, blocking.MaxRunning);

            blocking.Release();
            foreach (var request in requests)
                await client.WaitForAsync(m => m.requestID == request.requestID, $"the reply to {request.requestID}");
            Assert.Equal(2, blocking.MaxRunning);
        }

        private async Task<(GatewayTestClient Alice, GatewayTestClient Bob, Guid AliceSession, Guid MatchId, string BobId)> StartMatch(GatewayTestHost host)
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            var alice = await host.ConnectAsync();
            var aliceSession = await alice.InitializeSessionAsync(developerKey, gameKey);
            var aliceId = (await alice.LoginWithDeviceAsync(aliceSession, TestSetup.UniqueId("device"))).userId;
            var bob = await host.ConnectAsync();
            var bobSession = await bob.InitializeSessionAsync(developerKey, gameKey);
            var bobId = (await bob.LoginWithDeviceAsync(bobSession, TestSetup.UniqueId("device"))).userId;

            var matchId = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(matchId, gameKey.ToString(), null);
            var alicePlayer = GrainFactory.GetGrain<IPlayer>(aliceId);
            var bobPlayer = GrainFactory.GetGrain<IPlayer>(bobId);
            await game.StartGame(new[] { alicePlayer, bobPlayer }, null);
            await game.PlayerStarted(alicePlayer);
            await game.PlayerStarted(bobPlayer);
            return (alice, bob, aliceSession, matchId, bobId);
        }

        [Fact]
        public async Task RelayedMessagesArriveInTheOrderTheyWereSent()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var (alice, bob, aliceSession, matchId, _) = await StartMatch(host);
            await using var aliceClient = alice;
            await using var bobClient = bob;

            for (int i = 0; i < 100; ++i)
            {
                await alice.SendAsync(WildCatMessageBase.CreateWithGameTarget(new IntegerMessage { value = i }, UserMessageType,
                    WildCatMsgHeader.normal, matchId.ToString(), aliceSession.ToString()));
            }
            for (int i = 0; i < 100; ++i)
            {
                var relayed = await bob.WaitForTypeAsync(UserMessageType);
                Assert.Equal(i, relayed.GetPayload<IntegerMessage>().value);
                Assert.False(relayed.hasAuthData);
                Assert.Equal(matchId.ToString(), relayed.game);
            }
        }

        [Fact]
        public async Task ASmallRelayByteBudgetSlowsTheSenderButLosesNothing()
        {
            // Room for about ten of these frames: the receive loop keeps waiting for the relay
            // worker, and every message still arrives, in order.
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, o => o.RelayQueueBytes = 2000);
            var (alice, bob, aliceSession, matchId, _) = await StartMatch(host);
            await using var aliceClient = alice;
            await using var bobClient = bob;

            for (int i = 0; i < 100; ++i)
            {
                await alice.SendAsync(WildCatMessageBase.CreateWithGameTarget(new RawMessage { value = BitConverter.GetBytes(i).Concat(new byte[100]).ToArray() },
                    UserMessageType, WildCatMsgHeader.normal, matchId.ToString(), aliceSession.ToString()));
            }
            for (int i = 0; i < 100; ++i)
            {
                var relayed = await bob.WaitForTypeAsync(UserMessageType);
                Assert.Equal(i, BitConverter.ToInt32(relayed.GetPayload<RawMessage>().value, 0));
            }
        }

        [Fact]
        public async Task AClientThatStopsReadingIsDisconnectedAndOthersCarryOn()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, o => o.OutboundQueueCapacity = 4);
            await using var alice = await host.ConnectAsync();
            var aliceSession = await alice.InitializeSessionAsync(developerKey, gameKey);
            await alice.LoginWithDeviceAsync(aliceSession, TestSetup.UniqueId("device"));

            // Bob logs in with a bare socket and then never reads again.
            using var bob = await host.ConnectRawAsync();
            var bobSession = await RawRequestAsync<GuidMessage>(bob, WildCatMessageBase.CreateWithRequestID(
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey },
                WildCatMsgType.InitializeSessionRequest, WildCatMsgHeader.normal, 1));
            var bobLogin = await RawRequestAsync<AuthenticationResponse>(bob, WildCatMessageBase.CreateWithRequestID(
                new AuthenticationRequest { ticket = bobSession.value.ToString(), LoginType = LoginType.Device, Id = TestSetup.UniqueId("device"), AutoCreate = true },
                WildCatMsgType.AuthenticateRequest, WildCatMsgHeader.normal, 2));
            string bobId = bobLogin.userId;
            var bobConnection = host.Connections.GetByPlayer(bobId);
            Assert.NotNull(bobConnection);

            // Push until the socket buffers and the queue are full. Sending never waits.
            byte[] frame = WireCodec.Encode(WildCatMessageBase.Create(new RawMessage { value = new byte[256 * 1024] },
                UserMessageType, WildCatMsgHeader.normal));
            int sent = 0;
            while (bobConnection.TrySend(frame))
            {
                Assert.True(++sent < 10_000, "The queue never filled up.");
            }

            await TestSetup.WaitUntilAsync(() => host.Connections.GetByPlayer(bobId) == null, "the slow connection to be dropped");
            await TestSetup.WaitUntilAsync(async () => !await GrainFactory.GetGrain<IPlayer>(bobId).IsLoggedIn(), "Bob to be logged out");
            Assert.Contains(host.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is not reading its messages"));
            Assert.False(bobConnection.TrySend(frame));

            var name = await alice.GetNameAsync(aliceSession.ToString());
            Assert.True(name.GetPayload<OptionalResponse<StringMessage>>().isSuccesful);
        }

        /// <summary>Sends a request on a bare socket and reads frames until its answer arrives.</summary>
        private static async Task<T> RawRequestAsync<T>(ClientWebSocket socket, WildCatMessageBase request) where T : MessageBase, new()
        {
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await socket.SendAsync(WireCodec.Encode(request), WebSocketMessageType.Binary, true, timeout.Token);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                using var frame = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, timeout.Token);
                    frame.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                var message = WireCodec.Decode(frame.ToArray());
                if (message.hasRequestID && message.requestID == request.requestID)
                {
                    var response = message.GetPayload<OptionalResponse<T>>();
                    Assert.True(response.isSuccesful, response.errorMsg);
                    return response.msg;
                }
            }
        }

        [Fact]
        public async Task PushesReachTheClientThroughTheOutboundQueue()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));

            var player = GrainFactory.GetGrain<IPlayer>(login.userId);
            var pushed = WildCatMessageBase.Create(new StringMessage { value = "from a grain" }, UserMessageType, WildCatMsgHeader.normal);
            await player.SendDataToConnection(player, 0, WireCodec.Encode(pushed));

            var received = await client.WaitForTypeAsync(UserMessageType);
            Assert.Equal("from a grain", received.GetPayload<StringMessage>().value);
        }

        [Fact]
        public void TheRegistryRefusesTwoHandlersForOneTypeAndGameMessageIds()
        {
            var twice = Assert.Throws<InvalidOperationException>(() => new HandlerRegistry(new IMessageHandler[] { new FailingHandler(), new FailingHandler() }));
            Assert.Contains("two handlers", twice.Message);

            var gameId = new Mock(UserMessageType);
            Assert.Throws<InvalidOperationException>(() => new HandlerRegistry(new IMessageHandler[] { gameId }));
        }

        private sealed class Mock : IMessageHandler
        {
            private readonly WildCatMsgType type;

            public Mock(WildCatMsgType type)
            {
                this.type = type;
            }

            public IEnumerable<MessageRoute> Routes => new[] { new MessageRoute(type, type, SessionRequirement.None) };

            public Task HandleAsync(MessageContext context) => Task.CompletedTask;
        }

        /// <summary>
        /// Request ids that are defined but have no handler on purpose; docs/wire-protocol.md lists
        /// them under "Defined but unhandled". Anything else named ...Request needs a route.
        /// </summary>
        private static readonly WildCatMsgType[] DocumentedUnhandled =
        {
            WildCatMsgType.CreateEnvironmentRequest, WildCatMsgType.GetDefinedItemsRequest,
            WildCatMsgType.BuyItemRequest, WildCatMsgType.ConsumeItemRequest,
        };

        [Fact]
        public async Task EveryClientRequestIdHasARoute()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var requests = Enum.GetValues<WildCatMsgType>()
                .Where(t => t.ToString().EndsWith("Request", StringComparison.Ordinal) || t.ToString().EndsWith("Request_S", StringComparison.Ordinal))
                .ToList();
            Assert.Contains(WildCatMsgType.HeartBeatRequest, requests);

            foreach (var type in requests.Except(DocumentedUnhandled))
                Assert.True(host.Handlers.TryGet(type, out _, out _), $"{type} ({(int)type}) has no handler and is not documented as unhandled");
            foreach (var type in DocumentedUnhandled)
                Assert.False(host.Handlers.TryGet(type, out _, out _), $"{type} has a handler now; take it off the unhandled list");

            // The unhandled ones are documented as such.
            string protocolDoc = File.ReadAllText(Path.Combine(ProtocolSyncTests.FindRepoRoot(), "docs", "wire-protocol.md"));
            string unhandled = protocolDoc.Substring(protocolDoc.IndexOf("Defined but unhandled", StringComparison.Ordinal));
            unhandled = unhandled.Substring(0, unhandled.IndexOf("\n\n", StringComparison.Ordinal));
            foreach (var type in DocumentedUnhandled)
                Assert.Contains(((int)type).ToString(), unhandled);

            // And every request id the SDK core sends is served.
            string sdk = Path.Combine(ProtocolSyncTests.FindRepoRoot(), "unity-sdk", "Packages", "com.noopsoftworks.wildcat", "Runtime");
            var sent = Directory.EnumerateFiles(sdk, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Runtime/Protocol/", StringComparison.Ordinal))
                .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"WildCatMsgType\.(\w+Request)\b").Select(m => m.Groups[1].Value))
                .Distinct()
                .ToList();
            Assert.Contains("AuthenticateRequest", sent);
            foreach (var name in sent)
            {
                var type = Enum.Parse<WildCatMsgType>(name);
                Assert.True(host.Handlers.TryGet(type, out _, out _), $"The SDK sends {name}, which has no handler");
            }
        }

        [Fact]
        public async Task SessionAndSignInTypesHaveHandlersAndOnlySignInsCountAgainstTheLimit()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var registry = host.Handlers;
            var ported = new[]
            {
                WildCatMsgType.InitializeSessionRequest, WildCatMsgType.HeartBeatRequest, WildCatMsgType.GetServerTimeRequest,
                WildCatMsgType.AuthenticateRequest, WildCatMsgType.CreateNewAccountRequest, WildCatMsgType.EmailActivationRequest,
                WildCatMsgType.AddAuthMechanismToAccountRequest, WildCatMsgType.RecoverPasswordRequest,
                WildCatMsgType.ResendActivationRequest, WildCatMsgType.ResetPasswordWithCodeRequest,
            };
            foreach (var type in ported)
            {
                Assert.True(registry.TryGet(type, out var route, out var handler), $"{type} has no handler");
                Assert.NotNull(handler);
                Assert.Equal(type + 1, route.ResponseType);
            }

            var signIn = registry.Routes.Where(r => r.IsSignInAttempt).Select(r => r.RequestType).OrderBy(t => t).ToList();
            Assert.Equal(new[]
            {
                WildCatMsgType.AuthenticateRequest, WildCatMsgType.CreateNewAccountRequest, WildCatMsgType.EmailActivationRequest,
                WildCatMsgType.AddAuthMechanismToAccountRequest, WildCatMsgType.RecoverPasswordRequest,
                WildCatMsgType.ResendActivationRequest, WildCatMsgType.ResetPasswordWithCodeRequest,
            }, signIn);

            Assert.All(registry.Routes, r => Assert.True(r.RequestType <= WildCatMsgType.HighestInternal));
            Assert.All(GatewayExtensions.HandlerTypes(), t => Assert.True(typeof(IMessageHandler).IsAssignableFrom(t)));
        }
    }
}
