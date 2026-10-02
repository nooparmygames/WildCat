using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// An in-memory stand-in for the gateway, for tests of the connection's own logic (framing,
    /// correlation, timeouts, heartbeats, reconnection) that need exact control over the answers.
    /// Every connection attempt gets a new <see cref="FakeTransport"/>; <see cref="Respond"/>
    /// decides the answers to each frame the client sends.
    /// </summary>
    public sealed class FakeServer : ITransportFactory
    {
        private readonly List<FakeTransport> transports = new List<FakeTransport>();

        /// <summary>Answers to a frame the client sent; null or empty sends nothing back.</summary>
        public Func<WildCatMessageBase, IEnumerable<WildCatMessageBase>> Respond { get; set; }

        /// <summary>While true, connection attempts fail.</summary>
        public bool RefuseConnections { get; set; }

        /// <summary>
        /// The largest message the fake gateway takes; a bigger one breaks the socket, as the real
        /// gateway closes it with MessageTooBig (Gateway:MaxMessageBytes, 1 MiB by default).
        /// </summary>
        public int MaxMessageBytes { get; set; } = 1024 * 1024;

        private TaskCompletionSource<bool> sendGate;
        private int sendsHeld;

        /// <summary>Sends that are waiting in <see cref="HoldSends"/> right now.</summary>
        public int SendsHeld
        {
            get { return Volatile.Read(ref sendsHeld); }
        }

        /// <summary>Makes every send wait until <see cref="ReleaseSends"/>, like a socket whose peer stopped reading.</summary>
        public void HoldSends()
        {
            Interlocked.CompareExchange(ref sendGate, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), null);
        }

        public void ReleaseSends()
        {
            Interlocked.Exchange(ref sendGate, null)?.TrySetResult(true);
        }

        internal async Task WaitIfHeldAsync()
        {
            TaskCompletionSource<bool> gate = Volatile.Read(ref sendGate);
            if (gate == null)
                return;
            Interlocked.Increment(ref sendsHeld);
            try
            {
                await gate.Task;
            }
            finally
            {
                Interlocked.Decrement(ref sendsHeld);
            }
        }

        public IReadOnlyList<FakeTransport> Transports
        {
            get
            {
                lock (transports)
                    return transports.ToList();
            }
        }

        public FakeTransport Current
        {
            get
            {
                lock (transports)
                    return transports.LastOrDefault();
            }
        }

        /// <summary>Every frame the client sent, over all connections.</summary>
        public IReadOnlyList<WildCatMessageBase> Received
        {
            get { return Transports.SelectMany(t => t.Received).ToList(); }
        }

        public ITransport Create(WildCatClientOptions options)
        {
            var transport = new FakeTransport(this);
            lock (transports)
                transports.Add(transport);
            return transport;
        }

        /// <summary>An answer frame to <paramref name="request"/>, as the gateway builds it.</summary>
        public static WildCatMessageBase Answer<T>(WildCatMessageBase request, WildCatMsgType type, T payload) where T : MessageBase, new()
        {
            return WildCatMessageBase.CreateResponse(OptionalResponse<T>.CreateSuccessful(payload), type, request);
        }

        public static WildCatMessageBase Failure(WildCatMessageBase request, WildCatMsgType type, int code, string text)
        {
            return WildCatMessageBase.CreateResponse(OptionalResponse<EmptyMessage>.CreateFailed(text, code), type, request);
        }

        /// <summary>
        /// A responder that plays a small gateway: sessions, device and user name logins, GetName,
        /// server time and heartbeats. Everything else goes unanswered.
        /// </summary>
        public static IEnumerable<WildCatMessageBase> Basics(WildCatMessageBase frame)
        {
            switch (frame.msgType)
            {
                case WildCatMsgType.InitializeSessionRequest:
                    return new[] { Answer(frame, WildCatMsgType.InitializeSessionResponse, new GuidMessage { value = Guid.NewGuid() }) };
                case WildCatMsgType.AuthenticateRequest:
                    var login = frame.GetPayload<AuthenticationRequest>();
                    return new[]
                    {
                        Answer(frame, WildCatMsgType.AuthenticateResponse,
                            new AuthenticationResponse { userId = "player-" + login.Id, name = "", ticket = login.ticket })
                    };
                case WildCatMsgType.GetNameRequest:
                    return new[] { Answer(frame, WildCatMsgType.GetNameResponse, new StringMessage { value = "name" }) };
                case WildCatMsgType.GetServerTimeRequest:
                    return new[] { Answer(frame, WildCatMsgType.GetServerTimeResponse, new DateTimeMessage { value = DateTime.UtcNow }) };
                case WildCatMsgType.HeartBeatRequest:
                    return new[] { Answer(frame, WildCatMsgType.HeartBeatResponse, new EmptyMessage()) };
                default:
                    return Array.Empty<WildCatMessageBase>();
            }
        }

        internal IEnumerable<WildCatMessageBase> AnswersTo(WildCatMessageBase frame)
        {
            return Respond?.Invoke(frame) ?? Array.Empty<WildCatMessageBase>();
        }
    }

    /// <summary>One fake socket. Frames sent by the client are decoded and answered at once.</summary>
    public sealed class FakeTransport : ITransport
    {
        private readonly FakeServer server;
        private readonly Channel<byte[]> inbound = Channel.CreateUnbounded<byte[]>();
        private readonly ConcurrentQueue<WildCatMessageBase> received = new ConcurrentQueue<WildCatMessageBase>();
        private int sending;

        public FakeTransport(FakeServer server)
        {
            this.server = server;
        }

        public IReadOnlyList<WildCatMessageBase> Received
        {
            get { return received.ToList(); }
        }

        /// <summary>Set when two sends overlapped, which a WebSocket does not allow.</summary>
        public bool ConcurrentSendSeen { get; private set; }

        public bool CloseSent { get; private set; }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (server.RefuseConnections)
                return Task.FromException(new WebSocketException("The fake server refused the connection."));
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref sending) > 1)
                ConcurrentSendSeen = true;
            try
            {
                await Task.Yield();
                await server.WaitIfHeldAsync();
                if (message.Length > server.MaxMessageBytes)
                {
                    Break();
                    return;
                }
                WildCatMessageBase frame = WireCodec.Decode(message);
                received.Enqueue(frame);
                foreach (WildCatMessageBase answer in server.AnswersTo(frame))
                    Push(answer);
            }
            finally
            {
                Interlocked.Decrement(ref sending);
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await inbound.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException ex)
            {
                if (ex.InnerException != null)
                    throw ex.InnerException;
                return null;
            }
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            // The fake server answers a close at once.
            CloseSent = true;
            inbound.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public void Abort()
        {
            inbound.Writer.TryComplete(new WebSocketException("The socket was aborted."));
        }

        public void Dispose()
        {
            inbound.Writer.TryComplete(new ObjectDisposedException(nameof(FakeTransport)));
        }

        /// <summary>Sends a frame to the client.</summary>
        public void Push(WildCatMessageBase frame)
        {
            inbound.Writer.TryWrite(WireCodec.Encode(frame));
        }

        public void PushRaw(byte[] bytes)
        {
            inbound.Writer.TryWrite(bytes);
        }

        /// <summary>The server closes the socket with a handshake.</summary>
        public void CloseFromServer()
        {
            inbound.Writer.TryComplete();
        }

        /// <summary>The socket breaks.</summary>
        public void Break()
        {
            inbound.Writer.TryComplete(new WebSocketException("The fake network broke."));
        }
    }
}
