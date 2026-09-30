using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat.Gateway
{
    /// <summary>
    /// Processes our websocket connections and sends and receives data for them.
    /// This is only the low level processing and parsing of messages and executing logic happens in the FrontEnd class
    /// </summary>
    public class ConnectionProcessor
    {
        public event Func<byte[], int, Task> OnDataReceived;
        public event Func<ConnectionProcessor, int, Task> OnConnected;
        public event Func<ConnectionProcessor, int, Task> OnDisConnected;

        private SemaphoreSlim sendLock = new SemaphoreSlim(1);
        private readonly int maxMessageBytes;
        private readonly ILogger logger;

        public int ID { get; private set; }
        private static int lastId = int.MinValue;
        private static object idLock = new object();
        public WebSocket socket { get; private set; }

        public static int GetNextId()
        {
            lock (idLock)
            {
                return ++lastId;
            }
        }

        /// <param name="socket">The accepted WebSocket.</param>
        /// <param name="id">Connection id from <see cref="GetNextId"/>.</param>
        /// <param name="maxMessageBytes">Largest reassembled message the client may send.</param>
        /// <param name="logger">Logger for connection level events.</param>
        public ConnectionProcessor(WebSocket socket, int id, int maxMessageBytes, ILogger logger)
        {
            this.socket = socket;
            this.ID = id;
            this.maxMessageBytes = maxMessageBytes;
            this.logger = logger;
        }

        public async Task Process(HttpContext context, WebSocket webSocket)
        {
            await (OnConnected?.Invoke(this, ID) ?? Task.CompletedTask);
            await ReceiveMessages(webSocket, context.RequestAborted);
        }

        /// <summary>
        /// Reads messages until the socket closes and then runs the disconnect callback, whether
        /// the client closed cleanly, dropped the connection or sent a message over the size limit.
        /// </summary>
        private async Task ReceiveMessages(WebSocket webSocket, CancellationToken cancellationToken)
        {
            byte[] receiveBuffer = new byte[1024 * 4];
            // Collects the frames of a message that does not fit in one receive. Created on first
            // use so idle connections do not each hold a large buffer.
            MemoryStream messageBuffer = null;
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        long messageSize = (messageBuffer?.Length ?? 0) + receiveResult.Count;
                        if (messageSize > maxMessageBytes)
                        {
                            logger.LogWarning(
                                "Connection {ConnectionId} sent a message larger than {MaxMessageBytes} bytes, closing it",
                                ID, maxMessageBytes);
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig,
                                $"Messages are limited to {maxMessageBytes} bytes", cancellationToken);
                            break;
                        }

                        if (receiveResult.EndOfMessage && messageBuffer == null)
                        {
                            OnDataReceived?.Invoke(receiveBuffer.AsSpan(0, receiveResult.Count).ToArray(), ID);
                        }
                        else
                        {
                            messageBuffer ??= new MemoryStream();
                            messageBuffer.Write(receiveBuffer, 0, receiveResult.Count);
                            if (receiveResult.EndOfMessage)
                            {
                                OnDataReceived?.Invoke(messageBuffer.ToArray(), ID);
                                messageBuffer = null;
                            }
                        }
                    }
                    else
                    {

                    }
                }
            }
            catch (WebSocketException ex)
            {
                // The client went away without a close handshake (app killed, network lost). This
                // is an ordinary way for a game client to disconnect, not a server error.
                logger.LogDebug("Connection {ConnectionId} closed without a close handshake: {Reason}", ID, ex.Message);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Connection {ConnectionId} was aborted", ID);
            }
            finally
            {
                await (OnDisConnected?.Invoke(this, ID) ?? Task.CompletedTask);
            }
        }

        public async Task<bool> Send(byte[] message)
        {
            bool success = false;
            await sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
                success = true;
            }
            catch (Exception s)
            {
                logger.LogDebug("Sending to connection {ConnectionId} failed: {Reason}", ID, s.Message);
            }
            finally
            {
                sendLock.Release();
            }
            return success;
        }
    }
}
