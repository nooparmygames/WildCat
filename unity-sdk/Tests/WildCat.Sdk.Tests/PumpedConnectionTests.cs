using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Sdk.Tests;

public sealed class PumpedConnectionTests
{
    [Fact]
    public void IdleWriterSendsAndReceivesOnThePumpingThread()
    {
        using var loop = new SingleThreadLoop();
        var factory = new LocalFactory();
        using var connection = loop.Connection(factory);
        Assert.True(connection.ConnectAsync().IsCompletedSuccessfully);
        var request = Request(connection);
        var sent = Assert.Single(factory.Current.Sent);
        Assert.False(request.IsCompleted);
        factory.Current.Deliver(FakeServer.Answer(sent, WildCatMsgType.GetServerTimeResponse,
            new DateTimeMessage { value = DateTime.UtcNow }));
        loop.Pump();
        Assert.True(request.IsCompletedSuccessfully);
        Assert.Equal(loop.ThreadId, factory.Current.SendThread);
    }

    [Fact]
    public void RequestTimeoutAndExplicitCancellationNeedNoWorkerThread()
    {
        using var loop = new SingleThreadLoop();
        var factory = new LocalFactory();
        using var connection = loop.Connection(factory);
        Assert.True(connection.ConnectAsync().IsCompletedSuccessfully);
        var request = Request(connection);
        loop.Advance(2);
        Assert.True(request.IsFaulted);
        Assert.IsType<TimeoutException>(request.Exception.InnerException);
        using var cancellation = new CancellationTokenSource();
        var cancelled = Request(connection, cancellation.Token);
        cancellation.Cancel();
        loop.Pump();
        Assert.True(cancelled.IsCanceled);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public void ConnectTimeoutFiresWithoutManagedTimers()
    {
        using var loop = new SingleThreadLoop();
        var factory = new LocalFactory { StallConnect = true };
        using var connection = loop.Connection(factory);
        var connect = connection.ConnectAsync();
        Assert.False(connect.IsCompleted);
        loop.Advance(2);
        Assert.True(connect.IsFaulted);
        var failure = Assert.IsType<WildCatDisconnectedException>(connect.Exception.InnerException);
        Assert.IsType<TimeoutException>(failure.InnerException);
    }

    [Fact]
    public void HeartbeatTimeoutAndReconnectBackoffUseThePumpedClock()
    {
        using var loop = new SingleThreadLoop();
        var factory = new LocalFactory();
        using var connection = loop.Connection(factory, heartbeat: true);
        Assert.True(connection.ConnectAsync().IsCompletedSuccessfully);
        loop.Advance(1);
        Assert.Equal(WildCatMsgType.GetServerTimeRequest, Assert.Single(factory.Current.Sent).msgType);
        loop.Advance(2);
        Assert.Equal(ConnectionState.Reconnecting, connection.State);
        Assert.Single(factory.Transports);
        loop.Advance(1);
        Assert.Equal(2, factory.Transports.Count);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public void CloseHandshakeDeadlineFinishesEvenWhenThePeerNeverCloses()
    {
        using var loop = new SingleThreadLoop();
        var factory = new LocalFactory();
        using var connection = loop.Connection(factory);
        Assert.True(connection.ConnectAsync().IsCompletedSuccessfully);
        var close = connection.DisconnectAsync();
        Assert.False(close.IsCompleted);
        loop.Advance(3);
        Assert.True(close.IsCompletedSuccessfully);
        Assert.Equal(ConnectionState.Disconnected, connection.State);
    }

    private static Task<DateTimeMessage> Request(WildCatConnection connection, CancellationToken token = default)
    {
        return connection.RequestAsync<DateTimeMessage>(WildCatMsgType.GetServerTimeRequest, new EmptyMessage(),
            WildCatMsgType.GetServerTimeResponse, new RequestOptions { SendTicket = false }, token);
    }

    private sealed class SingleThreadLoop : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext previous = Current;
        private readonly Queue<Action> callbacks = new();
        private TimeSpan now;
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        private readonly PumpedConnectionScheduler scheduler;

        internal SingleThreadLoop()
        {
            SetSynchronizationContext(this);
            scheduler = new PumpedConnectionScheduler(() => now);
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            Assert.Equal(ThreadId, Environment.CurrentManagedThreadId);
            callbacks.Enqueue(() => callback(state));
        }

        internal WildCatConnection Connection(LocalFactory factory, bool heartbeat = false)
        {
            return new WildCatConnection(new WildCatClientOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                RequestTimeout = TimeSpan.FromSeconds(2),
                HeartbeatInterval = TimeSpan.FromSeconds(heartbeat ? 1 : 0),
                HeartbeatTimeout = TimeSpan.FromSeconds(2),
                ReconnectMinDelay = TimeSpan.FromSeconds(1),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
                ReconnectJitter = 0,
            }, transportFactory: factory, scheduler: scheduler);
        }

        internal void Advance(double seconds) { now += TimeSpan.FromSeconds(seconds); Pump(); }
        internal void Pump()
        {
            scheduler.Pump();
            int remaining = 100;
            while (callbacks.Count > 0)
            {
                Assert.True(--remaining > 0, "The main loop did not settle within 100 callbacks.");
                callbacks.Dequeue()();
            }
        }

        public void Dispose() { SetSynchronizationContext(previous); }
    }

    private sealed class LocalFactory : ITransportFactory
    {
        internal bool StallConnect;
        internal readonly List<LocalTransport> Transports = new();
        internal LocalTransport Current => Transports[Transports.Count - 1];
        public ITransport Create(WildCatClientOptions options)
        {
            var transport = new LocalTransport(StallConnect);
            Transports.Add(transport);
            return transport;
        }
    }

    // Delivers like browser callbacks: no Channels, Task.Run, managed timers or worker threads.
    private sealed class LocalTransport : ITransport
    {
        private readonly bool stallConnect;
        private readonly TaskCompletionSource<bool> opening = new();
        private CancellationTokenRegistration openingRegistration;
        private CancellationTokenRegistration receiveRegistration;
        private TaskCompletionSource<byte[]> receiving;
        internal readonly List<WildCatMessageBase> Sent = new();
        internal int SendThread;
        internal LocalTransport(bool stallConnect) { this.stallConnect = stallConnect; }
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (!stallConnect) return Task.CompletedTask;
            openingRegistration = cancellationToken.Register(() => opening.TrySetCanceled(cancellationToken));
            return opening.Task;
        }
        public Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            SendThread = Environment.CurrentManagedThreadId;
            Sent.Add(WireCodec.Decode(message));
            return Task.CompletedTask;
        }
        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            var pending = new TaskCompletionSource<byte[]>();
            receiving = pending;
            receiveRegistration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return pending.Task;
        }
        internal void Deliver(WildCatMessageBase message)
        {
            var pending = receiving;
            receiving = null;
            receiveRegistration.Dispose();
            pending.TrySetResult(WireCodec.Encode(message));
        }
        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Abort() { receiving?.TrySetException(new InvalidOperationException("Socket dropped.")); }
        public void Dispose() { openingRegistration.Dispose(); receiveRegistration.Dispose(); }
    }
}
