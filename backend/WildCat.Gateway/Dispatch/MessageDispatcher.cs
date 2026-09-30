using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Serves accepted sockets. For every frame it decides, in arrival order: undecodable frames
    /// and unknown ids get an ErrorMsg, game messages (ids above HighestInternal) go to the
    /// connection's relay queue, which one worker drains in order, and requests are checked
    /// against the sign-in limit and then run concurrently, at most
    /// <see cref="GameGatewayOptions.MaxConcurrentRequests"/> at a time per connection. Before a
    /// handler runs, the dispatcher checks the session its route asks for; after, it turns
    /// exceptions into error answers that echo the request id.
    /// </summary>
    public sealed class MessageDispatcher
    {
        private static readonly TimeSpan HeartBeatPingInterval = TimeSpan.FromSeconds(5);

        private readonly HandlerRegistry handlers;
        private readonly ConnectionRegistry connections;
        private readonly LoginManager logins;
        private readonly IClusterClient grains;
        private readonly GameGatewayOptions options;
        private readonly TimeProvider timeProvider;
        private readonly ILogger<MessageDispatcher> logger;
        private readonly ILogger<WebSocketConnection> transportLogger;

        public MessageDispatcher(HandlerRegistry handlers, ConnectionRegistry connections, LoginManager logins,
            IClusterClient grains, IOptions<GameGatewayOptions> options, TimeProvider timeProvider,
            ILogger<MessageDispatcher> logger, ILogger<WebSocketConnection> transportLogger)
        {
            this.handlers = handlers;
            this.connections = connections;
            this.logins = logins;
            this.grains = grains;
            this.options = options.Value;
            this.timeProvider = timeProvider;
            this.logger = logger;
            this.transportLogger = transportLogger;
        }

        /// <summary>
        /// Serves one accepted WebSocket until it closes, then releases its login. Returns when the
        /// socket is closed.
        /// </summary>
        public async Task RunAsync(WebSocket socket, CancellationToken aborted)
        {
            var transport = new WebSocketConnection(socket, WebSocketConnection.NextId(), options.MaxMessageBytes,
                options.OutboundQueueCapacity, transportLogger);
            var connection = new GatewayConnection(transport,
                new TokenBucket(options.AuthAttemptBurst, TimeSpan.FromMinutes(1) / options.AuthAttemptsPerMinute, timeProvider),
                options.MaxConcurrentRequests);
            connections.Add(connection);
            logger.LogDebug("Connection {ConnectionId} opened", connection.Id);
            Task relayWorker = Task.Run(() => RelayLoopAsync(connection));
            try
            {
                await transport.RunAsync(frame => OnFrameAsync(connection, frame), aborted);
            }
            finally
            {
                // Frames already queued for relay are still sent on; nothing new arrives.
                connection.RelayQueue.Writer.TryComplete();
                await logins.ReleaseAsync(connection);
            }
        }

        /// <summary>Called by the receive loop for each frame, in order. Waits only for queue space.</summary>
        internal async Task OnFrameAsync(GatewayConnection connection, byte[] frame)
        {
            WildCatMessageBase message;
            try
            {
                message = WireCodec.Decode(frame);
            }
            catch (WireFormatException ex)
            {
                logger.LogInformation("Connection {ConnectionId} sent a frame that could not be read: {Reason}", connection.Id, ex.Message);
                SendError(connection, null, GatewayErrors.Refused, GatewayErrors.MalformedMessage);
                return;
            }

            if (message.msgType > WildCatMsgType.HighestInternal)
            {
                try
                {
                    // Waits when the relay worker is behind, which stops reading the socket: the
                    // client is sending faster than its messages can be passed on.
                    await connection.RelayQueue.Writer.WriteAsync(message, connection.Closing);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ChannelClosedException)
                {
                }
                return;
            }

            if (!handlers.TryGet(message.msgType, out MessageRoute route, out IMessageHandler handler))
            {
                // Ids up to HighestInternal are the server's own: requests nobody serves and every
                // server event and response. They are never relayed, so a player cannot forge
                // server events or responses to the rest of a match.
                logger.LogInformation("Refused message type {MessageType} from connection {ConnectionId}: no handler",
                    message.msgType, connection.Id);
                SendError(connection, message, GatewayErrors.Refused, GatewayErrors.UnsupportedMessageType);
                return;
            }

            // Sign-in requests check a password or a code, so each connection gets a small
            // allowance of them. Decided here, in arrival order, before any grain call.
            if (route.IsSignInAttempt && !connection.AuthAttempts.TryTake())
            {
                logger.LogInformation("Refused {MessageType} from connection {ConnectionId}: too many sign-in attempts",
                    message.msgType, connection.Id);
                SendFailure(connection, message, route.ResponseType, GatewayErrors.Refused, GatewayErrors.TooManyAuthAttempts);
                return;
            }

            try
            {
                await connection.RequestSlots.WaitAsync(connection.Closing);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _ = ProcessRequestAsync(connection, message, route, handler);
        }

        /// <summary>Runs one request to the end. Never throws; releases the request slot.</summary>
        private async Task ProcessRequestAsync(GatewayConnection connection, WildCatMessageBase message, MessageRoute route,
            IMessageHandler handler)
        {
            try
            {
                // Let the receive loop go on to the next frame straight away.
                await Task.Yield();
                MessageContext context = await CreateContextAsync(connection, message, route);
                if (context != null)
                    await handler.HandleAsync(context);
            }
            catch (Exception ex)
            {
                AnswerException(connection, message, route, ex);
            }
            finally
            {
                connection.RequestSlots.Release();
            }
        }

        /// <summary>
        /// Checks what the route requires. Returns the context, or null after answering the
        /// request with an ErrorMsg.
        /// </summary>
        private async Task<MessageContext> CreateContextAsync(GatewayConnection connection, WildCatMessageBase message,
            MessageRoute route)
        {
            if (route.Session == SessionRequirement.None)
                return new MessageContext(connection, message, route, grains, logger, null, Guid.Empty, null);

            var (ticket, session) = await CheckSessionAsync(connection, message, route.Session == SessionRequirement.Player);
            if (session == null)
                return null;
            IPlayer player = null;
            if (!string.IsNullOrEmpty(session.UserID))
            {
                player = grains.GetGrain<IPlayer>(session.UserID);
                PingHeartBeat(connection, player);
            }
            return new MessageContext(connection, message, route, grains, logger, session, ticket, player);
        }

        /// <summary>
        /// Checks the session ticket in the frame header. Returns the ticket and the session, or
        /// answers with an ErrorMsg and returns a null session. A missing, malformed, unknown,
        /// ended or expired ticket, and a session no player has logged in on when one is needed,
        /// are client errors logged at Information without a stack trace; anything else is logged
        /// as an error.
        /// </summary>
        private async Task<(Guid Ticket, SessionGameInfo Session)> CheckSessionAsync(GatewayConnection connection,
            WildCatMessageBase message, bool requirePlayer)
        {
            string refusal;
            if (!message.hasAuthData)
            {
                refusal = GatewayErrors.SessionRequired;
            }
            else if (!Guid.TryParse(message.ticket, out Guid ticket))
            {
                refusal = GatewayErrors.InvalidTicket;
            }
            else
            {
                try
                {
                    var session = await grains.GetGrain<ISession>(ticket).TouchAndGetInfo();
                    if (!requirePlayer || !string.IsNullOrEmpty(session.UserID))
                        return (ticket, session);
                    refusal = GatewayErrors.NotLoggedIn;
                }
                catch (Exception ex) when (ex is SessionNotInitializedException || ex is SessionExpiredException)
                {
                    refusal = ex.Message;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Checking the session of {MessageType} from connection {ConnectionId} failed",
                        message.msgType, connection.Id);
                    SendError(connection, message, GatewayErrors.Internal, GatewayErrors.SessionCheckFailed);
                    return (Guid.Empty, null);
                }
            }

            logger.LogInformation("Refused {MessageType} from connection {ConnectionId}: {Reason}",
                message.msgType, connection.Id, refusal);
            SendError(connection, message, GatewayErrors.Refused, refusal);
            return (Guid.Empty, null);
        }

        /// <summary>
        /// Keeps the player grain's presence fresh while its client talks to us: at most one
        /// fire-and-forget heartbeat every 5 seconds per connection. (The old gateway moved the
        /// timestamp on every message, so a client that sent more often than that never pinged.)
        /// </summary>
        private void PingHeartBeat(GatewayConnection connection, IPlayer player)
        {
            long now = timeProvider.GetTimestamp();
            long last = Interlocked.Read(ref connection.LastHeartBeatPing);
            if (last != 0 && timeProvider.GetElapsedTime(last, now) < HeartBeatPingInterval)
                return;
            if (Interlocked.CompareExchange(ref connection.LastHeartBeatPing, now, last) != last)
                return;
            player.HeartBeat().Ignore();
        }

        /// <summary>Reads the relay queue of one connection until it is completed, one frame at a time.</summary>
        private async Task RelayLoopAsync(GatewayConnection connection)
        {
            try
            {
                var reader = connection.RelayQueue.Reader;
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out WildCatMessageBase message))
                        await RelayAsync(connection, message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The relay worker of connection {ConnectionId} stopped", connection.Id);
            }
        }

        /// <summary>
        /// Passes a game message on to the other players of the match in its game target, without
        /// the sender's ticket. Never throws: a relay that fails is logged and dropped, so later
        /// messages keep their order.
        /// </summary>
        private async Task RelayAsync(GatewayConnection connection, WildCatMessageBase message)
        {
            try
            {
                var (_, session) = await CheckSessionAsync(connection, message, requirePlayer: true);
                if (session == null)
                    return;
                var player = grains.GetGrain<IPlayer>(session.UserID);
                PingHeartBeat(connection, player);

                // Without a game target there is nowhere to relay to; the frame is dropped, as before.
                if (!message.HasGameTarget)
                    return;
                if (!Guid.TryParse(message.game, out Guid matchId))
                {
                    logger.LogInformation("Dropped {MessageType} from connection {ConnectionId}: the game target is not a match id",
                        message.msgType, connection.Id);
                    return;
                }
                var game = grains.GetGrain<IRelayServerGame>(matchId, session.GameKey.ToString(), null);
                await game.SendToAllBut(player, 0, RelayFrame.WithoutTicket(message));
            }
            catch (GameNotRunningException ex)
            {
                logger.LogInformation("Dropped {MessageType} from connection {ConnectionId}: {Reason}",
                    message.msgType, connection.Id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Relaying {MessageType} from connection {ConnectionId} failed", message.msgType, connection.Id);
            }
        }

        /// <summary>
        /// Answers a request whose handler threw. Expected failures keep their code and message;
        /// anything else is logged with its stack trace and answered with a generic text.
        /// </summary>
        private void AnswerException(GatewayConnection connection, WildCatMessageBase message, MessageRoute route, Exception ex)
        {
            int code;
            string text;
            switch (ex)
            {
                case RequestFailedException failed:
                    code = failed.ErrorCode;
                    text = failed.Message;
                    logger.LogInformation("{MessageType} from connection {ConnectionId} failed with code {ErrorCode}: {Reason}",
                        message.msgType, connection.Id, code, text);
                    break;
                case WireFormatException malformed:
                    code = GatewayErrors.Refused;
                    text = GatewayErrors.MalformedMessage;
                    logger.LogInformation("The payload of {MessageType} from connection {ConnectionId} could not be read: {Reason}",
                        message.msgType, connection.Id, malformed.Message);
                    break;
                case SessionNotInitializedException:
                case SessionExpiredException:
                case InvalidGameException:
                case GameNotRunningException:
                case ChallengeTimeoutException:
                    // The game's own domain errors carry messages written for clients.
                    code = GatewayErrors.Refused;
                    text = ex.Message;
                    logger.LogInformation("{MessageType} from connection {ConnectionId} failed: {Reason}",
                        message.msgType, connection.Id, text);
                    break;
                default:
                    code = GatewayErrors.Internal;
                    text = GatewayErrors.InternalError;
                    logger.LogError(ex, "Handling {MessageType} from connection {ConnectionId} failed", message.msgType, connection.Id);
                    break;
            }
            SendFailure(connection, message, route.ResponseType, code, text);
        }

        /// <summary>
        /// Sends an ErrorMsg (8) for a request refused before any handler ran. The payload is an
        /// OptionalResponse&lt;EmptyMessage&gt; that is never successful; the frame echoes the request
        /// id when the request had one.
        /// </summary>
        private static void SendError(GatewayConnection connection, WildCatMessageBase request, int errorCode, string errorMessage)
        {
            SendFailure(connection, request, WildCatMsgType.ErrorMsg, errorCode, errorMessage);
        }

        /// <summary>Answers a request with a failed OptionalResponse on <paramref name="responseType"/>.</summary>
        private static void SendFailure(GatewayConnection connection, WildCatMessageBase request, WildCatMsgType responseType,
            int errorCode, string errorMessage)
        {
            // A failed response carries no payload, so the payload type does not matter on the wire.
            connection.Send(WildCatMessageBase.CreateResponse(OptionalResponse<EmptyMessage>.CreateFailed(errorMessage, errorCode),
                responseType, request));
        }
    }
}
