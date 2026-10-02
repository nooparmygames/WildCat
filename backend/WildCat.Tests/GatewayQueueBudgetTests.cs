using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using WildCat.Interfaces;
using WildCat.Gateway;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The byte budgets of a connection's queues: the outbound queue closes a client that stops
    /// reading once the bytes waiting pass Gateway:OutboundQueueBytes, and the relay queue holds at
    /// most Gateway:RelayQueueBytes before the receive loop waits. Before, both were bounded by
    /// message count only, so a stalled reader could hold 1024 messages of 1 MiB each.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayQueueBudgetTests
    {
        private const int FrameSize = 64 * 1024;
        private const WildCatMsgType GameMessage = WildCatMsgType.HighestInternal + 7;

        private readonly InProcessTestCluster cluster;

        public GatewayQueueBudgetTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task ASlowReaderIsDroppedOnceItsQueuedBytesPassTheBudget()
        {
            const int budget = 1024 * 1024;
            var logs = new TestLogSink();
            var socket = new StalledWebSocket();
            var transport = new WebSocketConnection(socket, WebSocketConnection.NextId(), maxMessageBytes: 1024 * 1024,
                outboundCapacity: 1024, outboundQueueBytes: budget, logs.CreateLogger("transport"));
            var run = transport.RunAsync(_ => Task.CompletedTask, CancellationToken.None);

            byte[] frame = new byte[FrameSize];
            int accepted = 0;
            while (transport.TrySend(frame))
            {
                Assert.True(++accepted <= 1024, "The outbound queue took more than 1024 frames.");
                Assert.True(transport.QueuedBytes <= budget, $"{transport.QueuedBytes} bytes are queued.");
            }

            // The writer holds the first frame in a send that never ends; with it, 16 frames of
            // 64 KiB fill the 1 MiB budget. The count limit alone would have taken 1025.
            Assert.Equal(budget / FrameSize, accepted);
            Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is not reading its messages"));
            Assert.False(transport.TrySend(new byte[1]));

            // The writer is stuck, so the close handshake cannot go out: the socket is aborted.
            await socket.Aborted.WaitAsync(TestSetup.Timeout);
            await run.WaitAsync(TestSetup.Timeout);
        }

        [Fact]
        public void ASingleFrameLargerThanTheBudgetGoesOutWhenNothingElseWaits()
        {
            var logs = new TestLogSink();
            var socket = new StalledWebSocket();
            var transport = new WebSocketConnection(socket, WebSocketConnection.NextId(), 1024, 16, outboundQueueBytes: 1000,
                logs.CreateLogger("transport"));

            Assert.True(transport.TrySend(new byte[5000]));
            Assert.Equal(5000, transport.QueuedBytes);
            Assert.False(transport.TrySend(new byte[1]));
            Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is not reading its messages"));
            transport.Abort();
        }

        [Fact]
        public async Task RelayQueueHoldsAtMostItsByteBudget()
        {
            const int budget = 256 * 1024;
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var dispatcher = host.Services.GetRequiredService<MessageDispatcher>();
            var logs = new TestLogSink();
            var transport = new WebSocketConnection(new StalledWebSocket(), WebSocketConnection.NextId(), 1024 * 1024, 1024,
                1024 * 1024, logs.CreateLogger("transport"));
            // No relay worker runs for this connection, so nothing leaves its relay queue.
            var connection = new GatewayConnection(transport, new TokenBucket(5, TimeSpan.FromSeconds(10), TimeProvider.System), 8,
                relayQueueBytes: budget);

            byte[] frame = WireCodec.Encode(WildCatMessageBase.CreateWithGameTarget(new RawMessage { value = new byte[FrameSize] },
                GameMessage, WildCatMsgHeader.normal, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));
            int fits = budget / frame.Length;
            for (int i = 0; i < fits; ++i)
            {
                var queued = dispatcher.OnFrameAsync(connection, frame);
                Assert.True(queued.IsCompleted, $"frame {i + 1} of {fits} waited");
                await queued;
            }
            Assert.Equal(fits, connection.RelayQueue.Count);
            Assert.True(connection.RelayQueue.QueuedBytes <= budget);

            // The next frame does not fit: the receive loop waits, and so stops reading the socket.
            var waiting = dispatcher.OnFrameAsync(connection, frame);
            await Task.Delay(300);
            Assert.False(waiting.IsCompleted, "A frame past the relay queue's byte budget was queued.");
            Assert.Equal(fits, connection.RelayQueue.Count);

            // Once the worker takes a frame, it goes in.
            Assert.True(connection.RelayQueue.TryDequeue(out _));
            await waiting.WaitAsync(TestSetup.Timeout);
            Assert.Equal(fits, connection.RelayQueue.Count);
            Assert.True(connection.RelayQueue.QueuedBytes <= budget);
            transport.Abort();
        }

        [Fact]
        public async Task AClosingConnectionStopsWaitingForRelayRoom()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var dispatcher = host.Services.GetRequiredService<MessageDispatcher>();
            var transport = new WebSocketConnection(new StalledWebSocket(), WebSocketConnection.NextId(), 1024 * 1024, 1024,
                1024 * 1024, new TestLogSink().CreateLogger("transport"));
            var connection = new GatewayConnection(transport, new TokenBucket(5, TimeSpan.FromSeconds(10), TimeProvider.System), 8,
                relayQueueBytes: 100);
            byte[] frame = WireCodec.Encode(WildCatMessageBase.CreateWithGameTarget(new RawMessage { value = new byte[200] },
                GameMessage, WildCatMsgHeader.normal, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

            await dispatcher.OnFrameAsync(connection, frame).WaitAsync(TestSetup.Timeout);
            var waiting = dispatcher.OnFrameAsync(connection, frame);
            Assert.False(waiting.IsCompleted);

            transport.Abort();
            await waiting.WaitAsync(TestSetup.Timeout);
            Assert.Equal(1, connection.RelayQueue.Count);
        }
    }

    /// <summary>
    /// The login a connection holds is replaced as a whole, so push callbacks on other threads
    /// never read the player of one login with the game or ticket of another (they used to read
    /// four separately set properties, one of them a Guid written without a lock).
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayConnectionLoginTests
    {
        private readonly InProcessTestCluster cluster;

        public GatewayConnectionLoginTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        [Fact]
        public async Task ReadersSeeOneWholeLoginAtATime()
        {
            var transport = new WebSocketConnection(new StalledWebSocket(), WebSocketConnection.NextId(), 1024, 16, 1024 * 1024,
                new TestLogSink().CreateLogger("transport"));
            var connection = new GatewayConnection(transport, new TokenBucket(5, TimeSpan.FromSeconds(10), TimeProvider.System), 8, 1024 * 1024);
            var a = (Player: cluster.Client.GetGrain<IPlayer>("login-a"), Game: new Guid(Enumerable.Repeat((byte)0x11, 16).ToArray()), Ticket: Guid.NewGuid());
            var b = (Player: cluster.Client.GetGrain<IPlayer>("login-b"), Game: new Guid(Enumerable.Repeat((byte)0x22, 16).ToArray()), Ticket: Guid.NewGuid());
            Assert.Null(connection.Login);
            Assert.False(connection.IsLoggedIn);
            Assert.Equal(Guid.Empty, connection.GameKey);

            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var writer = Task.Run(() =>
            {
                for (int i = 0; !stop.IsCancellationRequested; ++i)
                {
                    var next = (i & 1) == 0 ? a : b;
                    if (i % 3 == 0)
                        connection.ClearLogin();
                    else
                        connection.SetLogin(next.Player, next.Game, next.Ticket);
                }
            });
            long reads = 0;
            while (!stop.IsCancellationRequested)
            {
                var login = connection.Login;
                reads++;
                if (login == null)
                    continue;
                var expected = login.Player.GetPrimaryKeyString() == "login-a" ? a : b;
                Assert.Equal(expected.Game, login.GameKey);
                Assert.Equal(expected.Ticket, login.LoginTicket);
            }
            await writer;
            Assert.True(reads > 0);

            connection.SetLogin(a.Player, a.Game, a.Ticket);
            Assert.True(connection.IsLoggedIn);
            Assert.Equal("login-a", connection.PlayerId);
            Assert.Equal(a.Game, connection.GameKey);
            Assert.Equal(a.Ticket, connection.LoginTicket);
            connection.ClearLogin();
            Assert.Null(connection.Player);
            Assert.Null(connection.PlayerId);
            Assert.Equal(Guid.Empty, connection.LoginTicket);
            transport.Abort();
        }
    }

    /// <summary>The byte count behind the queue budgets.</summary>
    public class ByteBudgetTests
    {
        [Fact]
        public void TakesWhatFitsAndOneFrameIntoAnEmptyBudget()
        {
            var budget = new ByteBudget(100);
            Assert.True(budget.TryTake(60));
            Assert.True(budget.TryTake(40));
            Assert.False(budget.TryTake(1));
            budget.Release(60);
            Assert.False(budget.TryTake(61));
            Assert.True(budget.TryTake(60));
            budget.Release(100);
            Assert.Equal(0, budget.Used);

            // A frame bigger than the whole budget still fits when nothing is held.
            Assert.True(budget.TryTake(500));
            Assert.False(budget.TryTake(1));
            budget.Release(500);
            Assert.True(budget.TryTake(1));
        }

        [Fact]
        public async Task TakeAsyncWaitsForARelease()
        {
            var budget = new ByteBudget(100);
            Assert.True(budget.TryTake(80));
            var waiting = budget.TakeAsync(50, CancellationToken.None);
            await Task.Delay(100);
            Assert.False(waiting.IsCompleted);

            budget.Release(20);
            await Task.Delay(100);
            Assert.False(waiting.IsCompleted); // 60 + 50 is still over 100

            budget.Release(60);
            await waiting.WaitAsync(TestSetup.Timeout);
            Assert.Equal(50, budget.Used);
        }

        [Fact]
        public async Task TakeAsyncGivesUpWhenCancelled()
        {
            var budget = new ByteBudget(10);
            budget.TryTake(10);
            using var cancel = new CancellationTokenSource();
            var waiting = budget.TakeAsync(5, cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(10, budget.Used);
        }
    }
}
