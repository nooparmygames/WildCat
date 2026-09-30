using System;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat
{
    /// <summary>
    /// One WebSocket carrying binary messages, one protocol frame per message. The connection
    /// calls <see cref="SendAsync"/> (and <see cref="CloseAsync"/>) from a single writer and
    /// <see cref="ReceiveAsync"/> from a single reader, so an implementation needs to allow one
    /// send and one receive at the same time and nothing more. <see cref="Abort"/> and
    /// <see cref="IDisposable.Dispose"/> can come from any thread at any time.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientWebSocketTransport"/> (System.Net.WebSockets) serves desktop, mobile and
    /// the editor; the Unity layer adds a browser WebSocket transport for WebGL builds.
    /// </remarks>
    public interface ITransport : IDisposable
    {
        /// <summary>Opens the socket. Throws when it cannot.</summary>
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        /// <summary>Sends one whole binary message.</summary>
        Task SendAsync(byte[] message, CancellationToken cancellationToken);

        /// <summary>
        /// Waits for the next whole binary message. Returns null once the server has closed the
        /// socket with a close handshake; throws when the socket broke.
        /// </summary>
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Starts a close handshake (sends the close message). <see cref="ReceiveAsync"/> returns null
        /// when the server has answered it.
        /// </summary>
        Task CloseAsync(CancellationToken cancellationToken);

        /// <summary>Drops the socket at once, without a handshake. Pending calls fail.</summary>
        void Abort();
    }
}
