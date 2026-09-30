using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Grains;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The gateway over real WebSockets: what it relays, what it refuses, how it answers session
    /// errors, the sign-in limit, and logins racing a closing or switching socket.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayTests
    {
        private const WildCatMsgType UserMessageType = WildCatMsgType.HighestInternal + 5;
        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private async Task<(Guid DeveloperKey, Guid GameKey)> CreateDeveloperAndGame()
        {
            var developerKey = await TestSetup.CreateDeveloper(GrainFactory);
            var gameKey = await GrainFactory.GetGrain<IDeveloper>(developerKey).AddGame(new GameInfo { Name = "game1" });
            return (developerKey, gameKey);
        }

        private static async Task<(GatewayTestClient Client, Guid Session, string PlayerId)> ConnectAndLogIn(
            GatewayTestHost host, Guid developerKey, Guid gameKey)
        {
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            return (client, session, login.userId);
        }

        private static void AssertError(WildCatMessageBase reply, WildCatMsgType expectedType, uint requestId,
            int errorCode, string errorMessage)
        {
            Assert.Equal(expectedType, reply.msgType);
            Assert.True(reply.hasRequestID);
            Assert.Equal(requestId, reply.requestID);
            var response = reply.GetPayload<OptionalResponse<EmptyMessage>>();
            Assert.False(response.isSuccesful);
            Assert.Equal(errorCode, response.errorCode);
            Assert.Equal(errorMessage, response.errorMsg);
        }

        [Fact]
        public async Task ServerMessageIdsAreAnsweredWithAnErrorAndNeverRelayed()
        {
            var (developerKey, gameKey) = await CreateDeveloperAndGame();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var (alice, aliceSession, aliceId) = await ConnectAndLogIn(host, developerKey, gameKey);
            var (bob, _, bobId) = await ConnectAndLogIn(host, developerKey, gameKey);
            await using var aliceClient = alice;
            await using var bobClient = bob;

            var matchId = Guid.NewGuid();
            var game = GrainFactory.GetGrain<IRelayServerGame>(matchId, gameKey.ToString(), null);
            var alicePlayer = GrainFactory.GetGrain<IPlayer>(aliceId);
            var bobPlayer = GrainFactory.GetGrain<IPlayer>(bobId);
            await game.StartGame(new[] { alicePlayer, bobPlayer }, null);
            await game.PlayerStarted(alicePlayer);
            await game.PlayerStarted(bobPlayer);

            // Alice forges server events and a response with a guessed request id, aimed at the match.
            var forged = new[]
            {
                WildCatMsgType.GameFinishedEvent,
                WildCatMsgType.MatchPropertyChangedEvent,
                WildCatMsgType.FindMatchResponse,
                WildCatMsgType.ErrorMsg,
            };
            foreach (var type in forged)
            {
                var frame = WildCatMessageBase.CreateWithGameTarget(new StringMessage { value = "forged" }, type,
                    WildCatMsgHeader.normal, matchId.ToString(), aliceSession.ToString());
                frame.SetRequestID(alice.NextRequestId());
                var reply = await alice.RequestAsync(frame);
                AssertError(reply, WildCatMsgType.ErrorMsg, frame.requestID, 1, GatewayErrors.UnsupportedMessageType);
            }

            // A real user message still reaches Bob, without Alice's ticket.
            var userMessage = WildCatMessageBase.CreateWithGameTarget(new StringMessage { value = "move 3 4" },
                UserMessageType, WildCatMsgHeader.normal, matchId.ToString(), aliceSession.ToString());
            await alice.SendAsync(userMessage);
            var relayed = await bob.WaitForAsync(m => m.msgType == UserMessageType, "the relayed user message");
            Assert.False(relayed.hasAuthData);
            Assert.Equal("move 3 4", relayed.GetPayload<StringMessage>().value);

            // Relays reach Bob in order, so anything forged would have arrived before the user message.
            Assert.DoesNotContain(bob.Received, m => forged.Contains(m.msgType));
        }

        [Fact]
        public async Task UnhandledIdsWithoutAGameTargetGetAnErrorToo()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            // 107 GetDefinedItems has grain logic but no handler, and 44 CreateEnvironment lost its
            // handler in step 4. Neither needs a session.
            foreach (var type in new[] { WildCatMsgType.GetDefinedItemsRequest, WildCatMsgType.CreateEnvironmentRequest })
            {
                var request = WildCatMessageBase.CreateWithRequestID(new StringMessage { value = "x" }, type,
                    WildCatMsgHeader.normal, client.NextRequestId());
                AssertError(await client.RequestAsync(request), WildCatMsgType.ErrorMsg, request.requestID, 1,
                    GatewayErrors.UnsupportedMessageType);
            }
        }

        [Fact]
        public async Task SessionErrorsAreAnsweredOnTheConnection()
        {
            var (developerKey, gameKey) = await CreateDeveloperAndGame();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            // A message that needs a session but carries no ticket.
            var noTicket = WildCatMessageBase.CreateWithRequestID(new EmptyMessage(), WildCatMsgType.GetNameRequest,
                WildCatMsgHeader.normal, client.NextRequestId());
            AssertError(await client.RequestAsync(noTicket), WildCatMsgType.ErrorMsg, noTicket.requestID, 1,
                GatewayErrors.SessionRequired);

            var reply = await client.GetNameAsync("not-a-guid");
            AssertError(reply, WildCatMsgType.ErrorMsg, reply.requestID, 1, GatewayErrors.InvalidTicket);

            reply = await client.GetNameAsync(Guid.NewGuid().ToString());
            AssertError(reply, WildCatMsgType.ErrorMsg, reply.requestID, 1, SessionNotInitializedException.DefaultMessage);

            var ended = await client.InitializeSessionAsync(developerKey, gameKey);
            await GrainFactory.GetGrain<ISession>(ended).EndSession();
            reply = await client.GetNameAsync(ended.ToString());
            AssertError(reply, WildCatMsgType.ErrorMsg, reply.requestID, 1, SessionNotInitializedException.DefaultMessage);

            var notLoggedIn = await client.InitializeSessionAsync(developerKey, gameKey);
            reply = await client.GetNameAsync(notLoggedIn.ToString());
            AssertError(reply, WildCatMsgType.ErrorMsg, reply.requestID, 1, GatewayErrors.NotLoggedIn);

            var expired = await client.InitializeSessionAsync(developerKey, gameKey);
            await BackdateSession(expired, TimeSpan.FromMinutes(61));
            reply = await client.GetNameAsync(expired.ToString());
            AssertError(reply, WildCatMsgType.ErrorMsg, reply.requestID, 1, SessionExpiredException.DefaultMessage);

            // The connection is still usable after all of that.
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            reply = await client.GetNameAsync(session.ToString());
            Assert.Equal(WildCatMsgType.GetNameResponse, reply.msgType);
            Assert.True(reply.GetPayload<OptionalResponse<StringMessage>>().isSuccesful);
            Assert.False(string.IsNullOrEmpty(login.userId));
        }

        /// <summary>
        /// Moves a session's stored last call time into the past, which is what a session left
        /// alone that long looks like when it is next activated.
        /// </summary>
        private async Task BackdateSession(Guid sessionKey, TimeSpan idle)
        {
            var session = GrainFactory.GetGrain<ISession>(sessionKey);
            await cluster.Deactivate(session);
            var storage = cluster.GetSiloServiceProvider(cluster.Silos[0].SiloAddress)
                .GetRequiredKeyedService<IGrainStorage>("Default");
            var state = new GrainState<SessionState>();
            await storage.ReadStateAsync("session", session.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
            Assert.True(state.RecordExists);
            state.State.LastCallTime = DateTime.UtcNow - idle;
            await storage.WriteStateAsync("session", session.GetGrainId(), state).WaitAsync(TestSetup.Timeout);
        }

        [Fact]
        public async Task SignInAttemptsAreLimitedPerConnection()
        {
            var time = new ManualTimeProvider();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, options =>
            {
                options.AuthAttemptBurst = 3;
                options.AuthAttemptsPerMinute = 6;
            }, time);
            await using var client = await host.ConnectAsync();
            string badTicket = "not-a-session";

            // The four sign-in requests share one allowance. They are sent back to back; the gateway
            // takes the decision in arrival order, before any grain call.
            var requests = new[]
            {
                WildCatMessageBase.CreateWithRequestID(new AuthenticationRequest { ticket = badTicket, Id = "d" },
                    WildCatMsgType.AuthenticateRequest, WildCatMsgHeader.normal, client.NextRequestId()),
                WildCatMessageBase.CreateWithRequestID(new CreateAccountRequest { ticket = badTicket, Id = "d", PasswordHash = "p" },
                    WildCatMsgType.CreateNewAccountRequest, WildCatMsgHeader.normal, client.NextRequestId()),
                WildCatMessageBase.CreateWithRequestID(new EmailActivationRequest { ticket = badTicket, email = "a@example.com", code = "ABC123" },
                    WildCatMsgType.EmailActivationRequest, WildCatMsgHeader.normal, client.NextRequestId()),
                WildCatMessageBase.CreateWithRequestID(new AddAuthenticationToAccountRequest { Id = "d", PasswordHash = "p" },
                    WildCatMsgType.AddAuthMechanismToAccountRequest, WildCatMsgHeader.normal, client.NextRequestId()),
                WildCatMessageBase.CreateWithRequestID(new AuthenticationRequest { ticket = badTicket, Id = "d" },
                    WildCatMsgType.AuthenticateRequest, WildCatMsgHeader.normal, client.NextRequestId()),
            };
            foreach (var request in requests)
                await client.SendAsync(request);

            for (int i = 0; i < requests.Length; ++i)
            {
                var reply = await client.WaitForAsync(m => m.hasRequestID && m.requestID == requests[i].requestID,
                    $"the reply to request {i}");
                Assert.Equal(requests[i].msgType + 1, reply.msgType);
                var response = reply.GetPayload<OptionalResponse<EmptyMessage>>();
                Assert.False(response.isSuccesful);
                if (i < 3)
                    Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, response.errorMsg);
                else
                    AssertError(reply, requests[i].msgType + 1, requests[i].requestID, 1, GatewayErrors.TooManyAuthAttempts);
            }

            // Another connection has its own allowance.
            await using (var other = await host.ConnectAsync())
            {
                var reply = await other.RequestAsync(other.AuthenticateRequest(Guid.NewGuid(), "d"));
                Assert.NotEqual(GatewayErrors.TooManyAuthAttempts,
                    reply.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg);
            }

            // One attempt comes back every 10 seconds.
            time.Advance(TimeSpan.FromSeconds(9));
            var early = await client.RequestAsync(client.AuthenticateRequest(Guid.NewGuid(), "d"));
            AssertError(early, WildCatMsgType.AuthenticateResponse, early.requestID, 1, GatewayErrors.TooManyAuthAttempts);
            time.Advance(TimeSpan.FromSeconds(10));
            var allowed = await client.RequestAsync(client.AuthenticateRequest(Guid.NewGuid(), "d"));
            Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, allowed.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg);
            var limited = await client.RequestAsync(client.AuthenticateRequest(Guid.NewGuid(), "d"));
            AssertError(limited, WildCatMsgType.AuthenticateResponse, limited.requestID, 1, GatewayErrors.TooManyAuthAttempts);

            // Other messages are not limited.
            var other2 = WildCatMessageBase.CreateWithRequestID(new StringMessage(), WildCatMsgType.GetDefinedItemsRequest,
                WildCatMsgHeader.normal, client.NextRequestId());
            AssertError(await client.RequestAsync(other2), WildCatMsgType.ErrorMsg, other2.requestID, 1,
                GatewayErrors.UnsupportedMessageType);
        }

        [Fact]
        public async Task SignInLimitDefaultsToFiveAttempts()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();

            var requests = Enumerable.Range(0, 6).Select(_ => client.AuthenticateRequest(Guid.NewGuid(), "d")).ToList();
            foreach (var request in requests)
                await client.SendAsync(request);
            var errors = new List<string>();
            foreach (var request in requests)
            {
                var reply = await client.WaitForAsync(m => m.hasRequestID && m.requestID == request.requestID, "a reply");
                errors.Add(reply.GetPayload<OptionalResponse<EmptyMessage>>().errorMsg);
            }

            Assert.All(errors.Take(5), e => Assert.NotEqual(GatewayErrors.TooManyAuthAttempts, e));
            Assert.Equal(GatewayErrors.TooManyAuthAttempts, errors[5]);
        }

        [Fact]
        public async Task LoginOnASocketThatClosesMidwayDoesNotStayLoggedIn()
        {
            var (developerKey, gameKey) = await CreateDeveloperAndGame();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);
            string device = TestSetup.UniqueId("device");

            // The login is sent and the socket dropped straight away, so the disconnect cleanup
            // runs while the login is still on its way through the grains.
            await client.SendAsync(client.AuthenticateRequest(session, device));
            client.Abort();
            await client.DisposeAsync();

            var mapping = GrainFactory.GetGrain<ICustomIDToAccountIDMap>(UserMappingUtility.GetDeviceIdMappingId(device));
            string playerId = await Poll(async () =>
            {
                var id = await mapping.GetAccountID();
                return string.IsNullOrEmpty(id) ? null : id;
            }, "the account to be created");
            var player = GrainFactory.GetGrain<IPlayer>(playerId);

            // The login went through the grain (it has a login ticket) and was then released.
            await Poll(async () =>
            {
                var state = await player.GetState();
                return state.LoginTicket != Guid.Empty && !state.IsLoggedIn ? "done" : null;
            }, "the login to be released");
            await Poll(() => Task.FromResult(
                host.Connections.PlayerIds.Contains(playerId) ? null : "done"),
                "the gateway to drop the player mapping");
            Assert.Equal(0, host.Connections.Count);
        }

        [Fact]
        public async Task LoggingInAsAnotherPlayerOnTheSameSocketLogsOutTheFirst()
        {
            var (developerKey, gameKey) = await CreateDeveloperAndGame();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            await using var client = await host.ConnectAsync();
            var session = await client.InitializeSessionAsync(developerKey, gameKey);

            var first = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            var second = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
            var firstPlayer = GrainFactory.GetGrain<IPlayer>(first.userId);
            var secondPlayer = GrainFactory.GetGrain<IPlayer>(second.userId);
            await secondPlayer.SetPlayerName("Second");

            Assert.False(await firstPlayer.IsLoggedIn());
            Assert.True(await secondPlayer.IsLoggedIn());
            Assert.DoesNotContain(first.userId, host.Connections.PlayerIds);
            Assert.Contains(second.userId, host.Connections.PlayerIds);

            // Logging the first player out left the session alone: the second player is using it.
            var reply = await client.GetNameAsync(session.ToString());
            Assert.Equal(WildCatMsgType.GetNameResponse, reply.msgType);
            var name = reply.GetPayload<OptionalResponse<StringMessage>>();
            Assert.True(name.isSuccesful, name.errorMsg);
            Assert.Equal("Second", name.msg.value);
        }

        [Fact]
        public async Task RemovingItemsThePlayerDoesNotHoldIsReportedAsAFailure()
        {
            var (developerKey, gameKey) = await CreateDeveloperAndGame();
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var (client, session, playerId) = await ConnectAndLogIn(host, developerKey, gameKey);
            await using var _ = client;

            var item = GrainFactory.GetGrain<IItem>(ResourcesUtility.GetItemKey(gameKey.ToString(), "gem"));
            await item.Initialize("gem", "a gem", null, 100000, false, false, TimeSpan.Zero, false, false, 0, TimeSpan.Zero, null);
            var inventory = GrainFactory.GetGrain<IPlayerInventory>(
                ResourcesUtility.GetPlayerInventoryKey(gameKey.ToString(), playerId));
            await inventory.AddItem(new IInventoryItem[] { item }, new[] { 3 });

            WildCatMessageBase Remove(int count) => WildCatMessageBase.CreateWithAuthDataAndRequestID(
                new RemoveItemInventoryRequestMessage { inventoryName = "", items = new[] { ("gem", count) } },
                WildCatMsgType.RemoveInventoryItemRequest, WildCatMsgHeader.normal, session.ToString(), client.NextRequestId());

            var tooMany = Remove(5);
            var reply = await client.RequestAsync(tooMany);
            Assert.Equal(WildCatMsgType.RemoveInventoryItemResponse, reply.msgType);
            var failed = reply.GetPayload<OptionalResponse<GetInventoryResponseMessage>>();
            Assert.False(failed.isSuccesful);
            Assert.Equal(1, failed.errorCode);

            reply = await client.RequestAsync(Remove(2));
            var removed = reply.GetPayload<OptionalResponse<GetInventoryResponseMessage>>();
            Assert.True(removed.isSuccesful, removed.errorMsg);
            Assert.Equal(new[] { ("gem", 1) }, removed.msg.items);
        }

        [Fact]
        public async Task WebSocketFromAnUnlistedOriginIsRefused()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client,
                options => options.AllowedOrigins = new[] { "https://game.example.com" });

            using (var refused = new ClientWebSocket())
            {
                refused.Options.CollectHttpResponseDetails = true;
                refused.Options.SetRequestHeader("Origin", "https://evil.example.com");
                using var timeout = new CancellationTokenSource(TestSetup.Timeout);
                await Assert.ThrowsAsync<WebSocketException>(() => refused.ConnectAsync(host.WebSocketUri, timeout.Token));
                Assert.Equal(HttpStatusCode.Forbidden, refused.HttpStatusCode);
            }

            await using (var listed = await host.ConnectAsync("https://GAME.example.com/"))
            {
                var request = WildCatMessageBase.CreateWithRequestID(new StringMessage(), WildCatMsgType.GetDefinedItemsRequest,
                    WildCatMsgHeader.normal, listed.NextRequestId());
                Assert.Equal(WildCatMsgType.ErrorMsg, (await listed.RequestAsync(request)).msgType);
            }

            // Native clients send no Origin header at all.
            await using (var native = await host.ConnectAsync())
            {
                var request = WildCatMessageBase.CreateWithRequestID(new StringMessage(), WildCatMsgType.GetDefinedItemsRequest,
                    WildCatMsgHeader.normal, native.NextRequestId());
                Assert.Equal(WildCatMsgType.ErrorMsg, (await native.RequestAsync(request)).msgType);
            }
        }

        /// <summary>
        /// Calls <paramref name="probe"/> until it returns a value, for at most
        /// <see cref="TestSetup.Timeout"/>.
        /// </summary>
        private static async Task<T> Poll<T>(Func<Task<T>> probe, string description) where T : class
        {
            var deadline = DateTime.UtcNow + TestSetup.Timeout;
            while (true)
            {
                var value = await probe();
                if (value != null)
                    return value;
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Gave up waiting for {description}.");
                await Task.Delay(50);
            }
        }
    }
}
