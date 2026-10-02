using System.Net.WebSockets;

namespace WildCat.Tests
{
    /// <summary>
    /// A WebSocket whose client never reads and never sends: every send and receive waits until
    /// the socket is aborted. Lets transport tests fill queues without timing on real sockets.
    /// </summary>
    internal sealed class StalledWebSocket : WebSocket
    {
        private readonly TaskCompletionSource aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int sends;

        /// <summary>Sends that started (the writer is stuck in the first one).</summary>
        public int SendsStarted => Volatile.Read(ref sends);

        public Task Aborted => aborted.Task;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string CloseStatusDescription => null;

        public override WebSocketState State => aborted.Task.IsCompleted ? WebSocketState.Aborted : WebSocketState.Open;

        public override string SubProtocol => null;

        public override void Abort()
        {
            aborted.TrySetResult();
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return WaitForAbort(cancellationToken);
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return WaitForAbort(cancellationToken);
        }

        public override void Dispose()
        {
            Abort();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await WaitForAbort(cancellationToken);
            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sends);
            return WaitForAbort(cancellationToken);
        }

        private async Task WaitForAbort(CancellationToken cancellationToken)
        {
            await aborted.Task.WaitAsync(cancellationToken);
            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        }
    }
}
