using System;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// The connection's own behavior against the real gateway: request timeouts, the heartbeat,
    /// explicit disconnects, server-side drops, reconnection with session resume, and the
    /// main-thread dispatcher.
    /// </summary>
    [Collection(ServerCollection.Name)]
    public class ConnectionTests : ServerTestBase
    {
        public ConnectionTests(ServerFixture server)
            : base(server)
        {
        }

        [Fact]
        public async Task ARequestWithoutAnAnswerInTimeFailsWithATimeoutAndTheLateAnswerIsIgnored()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var logger = new ListLogger();
            var (client, _) = await LoggedInClientAsync(developerKey, gameKey, logger: logger);

            // The matchmaker answers after its one second wait at the earliest; the request gives up first.
            await Assert.ThrowsAsync<TimeoutException>(() => Wait.For(client.Connection.RequestAsync<FindMatchResponse>(
                WildCatMsgType.FindMatchRequest, new FindMatchRequest(Unique.Id("mode"), 0, 1), WildCatMsgType.FindMatchResponse,
                new RequestOptions { Timeout = TimeSpan.FromMilliseconds(200) })));

            await Wait.UntilAsync(() => logger.Contains("Ignored a late FindMatchResponse"), "the late answer to arrive");
            Assert.True(client.IsConnected);
            Assert.Equal("", await Wait.For(client.Profile.GetNameAsync()));
        }

        [Fact]
        public async Task HeartbeatsMeasureTheRoundTripTime()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            WildCatClient client = NewClient(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(100));
            await Wait.For(client.ConnectAsync());

            // Before a login the heartbeat is a GetServerTime; after it, a HeartBeat with the ticket.
            await Wait.UntilAsync(() => client.Connection.RoundTripTime > TimeSpan.Zero, "a heartbeat before login");
            await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            await Wait.For(client.Auth.LoginWithDeviceAsync(Unique.Id("device")));
            await Task.Delay(500);

            Assert.True(client.IsConnected);
            Assert.True(client.Connection.SmoothedRoundTripTime > TimeSpan.Zero);
            Assert.True(client.Connection.RoundTripTime < TimeSpan.FromSeconds(5), $"RTT {client.Connection.RoundTripTime}");
        }

        [Fact]
        public async Task DisconnectAsyncClosesOnceAndTheClientCanConnectAgain()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            string device = Unique.Id("device");
            var (client, login) = await LoggedInClientAsync(developerKey, gameKey, device);
            var disconnected = new Recorder<DisconnectedEventArgs>();
            var reconnecting = new Recorder<ReconnectingEventArgs>();
            client.Disconnected += disconnected.Add;
            client.Reconnecting += reconnecting.Add;

            await Wait.For(client.DisconnectAsync());

            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);
            DisconnectedEventArgs args = await disconnected.WaitForAnyAsync("the Disconnected event");
            Assert.Equal(DisconnectReason.ClientClosed, args.Reason);
            Assert.False(client.IsLoggedIn);
            Assert.False(client.Session.IsInitialized);
            var error = await Assert.ThrowsAsync<WildCatDisconnectedException>(() => client.Profile.GetNameAsync());
            Assert.Equal(DisconnectReason.ClientClosed, error.Reason);
            await Task.Delay(300);
            Assert.Equal(1, disconnected.Count);
            Assert.Equal(0, reconnecting.Count);

            // The old SDK cleared its response handlers on Disconnect, so nothing worked after reconnecting.
            await Wait.For(client.ConnectAsync());
            await Wait.For(client.Session.InitializeAsync(developerKey, gameKey));
            LoginResult again = await Wait.For(client.Auth.LoginWithDeviceAsync(device));
            Assert.Equal(login.PlayerId, again.PlayerId);
            Assert.Equal("", await Wait.For(client.Profile.GetNameAsync()));
        }

        [Fact]
        public async Task WhenTheServerDropsTheSocketTheClientReconnectsAndResumesTheSession()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, login) = await LoggedInClientAsync(developerKey, gameKey);
            await Wait.For(client.Profile.SetNameAsync("Grace"));
            Guid firstTicket = client.Session.Ticket;
            var reconnecting = new Recorder<ReconnectingEventArgs>();
            var reconnected = new Recorder<bool>();
            var disconnected = new Recorder<DisconnectedEventArgs>();
            client.Reconnecting += reconnecting.Add;
            client.Reconnected += () => reconnected.Add(true);
            client.Disconnected += disconnected.Add;

            // A request in flight when the socket drops fails at once instead of waiting for its timeout.
            Task<MatchInfo> inFlight = client.Matchmaking.FindMatchAsync(Unique.Id("mode"), 0, TimeSpan.FromSeconds(20));
            await Task.Delay(200);
            Assert.True(Server.DropAllConnections() > 0);

            var error = await Assert.ThrowsAsync<WildCatDisconnectedException>(() => Wait.For(inFlight));
            Assert.Equal(DisconnectReason.ConnectionLost, error.Reason);
            ReconnectingEventArgs first = await reconnecting.WaitForAnyAsync("the Reconnecting event");
            Assert.Equal(1, first.Attempt);
            Assert.Equal(DisconnectReason.ConnectionLost, first.Reason);
            await reconnected.WaitForAnyAsync("the Reconnected event");

            Assert.True(client.IsConnected);
            Assert.True(client.IsLoggedIn);
            Assert.Equal(login.PlayerId, client.Auth.PlayerId);
            Assert.NotEqual(Guid.Empty, client.Session.Ticket);
            Assert.NotEqual(firstTicket, client.Session.Ticket);
            Assert.Equal("Grace", await Wait.For(client.Profile.GetNameAsync()));
            Assert.Equal(0, disconnected.Count);
        }

        [Fact]
        public async Task PushEventsArriveExactlyOnceAfterAResume()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, _) = await LoggedInClientAsync(developerKey, gameKey);
            var (bob, bobLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var aliceBack = new Recorder<bool>();
            var bobBack = new Recorder<bool>();
            alice.Reconnected += () => aliceBack.Add(true);
            bob.Reconnected += () => bobBack.Add(true);

            Assert.True(Server.DropAllConnections() > 0);
            await aliceBack.WaitForAnyAsync("Alice's reconnection");
            await bobBack.WaitForAnyAsync("Bob's reconnection");

            // The server subscribed Bob's new socket to his events; the dead one must not add a copy.
            var invitations = new Recorder<FriendEvent>();
            bob.Friends.InvitationReceived += invitations.Add;
            await Wait.For(alice.Friends.InviteByPlayerIdAsync(bobLogin.PlayerId));
            await invitations.WaitForAnyAsync("the invitation after the resume");
            await Task.Delay(1000);
            Assert.Equal(1, invitations.Count);
        }

        [Fact]
        public async Task ReconnectionBacksOffUntilTheServerTakesConnectionsAgain()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, login) = await LoggedInClientAsync(developerKey, gameKey,
                configure: o => o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(400));
            var reconnecting = new Recorder<ReconnectingEventArgs>();
            var reconnected = new Recorder<bool>();
            client.Reconnecting += reconnecting.Add;
            client.Reconnected += () => reconnected.Add(true);

            Server.RefuseConnections = true;
            try
            {
                Server.DropAllConnections();
                await reconnecting.WaitForAsync(r => r.Attempt >= 4, "four reconnection attempts");
                Assert.Equal(ConnectionState.Reconnecting, client.Connection.State);
                await Assert.ThrowsAsync<WildCatDisconnectedException>(() => client.Profile.GetNameAsync());
                Assert.False(client.Relay.Send(Guid.NewGuid(), RelayClient.MessageType(1), new EmptyMessage()));
            }
            finally
            {
                Server.RefuseConnections = false;
            }
            await reconnected.WaitForAnyAsync("the Reconnected event");

            var attempts = reconnecting.Items;
            Assert.Equal(Enumerable.Range(1, attempts.Count), attempts.Select(a => a.Attempt));
            Assert.All(attempts, a => Assert.InRange(a.Delay, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400)));
            Assert.True(attempts[2].Delay > attempts[0].Delay, "The delay grows from attempt to attempt.");
            Assert.NotNull(attempts[1].LastError);
            Assert.Equal(login.PlayerId, client.Auth.PlayerId);
            Assert.Equal("", await Wait.For(client.Profile.GetNameAsync()));
        }

        [Fact]
        public async Task ReconnectionGivesUpAfterTheConfiguredAttempts()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, _) = await LoggedInClientAsync(developerKey, gameKey, configure: o => o.MaxReconnectAttempts = 2);
            var disconnected = new Recorder<DisconnectedEventArgs>();
            var reconnecting = new Recorder<ReconnectingEventArgs>();
            client.Disconnected += disconnected.Add;
            client.Reconnecting += reconnecting.Add;

            Server.RefuseConnections = true;
            try
            {
                Server.DropAllConnections();
                DisconnectedEventArgs args = await disconnected.WaitForAnyAsync("the Disconnected event");
                Assert.Equal(DisconnectReason.ReconnectFailed, args.Reason);
            }
            finally
            {
                Server.RefuseConnections = false;
            }
            Assert.Equal(2, reconnecting.Count);
            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);
            Assert.False(client.IsLoggedIn);
        }

        [Fact]
        public async Task WithoutAutoReconnectADropIsAFinalDisconnect()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, _) = await LoggedInClientAsync(developerKey, gameKey, configure: o => o.AutoReconnect = false);
            var disconnected = new Recorder<DisconnectedEventArgs>();
            client.Disconnected += disconnected.Add;

            Server.DropAllConnections();

            DisconnectedEventArgs args = await disconnected.WaitForAnyAsync("the Disconnected event");
            Assert.Equal(DisconnectReason.ConnectionLost, args.Reason);
            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);
            await Task.Delay(300);
            Assert.Equal(1, disconnected.Count);
        }

        [Fact]
        public async Task WithAQueuedDispatcherCompletionsAndEventsWaitForThePump()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var dispatcher = new QueuedDispatcher();
            WildCatClient client = NewClient(dispatcher: dispatcher);
            var connected = new Recorder<bool>();
            client.Connected += () => connected.Add(true);

            // Even ConnectAsync completes through the dispatcher, after the Connected event.
            Task connect = client.ConnectAsync();
            await Wait.UntilAsync(() => dispatcher.Count == 2, "the Connected event and the connect completion to be queued");
            Assert.False(connect.IsCompleted);
            Assert.Equal(0, connected.Count);
            Assert.Equal(2, dispatcher.Pump());
            Assert.Equal(1, connected.Count);
            Assert.True(connect.IsCompletedSuccessfully);

            Task<Guid> session = client.Session.InitializeAsync(developerKey, gameKey);
            await Wait.UntilAsync(() => dispatcher.Count > 0, "the answer to be queued");
            Assert.False(session.IsCompleted);
            dispatcher.Pump();
            Assert.True(session.IsCompletedSuccessfully);

            // Pumped in a loop, the way WildCatBehaviour pumps it every frame in Update.
            Task<LoginResult> login = client.Auth.LoginWithDeviceAsync(Unique.Id("device"));
            await Wait.UntilAsync(() =>
            {
                dispatcher.Pump();
                return login.IsCompleted;
            }, "the login to complete through the pump");
            Assert.True(login.IsCompletedSuccessfully);
            Assert.True(client.IsLoggedIn);

            Task disconnect = client.DisconnectAsync();
            await Wait.UntilAsync(() =>
            {
                dispatcher.Pump();
                return disconnect.IsCompleted;
            }, "the disconnect to complete through the pump");
            Assert.Equal(ConnectionState.Disconnected, client.Connection.State);
        }
    }
}
