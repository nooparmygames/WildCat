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
    /// does, using the test cluster's client in place of the silo's.
    /// </summary>
    internal sealed class GatewayTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private GatewayTestHost(WebApplication app, Uri webSocketUri)
        {
            this.app = app;
            WebSocketUri = webSocketUri;
        }

        public Uri WebSocketUri { get; }

        public FrontEndServer FrontEnd => (FrontEndServer)app.Services.GetRequiredService<IFrontEndServer>();

        public static async Task<GatewayTestHost> StartAsync(IClusterClient client,
            Action<GameGatewayOptions> configure = null, TimeProvider timeProvider = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(client);
            if (timeProvider != null)
                builder.Services.AddSingleton(timeProvider);
            builder.Services.AddGameGateway();
            if (configure != null)
                builder.Services.Configure(configure);

            var app = builder.Build();
            app.UseWebSockets();
            app.MapGameGateway("/ws");
            await app.StartAsync().WaitAsync(TestSetup.Timeout);

            var address = new Uri(app.Urls.First());
            return new GatewayTestHost(app, new Uri($"ws://{address.Host}:{address.Port}/ws"));
        }

        public Task<GatewayTestClient> ConnectAsync(string origin = null)
        {
            return GatewayTestClient.ConnectAsync(WebSocketUri, origin);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().WaitAsync(TestSetup.Timeout);
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// A WebSocket client speaking the wire protocol. A background loop reads every frame; tests
    /// wait for the frames they expect with a bounded wait and can look at everything received.
    /// </summary>
    internal sealed class GatewayTestClient : IAsyncDisposable
    {
        /// <summary>How long a test waits for one expected frame.</summary>
        public static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(15);

        private readonly ClientWebSocket socket;
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

        public uint NextRequestId()
        {
            return (uint)Interlocked.Increment(ref lastRequestId);
        }

        public async Task SendAsync(WildCatMessageBase message)
        {
            using var timeout = new CancellationTokenSource(TestSetup.Timeout);
            await socket.SendAsync(WireCodec.Encode(message), WebSocketMessageType.Binary, true, timeout.Token);
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

        public async Task<Guid> InitializeSessionAsync(Guid developerKey, Guid gameKey)
        {
            var reply = await RequestAsync(WildCatMessageBase.CreateWithRequestID(
                new InitializeSessionRequest { DeveloperKey = developerKey, GameKey = gameKey },
                WildCatMsgType.InitializeSessionRequest, WildCatMsgHeader.normal, NextRequestId()));
            Assert.Equal(WildCatMsgType.InitializeSessionResponse, reply.msgType);
            var response = reply.GetPayload<OptionalResponse<GuidMessage>>();
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg.value;
        }

        public WildCatMessageBase AuthenticateRequest(Guid session, string deviceId)
        {
            return WildCatMessageBase.CreateWithRequestID(
                new AuthenticationRequest
                {
                    ticket = session.ToString(), LoginType = LoginType.Device, Id = deviceId, AutoCreate = true
                },
                WildCatMsgType.AuthenticateRequest, WildCatMsgHeader.normal, NextRequestId());
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
}
