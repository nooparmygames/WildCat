using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// The engine-free core against <see cref="FakeServer"/>: how frames are built and answers
    /// matched, what errors become, timeouts, cancellation, heartbeats, reconnection and resume.
    /// </summary>
    public class CoreTests
    {
        private static readonly Guid DeveloperKey = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid GameKey = new Guid("22222222-2222-2222-2222-222222222222");

        private static WildCatClient NewClient(FakeServer server, Action<WildCatClientOptions> configure = null,
            IMainThreadDispatcher dispatcher = null, ListLogger logger = null)
        {
            var options = new WildCatClientOptions
            {
                ServerUri = new Uri("ws://localhost:5000/ws"),
                HeartbeatInterval = TimeSpan.Zero,
                ReconnectMinDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            };
            configure?.Invoke(options);
            return new WildCatClient(options, dispatcher, server, logger ?? new ListLogger());
        }

        private static async Task<WildCatClient> LoggedInAsync(FakeServer server, Action<WildCatClientOptions> configure = null, ListLogger logger = null)
        {
            server.Respond ??= FakeServer.Basics;
            WildCatClient client = NewClient(server, configure, null, logger);
            await Wait.For(client.ConnectAsync());
            await Wait.For(client.Session.InitializeAsync(DeveloperKey, GameKey));
            await Wait.For(client.Auth.LoginWithDeviceAsync("device-1"));
            return client;
        }

        [Fact]
        public async Task FramesCarryTheTicketWhereTheGatewayExpectsIt()
        {
            var server = new FakeServer { Respond = FakeServer.Basics };
            using WildCatClient client = await LoggedInAsync(server);
            Guid match = Guid.NewGuid();
            Task leave = client.Matches.LeaveAsync(match);
            Assert.True(client.Relay.Send(match, RelayClient.MessageType(3), new IntegerMessage { value = 9 }));
            await Wait.For(client.Profile.GetNameAsync());

            var sent = server.Received;
            var init = sent.Single(f => f.msgType == WildCatMsgType.InitializeSessionRequest);
            Assert.Equal(WildCatMsgHeader.HasRequestID, init.header);

            // Authenticate carries the ticket in its payload, not in the header.
            var auth = sent.Single(f => f.msgType == WildCatMsgType.AuthenticateRequest);
            Assert.Equal(WildCatMsgHeader.HasRequestID, auth.header);
            Assert.Equal(client.Session.Ticket.ToString(), auth.GetPayload<AuthenticationRequest>().ticket);
            Assert.True(auth.GetPayload<AuthenticationRequest>().AutoCreate);

            var getName = sent.Single(f => f.msgType == WildCatMsgType.GetNameRequest);
            Assert.Equal(WildCatMsgHeader.HasRequestID | WildCatMsgHeader.HasAuthData, getName.header);
            Assert.Equal(client.Session.Ticket.ToString(), getName.ticket);

            var leaveFrame = sent.Single(f => f.msgType == WildCatMsgType.LeaveGameRequest);
            Assert.True(leaveFrame.HasGameTarget && leaveFrame.hasAuthData && leaveFrame.hasRequestID);
            Assert.Equal(match.ToString(), leaveFrame.game);

            var relayed = sent.Single(f => f.msgType == RelayClient.MessageType(3));
            Assert.True(relayed.HasGameTarget && relayed.hasAuthData);
            Assert.False(relayed.hasRequestID);
            Assert.Equal(9, relayed.GetPayload<IntegerMessage>().value);

            // Request ids are unique.
            var ids = sent.Where(f => f.hasRequestID).Select(f => f.requestID).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.False(leave.IsCompleted);
        }

        [Fact]
        public async Task AnswersAreMatchedByRequestIdInWhateverOrderTheyCome()
        {
            var held = new List<WildCatMessageBase>();
            var server = new FakeServer
            {
                Respond = frame =>
                {
                    if (frame.msgType != WildCatMsgType.GetPlayerNameRequest)
                        return FakeServer.Basics(frame);
                    lock (held)
                        held.Add(frame);
                    return null;
                },
            };
            using WildCatClient client = await LoggedInAsync(server);

            var requests = Enumerable.Range(0, 20).Select(i => client.Profile.GetPlayerNameAsync("p" + i)).ToList();
            await Wait.UntilAsync(() => { lock (held) return held.Count == 20; }, "20 requests to reach the server");
            List<WildCatMessageBase> frames;
            lock (held)
                frames = held.ToList();
            frames.Reverse();
            foreach (WildCatMessageBase frame in frames)
            {
                string who = frame.GetPayload<StringMessage>().value;
                server.Current.Push(FakeServer.Answer(frame, WildCatMsgType.GetPlayerNameResponse, new StringMessage { value = "name of " + who }));
            }

            string[] names = await Wait.For(Task.WhenAll(requests));
            for (int i = 0; i < 20; ++i)
                Assert.Equal("name of p" + i, names[i]);
            Assert.False(server.Current.ConcurrentSendSeen);
        }

        [Fact]
        public async Task ErrorAnswersBecomeExceptionsWithTheServersCodeAndText()
        {
            var server = new FakeServer
            {
                Respond = frame =>
                {
                    switch (frame.msgType)
                    {
                        case WildCatMsgType.SetNameRequest:
                            return new[] { FakeServer.Failure(frame, WildCatMsgType.SetNameResponse, 2, "Taken") };
                        case WildCatMsgType.GetCloudDataRequest:
                            return new[] { FakeServer.Failure(frame, WildCatMsgType.ErrorMsg, 1, "The session ticket is not valid.") };
                        case WildCatMsgType.GetPublisherDataRequest:
                            // An answer of the wrong type.
                            return new[] { FakeServer.Answer(frame, WildCatMsgType.GetNameResponse, new StringMessage()) };
                        default:
                            return FakeServer.Basics(frame);
                    }
                },
            };
            using WildCatClient client = await LoggedInAsync(server);

            var taken = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.Profile.SetNameAsync("x")));
            Assert.Equal(2, taken.ErrorCode);
            Assert.Equal("Taken", taken.Message);
            Assert.False(taken.IsGatewayRefusal);
            Assert.Equal(WildCatMsgType.SetNameRequest, taken.RequestType);

            var refused = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.CloudData.GetAsync("k")));
            Assert.True(refused.IsGatewayRefusal);
            Assert.Equal("The session ticket is not valid.", refused.Message);

            var wrong = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(client.CloudData.GetPublisherDataAsync("k")));
            Assert.Equal(WildCatErrorCodes.UnexpectedResponse, wrong.ErrorCode);
        }

        [Fact]
        public async Task TimeoutsAndCancellationEndARequestAndLateAnswersAreDropped()
        {
            WildCatMessageBase unanswered = null;
            var server = new FakeServer
            {
                Respond = frame =>
                {
                    if (frame.msgType == WildCatMsgType.GetPlayerNameRequest)
                    {
                        unanswered = frame;
                        return null;
                    }
                    return FakeServer.Basics(frame);
                },
            };
            var logger = new ListLogger();
            using WildCatClient client = await LoggedInAsync(server, o => o.RequestTimeout = TimeSpan.FromMilliseconds(150), logger);

            var timeout = await Assert.ThrowsAsync<TimeoutException>(() => Wait.For(client.Profile.GetPlayerNameAsync("p")));
            Assert.Contains("GetPlayerNameRequest", timeout.Message);
            server.Current.Push(FakeServer.Answer(unanswered, WildCatMsgType.GetPlayerNameResponse, new StringMessage { value = "late" }));
            await Wait.UntilAsync(() => logger.Contains("Ignored a late GetPlayerNameResponse"), "the late answer to be dropped");

            using var cancel = new CancellationTokenSource();
            Task<StringMessage> pending = client.Connection.RequestAsync<StringMessage>(WildCatMsgType.GetPlayerNameRequest,
                new StringMessage { value = "p" }, WildCatMsgType.GetPlayerNameResponse,
                new RequestOptions { Timeout = Timeout.InfiniteTimeSpan }, cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait.For(pending));
            Assert.Equal("name", await Wait.For(client.Profile.GetNameAsync()));
        }

        [Fact]
        public async Task AMissedHeartbeatDropsTheSocketAndTheClientReconnects()
        {
            bool answerHeartbeats = false;
            var server = new FakeServer
            {
                Respond = frame => frame.msgType == WildCatMsgType.GetServerTimeRequest && !Volatile.Read(ref answerHeartbeats)
                    ? null
                    : FakeServer.Basics(frame),
            };
            using WildCatClient client = NewClient(server, o =>
            {
                o.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                o.HeartbeatTimeout = TimeSpan.FromMilliseconds(100);
            });
            var reconnecting = new Recorder<ReconnectingEventArgs>();
            var reconnected = new Recorder<bool>();
            client.Reconnecting += reconnecting.Add;
            client.Reconnected += () => reconnected.Add(true);
            await Wait.For(client.ConnectAsync());

            ReconnectingEventArgs drop = await reconnecting.WaitForAnyAsync("the heartbeat timeout");
            Assert.Equal(DisconnectReason.HeartbeatTimeout, drop.Reason);
            Volatile.Write(ref answerHeartbeats, true);
            await reconnected.WaitForAnyAsync("the reconnection");
            await Wait.UntilAsync(() => client.Connection.RoundTripTime > TimeSpan.Zero, "a measured round trip");
        }

        [Fact]
        public async Task ReconnectionResumesTheSessionWithTheLastCredentials()
        {
            var server = new FakeServer { Respond = FakeServer.Basics };
            using WildCatClient client = NewClient(server);
            var reconnected = new Recorder<bool>();
            client.Reconnected += () => reconnected.Add(true);
            await Wait.For(client.ConnectAsync());
            await Wait.For(client.Session.InitializeAsync(DeveloperKey, GameKey));
            await Wait.For(client.Auth.LoginWithUserNameAsync("ada", "secret words", createAccount: true));
            Guid firstTicket = client.Session.Ticket;

            server.Current.Break();
            await reconnected.WaitForAnyAsync("the reconnection");

            Assert.Equal(2, server.Transports.Count);
            var resumed = server.Transports[1].Received;
            var init = resumed.Single(f => f.msgType == WildCatMsgType.InitializeSessionRequest).GetPayload<InitializeSessionRequest>();
            Assert.Equal(DeveloperKey, init.DeveloperKey);
            Assert.Equal(GameKey, init.GameKey);
            var login = resumed.Single(f => f.msgType == WildCatMsgType.AuthenticateRequest).GetPayload<AuthenticationRequest>();
            Assert.Equal(LoginType.UserWithPassword, login.LoginType);
            Assert.Equal("ada", login.Id);
            Assert.Equal("secret words", login.PasswordHash);
            Assert.Equal(client.Session.Ticket.ToString(), login.ticket);
            Assert.NotEqual(firstTicket, client.Session.Ticket);
            Assert.True(client.IsLoggedIn);
            Assert.Equal("player-ada", client.Auth.PlayerId);
        }

        [Fact]
        public async Task ARefusedResumeEndsTheConnection()
        {
            int logins = 0;
            var server = new FakeServer
            {
                Respond = frame =>
                {
                    if (frame.msgType == WildCatMsgType.AuthenticateRequest && Interlocked.Increment(ref logins) > 1)
                        return new[] { FakeServer.Failure(frame, WildCatMsgType.AuthenticateResponse, 1, "Login failed.") };
                    return FakeServer.Basics(frame);
                },
            };
            using WildCatClient client = await LoggedInAsync(server);
            var disconnected = new Recorder<DisconnectedEventArgs>();
            client.Disconnected += disconnected.Add;

            server.Current.Break();

            DisconnectedEventArgs args = await disconnected.WaitForAnyAsync("the Disconnected event");
            Assert.Equal(DisconnectReason.ResumeFailed, args.Reason);
            Assert.IsType<WildCatException>(args.Error);
            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);
            Assert.False(client.IsLoggedIn);
        }

        [Fact]
        public async Task AServerCloseWithoutAutoReconnectIsReportedOnce()
        {
            var server = new FakeServer { Respond = FakeServer.Basics };
            using WildCatClient client = NewClient(server, o => o.AutoReconnect = false);
            var disconnected = new Recorder<DisconnectedEventArgs>();
            client.Disconnected += disconnected.Add;
            await Wait.For(client.ConnectAsync());

            server.Current.CloseFromServer();

            DisconnectedEventArgs args = await disconnected.WaitForAnyAsync("the Disconnected event");
            Assert.Equal(DisconnectReason.ServerClosed, args.Reason);
            await Task.Delay(100);
            Assert.Equal(1, disconnected.Count);
            Assert.True(server.Current.CloseSent, "The client answers the server's close.");
            var error = await Assert.ThrowsAsync<WildCatDisconnectedException>(() => client.Profile.GetNameAsync());
            Assert.Equal(DisconnectReason.ServerClosed, error.Reason);
        }

        [Fact]
        public async Task AConnectionThatCannotOpenThrows()
        {
            var server = new FakeServer { RefuseConnections = true };
            using WildCatClient client = NewClient(server);

            var error = await Assert.ThrowsAsync<WildCatDisconnectedException>(() => Wait.For(client.ConnectAsync()));
            Assert.Equal(DisconnectReason.ConnectFailed, error.Reason);
            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);

            server.RefuseConnections = false;
            await Wait.For(client.ConnectAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        }

        [Fact]
        public async Task HandlersThatThrowAreLoggedAndTheOthersStillRun()
        {
            var server = new FakeServer { Respond = FakeServer.Basics };
            var logger = new ListLogger();
            using WildCatClient client = await LoggedInAsync(server, null, logger);
            var events = new Recorder<string>();
            client.Friends.InvitationReceived += _ => throw new InvalidOperationException("handler bug");
            client.Friends.InvitationReceived += e => events.Add(e.PlayerId);
            WildCatMsgType game = RelayClient.MessageType(7);
            client.Relay.On<IntegerMessage>(game, _ => throw new InvalidOperationException("relay handler bug"));
            client.Relay.On<IntegerMessage>(game, m => events.Add("relay " + m.value));

            server.Current.Push(WildCatMessageBase.Create(OptionalResponse<FriendlistMessage>.CreateSuccessful(
                new FriendlistMessage { friend = "p2", friendName = "Bo" }), WildCatMsgType.FriendshipInvitationEvent, WildCatMsgHeader.normal));
            server.Current.Push(WildCatMessageBase.CreateWithGameTarget(new IntegerMessage { value = 4 }, game, WildCatMsgHeader.normal,
                Guid.NewGuid().ToString(), "ticket"));
            server.Current.PushRaw(new byte[] { 1, 2 });
            server.Current.Push(WildCatMessageBase.Create(OptionalResponse<FriendlistMessage>.CreateSuccessful(
                new FriendlistMessage { friend = "p3" }), WildCatMsgType.FriendshipInvitationEvent, WildCatMsgHeader.normal));

            await Wait.UntilAsync(() => events.Count == 3, "three handled events");
            Assert.Equal(new[] { "p2", "relay 4", "p3" }, events.Items);
            Assert.True(logger.Contains("handler bug"));
            Assert.True(logger.Contains("could not be read"));
            Assert.True(client.IsConnected);
        }

        [Fact]
        public async Task PasswordsAreOnlySentOverTlsOrToThisMachine()
        {
            var server = new FakeServer { Respond = FakeServer.Basics };
            using WildCatClient remote = NewClient(server, o => o.ServerUri = new Uri("ws://game.example.com/ws"));
            await Wait.For(remote.ConnectAsync());
            await Wait.For(remote.Session.InitializeAsync(DeveloperKey, GameKey));

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => remote.Auth.LoginWithUserNameAsync("ada", "pw"));
            Assert.Contains("wss://", refused.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => remote.Auth.CreateAccountWithEmailAsync("ada@example.com", "pw"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => remote.Auth.AddEmailAsync("ada@example.com", "pw"));
            // Device logins carry no password.
            await Wait.For(remote.Auth.LoginWithDeviceAsync("d"));
            Assert.DoesNotContain(server.Received, f => f.msgType == WildCatMsgType.AuthenticateRequest
                                                         && f.GetPayload<AuthenticationRequest>().LoginType != LoginType.Device);

            var logger = new ListLogger();
            using WildCatClient allowed = NewClient(server, o =>
            {
                o.ServerUri = new Uri("ws://game.example.com/ws");
                o.AllowPlainTextPasswordsWithoutTls = true;
            }, null, logger);
            await Wait.For(allowed.ConnectAsync());
            await Wait.For(allowed.Session.InitializeAsync(DeveloperKey, GameKey));
            await Wait.For(allowed.Auth.LoginWithUserNameAsync("ada", "pw"));
            Assert.True(logger.Contains("plain text"));

            using WildCatClient secure = NewClient(server, o => o.ServerUri = new Uri("wss://game.example.com/ws"));
            await Wait.For(secure.ConnectAsync());
            await Wait.For(secure.Session.InitializeAsync(DeveloperKey, GameKey));
            await Wait.For(secure.Auth.LoginWithUserNameAsync("ada", "pw"));
        }

        [Fact]
        public void ReconnectionDelaysDoubleWithinTheLimitsAndSpread()
        {
            var options = new WildCatClientOptions();
            Assert.Equal(TimeSpan.FromSeconds(1), options.GetReconnectDelay(1, 0.5));
            Assert.Equal(TimeSpan.FromSeconds(2), options.GetReconnectDelay(2, 0.5));
            Assert.Equal(TimeSpan.FromSeconds(16), options.GetReconnectDelay(5, 0.5));
            Assert.Equal(TimeSpan.FromSeconds(30), options.GetReconnectDelay(6, 0.5));
            Assert.Equal(TimeSpan.FromSeconds(30), options.GetReconnectDelay(1000, 0.5));
            for (int attempt = 1; attempt < 12; ++attempt)
            {
                foreach (double r in new[] { 0.0, 0.3, 0.99 })
                    Assert.InRange(options.GetReconnectDelay(attempt, r), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
            }
            Assert.Equal(TimeSpan.FromSeconds(2 * 0.8), options.GetReconnectDelay(2, 0));
            Assert.Equal(TimeSpan.FromSeconds(24), options.GetReconnectDelay(9, 0));
        }

        [Fact]
        public void OptionsAreCheckedWhenTheClientIsCreated()
        {
            Assert.Throws<ArgumentException>(() => new WildCatClient(new WildCatClientOptions { ServerUri = new Uri("http://localhost/ws") }));
            Assert.Throws<ArgumentException>(() => new WildCatClient(new WildCatClientOptions { RequestTimeout = TimeSpan.Zero }));
            Assert.Throws<ArgumentException>(() => new WildCatClient(new WildCatClientOptions
            {
                ReconnectMinDelay = TimeSpan.FromSeconds(5),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
            }));

            // The client copies the options.
            var options = new WildCatClientOptions();
            using var client = new WildCatClient(options);
            options.ServerUri = new Uri("wss://elsewhere.example.com/ws");
            Assert.Equal(new Uri("ws://localhost:5000/ws"), client.Connection.ServerUri);
        }

        [Fact]
        public async Task TheQueuedDispatcherRunsActionsInOrderOnThePumpingThread()
        {
            var dispatcher = new QueuedDispatcher();
            var order = new List<int>();
            var threads = new List<int>();
            var posters = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
            {
                for (int i = 0; i < 100; ++i)
                    dispatcher.Post(() => { lock (threads) threads.Add(Environment.CurrentManagedThreadId); });
            })).ToArray();
            await Task.WhenAll(posters);
            int pumpThread = Environment.CurrentManagedThreadId;
            Assert.Equal(400, dispatcher.Pump());
            Assert.All(threads, t => Assert.Equal(pumpThread, t));

            dispatcher.Post(() => order.Add(1));
            dispatcher.Post(() => throw new InvalidOperationException("bad"));
            dispatcher.Post(() => order.Add(2));
            var errors = new List<Exception>();
            Assert.Equal(2, dispatcher.Pump(2, errors.Add));
            Assert.Equal(new[] { 1 }, order);
            Assert.Single(errors);
            Assert.Equal(1, dispatcher.Pump());
            Assert.Equal(new[] { 1, 2 }, order);

            dispatcher.Post(() => throw new InvalidOperationException("bad"));
            Assert.Throws<AggregateException>(() => dispatcher.Pump());
        }
    }
}
