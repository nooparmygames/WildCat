using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Gateway;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The real WebSocket gateway on Kestrel at a free loopback port, mapped at /ws like the host
    /// does, using the test cluster's client in place of the silo's. Everything the gateway logs is
    /// captured in <see cref="Logs"/>.
    /// </summary>
    internal sealed class GatewayTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private GatewayTestHost(WebApplication app, Uri webSocketUri, TestLogSink logs)
        {
            this.app = app;
            WebSocketUri = webSocketUri;
            Logs = logs;
        }

        public Uri WebSocketUri { get; }

        public TestLogSink Logs { get; }

        public IServiceProvider Services => app.Services;

        public ConnectionRegistry Connections => app.Services.GetRequiredService<ConnectionRegistry>();

        public HandlerRegistry Handlers => app.Services.GetRequiredService<HandlerRegistry>();

        /// <param name="client">The cluster client the gateway calls grains with.</param>
        /// <param name="configure">Changes to the gateway options.</param>
        /// <param name="timeProvider">A clock to use in place of the system clock (sign-in limit).</param>
        /// <param name="configureServices">More services, for example extra <see cref="IMessageHandler"/> instances.</param>
        public static async Task<GatewayTestHost> StartAsync(IClusterClient client,
            Action<GameGatewayOptions> configure = null, TimeProvider timeProvider = null,
            Action<IServiceCollection> configureServices = null)
        {
            var logs = new TestLogSink();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton(client);
            if (timeProvider != null)
                builder.Services.AddSingleton(timeProvider);
            builder.Services.AddGameGateway();
            if (configure != null)
                builder.Services.Configure(configure);
            configureServices?.Invoke(builder.Services);

            var app = builder.Build();
            app.UseWebSockets();
            app.MapGameGateway("/ws");
            await app.StartAsync().WaitAsync(TestSetup.Timeout);

            var address = new Uri(app.Urls.First());
            return new GatewayTestHost(app, new Uri($"ws://{address.Host}:{address.Port}/ws"), logs);
        }

        public Task<GatewayTestClient> ConnectAsync(string origin = null)
        {
            return GatewayTestClient.ConnectAsync(WebSocketUri, origin);
        }

        /// <summary>A bare socket that reads nothing unless the test does, for transport tests.</summary>
        public async Task<ClientWebSocket> ConnectRawAsync()
        {
            var socket = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await socket.ConnectAsync(WebSocketUri, timeout.Token);
            return socket;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().WaitAsync(TestSetup.Timeout);
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// A WebSocket client speaking the wire protocol through <see cref="WireCodec"/>. A background
    /// loop reads every frame; tests wait for the frames they expect with a bounded wait and can
    /// look at everything received.
    /// </summary>
    internal sealed class GatewayTestClient : IAsyncDisposable
    {
        /// <summary>How long a test waits for one expected frame.</summary>
        public static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(15);

        private readonly ClientWebSocket socket;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly Channel<WildCatMessageBase> incoming = Channel.CreateUnbounded<WildCatMessageBase>();
        private readonly List<WildCatMessageBase> unclaimed = new List<WildCatMessageBase>();
        private readonly List<WildCatMessageBase> received = new List<WildCatMessageBase>();
        private readonly Task receiveLoop;
        private int lastRequestId;

        private GatewayTestClient(ClientWebSocket socket)
        {
            this.socket = socket;
            receiveLoop = Task.Run(ReceiveLoop);
        }

        public static async Task<GatewayTestClient> ConnectAsync(Uri uri, string origin = null)
        {
            var socket = new ClientWebSocket();
            if (origin != null)
                socket.Options.SetRequestHeader("Origin", origin);
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await socket.ConnectAsync(uri, timeout.Token);
            return new GatewayTestClient(socket);
        }

        /// <summary>Every frame received so far, in arrival order.</summary>
        public IReadOnlyList<WildCatMessageBase> Received
        {
            get
            {
                lock (received)
                    return received.ToList();
            }
        }

        public WebSocketState State => socket.State;

        /// <summary>How the server closed the socket, once it has.</summary>
        public WebSocketCloseStatus? CloseStatus => socket.CloseStatus;

        /// <summary>Completes when the server has closed the socket or it broke.</summary>
        public Task Closed => receiveLoop;

        public uint NextRequestId()
        {
            return (uint)Interlocked.Increment(ref lastRequestId);
        }

        public Task SendAsync(WildCatMessageBase message)
        {
            return SendRawAsync(WireCodec.Encode(message));
        }

        public async Task SendRawAsync(byte[] frame)
        {
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await sendLock.WaitAsync(timeout.Token);
            try
            {
                await socket.SendAsync(frame, WebSocketMessageType.Binary, true, timeout.Token);
            }
            finally
            {
                sendLock.Release();
            }
        }

        /// <summary>Sends one binary message split into WebSocket frames of at most <paramref name="fragmentSize"/> bytes.</summary>
        public async Task SendFragmentedAsync(byte[] message, int fragmentSize)
        {
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await sendLock.WaitAsync(timeout.Token);
            try
            {
                for (int offset = 0; offset < message.Length; offset += fragmentSize)
                {
                    int count = Math.Min(fragmentSize, message.Length - offset);
                    await socket.SendAsync(new ArraySegment<byte>(message, offset, count), WebSocketMessageType.Binary,
                        offset + count >= message.Length, timeout.Token);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        public async Task SendTextAsync(string text)
        {
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await sendLock.WaitAsync(timeout.Token);
            try
            {
                await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, timeout.Token);
            }
            finally
            {
                sendLock.Release();
            }
        }

        /// <summary>
        /// A request frame with the next request id, and with the ticket in the header when one is
        /// given.
        /// </summary>
        public WildCatMessageBase Request<T>(WildCatMsgType type, T payload, string ticket = null) where T : MessageBase
        {
            return ticket == null
                ? WildCatMessageBase.CreateWithRequestID(payload, type, WildCatMsgHeader.normal, NextRequestId())
                : WildCatMessageBase.CreateWithAuthDataAndRequestID(payload, type, WildCatMsgHeader.normal, ticket, NextRequestId());
        }

        /// <summary>Sends a request that carries a request id and waits for the frame echoing it.</summary>
        public async Task<WildCatMessageBase> RequestAsync(WildCatMessageBase request)
        {
            if (!request.hasRequestID)
                throw new ArgumentException("The request needs a request id.", nameof(request));
            await SendAsync(request);
            return await WaitForAsync(m => m.hasRequestID && m.requestID == request.requestID,
                $"a reply to request {request.requestID} ({request.msgType})");
        }

        /// <summary>
        /// Sends a request and returns the decoded answer, checking that it came back as
        /// <paramref name="responseType"/> with only the request id in its header.
        /// </summary>
        public async Task<OptionalResponse<T>> CallAsync<T>(WildCatMsgType type, MessageBase payload,
            WildCatMsgType responseType, string ticket = null) where T : MessageBase, new()
        {
            var reply = await RequestAsync(Request(type, payload, ticket));
            Assert.Equal(responseType, reply.msgType);
            Assert.Equal(WildCatMsgHeader.HasRequestID, reply.header);
            return reply.GetPayload<OptionalResponse<T>>();
        }

        /// <summary>
        /// Returns the first frame, received earlier or arriving within <paramref name="timeout"/>,
        /// that matches. Frames that do not match stay available to later waits.
        /// </summary>
        public async Task<WildCatMessageBase> WaitForAsync(Func<WildCatMessageBase, bool> match, string description,
            TimeSpan? timeout = null)
        {
            var found = unclaimed.FirstOrDefault(match);
            if (found != null)
            {
                unclaimed.Remove(found);
                return found;
            }

            using var cancel = new CancellationTokenSource(timeout ?? FrameTimeout);
            try
            {
                while (true)
                {
                    var message = await incoming.Reader.ReadAsync(cancel.Token);
                    if (match(message))
                        return message;
                    unclaimed.Add(message);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ChannelClosedException)
            {
                string seen = string.Join(", ", Received.Select(m => $"{m.msgType}#{(m.hasRequestID ? m.requestID : 0)}"));
                throw new TimeoutException($"No {description} arrived (socket {socket.State}). Received so far: [{seen}]");
            }
        }

        /// <summary>Waits for a frame of <paramref name="type"/>, such as a pushed event.</summary>
        public Task<WildCatMessageBase> WaitForTypeAsync(WildCatMsgType type, TimeSpan? timeout = null)
        {
            return WaitForAsync(m => m.msgType == type, $"a {type} frame", timeout);
        }

        public async Task<Guid> InitializeSessionAsync(Guid developerKey, Guid gameKey)
        {
            var response = await CallAsync<GuidMessage>(WildCatMsgType.InitializeSessionRequest,
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey },
                WildCatMsgType.InitializeSessionResponse);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg.value;
        }

        public WildCatMessageBase AuthenticateRequest(Guid session, string deviceId)
        {
            return Request(WildCatMsgType.AuthenticateRequest, new AuthenticationRequest
            {
                ticket = session.ToString(), LoginType = LoginType.Device, Id = deviceId, AutoCreate = true
            });
        }

        /// <summary>Logs in with a device id, creating the account on first use.</summary>
        public async Task<AuthenticationResponse> LoginWithDeviceAsync(Guid session, string deviceId)
        {
            var reply = await RequestAsync(AuthenticateRequest(session, deviceId));
            Assert.Equal(WildCatMsgType.AuthenticateResponse, reply.msgType);
            var response = reply.GetPayload<OptionalResponse<AuthenticationResponse>>();
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg;
        }

        /// <summary>Asks for the logged in player's name (36), a call that needs the session ticket.</summary>
        public Task<WildCatMessageBase> GetNameAsync(string ticket)
        {
            return RequestAsync(WildCatMessageBase.CreateWithAuthDataAndRequestID(new EmptyMessage(),
                WildCatMsgType.GetNameRequest, WildCatMsgHeader.normal, ticket, NextRequestId()));
        }

        /// <summary>Drops the connection without a close handshake, like a killed app.</summary>
        public void Abort()
        {
            socket.Abort();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    using var frame = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;
                        frame.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var message = WireCodec.Decode(frame.ToArray());
                    lock (received)
                        received.Add(message);
                    incoming.Writer.TryWrite(message);
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
            }
            finally
            {
                incoming.Writer.TryComplete();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
                }
                catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException)
                {
                    socket.Abort();
                }
            }
            await receiveLoop.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
            socket.Dispose();
        }
    }

    /// <summary>Collects every log entry of a test host.</summary>
    internal sealed class TestLogSink : ILoggerProvider
    {
        private readonly ConcurrentQueue<Entry> entries = new ConcurrentQueue<Entry>();

        public IReadOnlyList<Entry> Entries => entries.ToList();

        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(this, categoryName);
        }

        public void Dispose()
        {
        }

        internal sealed record Entry(string Category, LogLevel Level, string Message, Exception Exception);

        private sealed class Logger : ILogger
        {
            private readonly TestLogSink sink;
            private readonly string category;

            public Logger(TestLogSink sink, string category)
            {
                this.sink = sink;
                this.category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                sink.entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
