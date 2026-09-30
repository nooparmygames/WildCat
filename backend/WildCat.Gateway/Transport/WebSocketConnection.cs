using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildCat.Gateway
{
    /// <summary>
    /// The transport of one client socket. A receive loop reads whole binary messages (up to
    /// <see cref="GameGatewayOptions.MaxMessageBytes"/>) and hands each one on in arrival order;
    /// a single writer task drains a bounded outbound queue, so code that sends a frame
    /// (<see cref="TrySend"/>) never waits for the socket. A client that stops reading fills the
    /// queue and is disconnected with PolicyViolation instead of holding memory or senders.
    /// </summary>
    public sealed class WebSocketConnection
    {
        /// <summary>
        /// How long closing waits for the writer to finish the frame it is sending, or for the
        /// client to answer a close frame, before the socket is aborted.
        /// </summary>
        internal static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

        internal const string SlowConsumerCloseReason = "Messages were not read fast enough";

        private static int lastId;

        private readonly WebSocket socket;
        private readonly int maxMessageBytes;
        private readonly int outboundCapacity;
        private readonly ILogger logger;
        private readonly Channel<byte[]> outbound;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private Task writer = Task.CompletedTask;
        private Task receiver = Task.CompletedTask;
        private WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
        private string closeDescription = "";
        private int slowConsumer;
        private int closeFrameSent;
        private volatile bool dropQueuedFrames;

        public WebSocketConnection(WebSocket socket, int id, int maxMessageBytes, int outboundCapacity, ILogger logger)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (maxMessageBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            if (outboundCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(outboundCapacity));
            Id = id;
            this.maxMessageBytes = maxMessageBytes;
            this.outboundCapacity = outboundCapacity;
            outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(outboundCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        /// <summary>A process-wide unique id for a new connection.</summary>
        public static int NextId()
        {
            return Interlocked.Increment(ref lastId);
        }

        public int Id { get; }

        /// <summary>Cancelled as soon as the connection starts closing, for whatever reason.</summary>
        public CancellationToken Closing
        {
            get { return closing.Token; }
        }

        /// <summary>Frames waiting for the writer.</summary>
        internal int QueuedFrames
        {
            get { return outbound.Reader.Count; }
        }

        /// <summary>
        /// Queues an encoded frame for the socket and returns at once. Returns false when the
        /// connection is closing, and also when the queue is full, in which case the connection is
        /// closed: its client is not reading.
        /// </summary>
        public bool TrySend(byte[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (outbound.Writer.TryWrite(frame))
                return true;
            // TryWrite fails for a full queue or a completed one, and every path that completes
            // the queue cancels the closing token first, so this is a full queue.
            if (!closing.IsCancellationRequested && Interlocked.CompareExchange(ref slowConsumer, 1, 0) == 0)
            {
                logger.LogWarning(
                    "Connection {ConnectionId} is not reading its messages: {Capacity} are waiting. Closing it with PolicyViolation",
                    Id, outboundCapacity);
                dropQueuedFrames = true;
                outbound.Writer.TryComplete();
                _ = Task.Run(CloseSlowConsumerAsync);
            }
            return false;
        }

        /// <summary>
        /// Serves the socket until it closes: reads messages and awaits <paramref name="onFrame"/>
        /// for each, in order, while the writer sends queued frames. Returns once the socket is
        /// closed and the writer has stopped. Never throws for socket errors.
        /// </summary>
        public async Task RunAsync(Func<byte[], Task> onFrame, CancellationToken aborted)
        {
            if (onFrame == null)
                throw new ArgumentNullException(nameof(onFrame));
            writer = Task.Run(WriteLoopAsync);
            receiver = ReceiveLoopAsync(onFrame, aborted);
            try
            {
                await receiver;
            }
            finally
            {
                await ShutdownAsync();
            }
        }

        /// <summary>Drops the connection at once, without a close handshake.</summary>
        public void Abort()
        {
            closing.Cancel();
            outbound.Writer.TryComplete();
            try
            {
                socket.Abort();
            }
            catch (Exception ex)
            {
                logger.LogDebug("Aborting connection {ConnectionId} failed: {Reason}", Id, ex.Message);
            }
        }

        private async Task ReceiveLoopAsync(Func<byte[], Task> onFrame, CancellationToken aborted)
        {
            byte[] buffer = new byte[4 * 1024];
            // Collects the parts of a message that does not fit in one receive. Created on first
            // use so idle connections do not each hold a large buffer.
            MemoryStream message = null;
            try
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), aborted);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    if (result.MessageType != WebSocketMessageType.Binary)
                        continue; // Text messages are not part of the protocol and are ignored.

                    long messageSize = (message?.Length ?? 0) + result.Count;
                    if (messageSize > maxMessageBytes)
                    {
                        logger.LogWarning(
                            "Connection {ConnectionId} sent a message larger than {MaxMessageBytes} bytes, closing it",
                            Id, maxMessageBytes);
                        closeStatus = WebSocketCloseStatus.MessageTooBig;
                        closeDescription = $"Messages are limited to {maxMessageBytes} bytes";
                        break;
                    }

                    byte[] frame;
                    if (result.EndOfMessage && message == null)
                    {
                        frame = buffer.AsSpan(0, result.Count).ToArray();
                    }
                    else
                    {
                        message ??= new MemoryStream();
                        message.Write(buffer, 0, result.Count);
                        if (!result.EndOfMessage)
                            continue;
                        frame = message.ToArray();
                        message = null;
                    }

                    try
                    {
                        await onFrame(frame);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Handling a message from connection {ConnectionId} failed", Id);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                // The client went away without a close handshake (app killed, network lost). This
                // is an ordinary way for a game client to disconnect, not a server error.
                logger.LogDebug("Connection {ConnectionId} closed without a close handshake: {Reason}", Id, ex.Message);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Connection {ConnectionId} was aborted", Id);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                logger.LogDebug("Connection {ConnectionId} stopped receiving: {Reason}", Id, ex.Message);
            }
        }

        private async Task WriteLoopAsync()
        {
            try
            {
                ChannelReader<byte[]> reader = outbound.Reader;
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out byte[] frame))
                    {
                        if (dropQueuedFrames)
                            return;
                        await socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, CancellationToken.None);
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException
                                       || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                // The socket is gone; make sure the receive loop notices too.
                logger.LogDebug("Sending to connection {ConnectionId} failed: {Reason}", Id, ex.Message);
                Abort();
            }
        }

        /// <summary>
        /// Runs once the receive loop has ended: stops new sends, lets the writer send what is
        /// queued (for at most <see cref="CloseTimeout"/>) and answers or sends the close frame.
        /// </summary>
        private async Task ShutdownAsync()
        {
            closing.Cancel();
            outbound.Writer.TryComplete();
            if (!await CompletesWithin(writer, CloseTimeout))
            {
                logger.LogDebug("Connection {ConnectionId} did not take its last messages in time", Id);
                Abort();
                return;
            }
            await SendCloseFrameAsync(closeStatus, closeDescription);
        }

        private async Task CloseSlowConsumerAsync()
        {
            try
            {
                closing.Cancel();
                // The writer is probably stuck in a send the client does not read.
                if (!await CompletesWithin(writer, CloseTimeout))
                {
                    Abort();
                    return;
                }
                await SendCloseFrameAsync(WebSocketCloseStatus.PolicyViolation, SlowConsumerCloseReason);
                if (!await CompletesWithin(receiver, CloseTimeout))
                    Abort();
            }
            catch (Exception ex)
            {
                logger.LogDebug("Closing slow connection {ConnectionId} failed: {Reason}", Id, ex.Message);
                Abort();
            }
        }

        /// <summary>Sends the close frame once, if the socket can still take one; aborts when it cannot.</summary>
        private async Task SendCloseFrameAsync(WebSocketCloseStatus status, string description)
        {
            if (Interlocked.Exchange(ref closeFrameSent, 1) != 0)
                return;
            WebSocketState state = socket.State;
            if (state != WebSocketState.Open && state != WebSocketState.CloseReceived)
                return;
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                await socket.CloseOutputAsync(status, description, timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException
                                       || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                logger.LogDebug("Closing connection {ConnectionId} failed: {Reason}", Id, ex.Message);
                Abort();
            }
        }

        private static async Task<bool> CompletesWithin(Task task, TimeSpan timeout)
        {
            if (task.IsCompleted)
                return true;
            try
            {
                await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception)
            {
                // The task finished, with an error it has already logged.
            }
            return true;
        }
    }
}
