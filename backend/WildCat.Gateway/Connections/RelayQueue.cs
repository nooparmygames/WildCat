using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// The game messages of one connection waiting for its relay worker, in arrival order. It holds
    /// at most <see cref="GatewayConnection.RelayQueueCapacity"/> frames and
    /// <see cref="GameGatewayOptions.RelayQueueBytes"/> bytes (a single frame larger than that
    /// still goes through when the queue is empty); the receive loop waits for room, which stops
    /// reading the socket of a client that sends faster than its messages are passed on.
    /// </summary>
    internal sealed class RelayQueue
    {
        private readonly Channel<(WildCatMessageBase Message, int Bytes)> channel;
        private readonly ByteBudget bytes;

        public RelayQueue(int capacity, long maxBytes)
        {
            channel = Channel.CreateBounded<(WildCatMessageBase, int)>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            bytes = new ByteBudget(maxBytes);
        }

        /// <summary>Frames waiting.</summary>
        public int Count
        {
            get { return channel.Reader.Count; }
        }

        /// <summary>Bytes of the frames waiting (their size on the wire).</summary>
        public long QueuedBytes
        {
            get { return bytes.Used; }
        }

        /// <summary>
        /// Queues a game message of <paramref name="size"/> bytes, waiting while the queue is full.
        /// Throws OperationCanceledException when <paramref name="cancellationToken"/> fires and
        /// ChannelClosedException once the queue is completed.
        /// </summary>
        public async Task EnqueueAsync(WildCatMessageBase message, int size, CancellationToken cancellationToken)
        {
            await bytes.TakeAsync(size, cancellationToken);
            try
            {
                await channel.Writer.WriteAsync((message, size), cancellationToken);
            }
            catch
            {
                bytes.Release(size);
                throw;
            }
        }

        /// <summary>Waits until a frame can be read; false once the queue is completed and empty.</summary>
        public ValueTask<bool> WaitToReadAsync()
        {
            return channel.Reader.WaitToReadAsync();
        }

        /// <summary>Takes the next frame and gives its bytes back to the budget.</summary>
        public bool TryDequeue(out WildCatMessageBase message)
        {
            if (channel.Reader.TryRead(out var item))
            {
                bytes.Release(item.Bytes);
                message = item.Message;
                return true;
            }
            message = null;
            return false;
        }

        /// <summary>Accepts no more frames; the ones queued can still be read.</summary>
        public void Complete()
        {
            channel.Writer.TryComplete();
        }
    }
}
