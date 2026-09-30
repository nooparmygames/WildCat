using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat
{
    /// <summary>
    /// <see cref="ITransport"/> over System.Net.WebSockets.ClientWebSocket, which works in the Unity
    /// editor and in desktop and mobile players (Mono and IL2CPP). Not for WebGL, where browsers
    /// only offer their own WebSocket API.
    /// </summary>
    public sealed class ClientWebSocketTransport : ITransport
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly int maxMessageBytes;
        private readonly byte[] buffer = new byte[16 * 1024];

        /// <param name="maxMessageBytes">The largest message <see cref="ReceiveAsync"/> accepts.</param>
        /// <param name="configure">Changes to the socket options before connecting (headers, proxy, keep-alive).</param>
        public ClientWebSocketTransport(int maxMessageBytes, Action<ClientWebSocketOptions> configure = null)
        {
            if (maxMessageBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            this.maxMessageBytes = maxMessageBytes;
            if (configure != null)
                configure(socket.Options);
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            return socket.ConnectAsync(uri, cancellationToken);
        }

        public Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            return socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cancellationToken);
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            MemoryStream parts = null;
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    // Text messages are not part of the protocol. Skip the rest of this one.
                    parts = null;
                    while (!result.EndOfMessage)
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                long size = (parts == null ? 0 : parts.Length) + result.Count;
                if (size > maxMessageBytes)
                    throw new InvalidDataException("The server sent a message larger than " + maxMessageBytes + " bytes.");

                if (result.EndOfMessage && parts == null)
                {
                    byte[] whole = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, whole, 0, result.Count);
                    return whole;
                }

                if (parts == null)
                    parts = new MemoryStream();
                parts.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    return parts.ToArray();
            }
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            WebSocketState state = socket.State;
            if (state != WebSocketState.Open && state != WebSocketState.CloseReceived)
                return Task.CompletedTask;
            return socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
        }

        public void Abort()
        {
            socket.Abort();
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }
}
