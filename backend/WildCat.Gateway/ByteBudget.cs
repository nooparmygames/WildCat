using System;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat.Gateway
{
    /// <summary>
    /// A thread-safe count of the bytes a queue holds against a limit. Frames are taken in when
    /// they fit, or when the queue is empty, so a single frame larger than the whole budget still
    /// goes through on its own; they are released when they leave the queue. The gateway keeps one
    /// per connection for the outbound queue (<see cref="GameGatewayOptions.OutboundQueueBytes"/>)
    /// and one for the relay queue (<see cref="GameGatewayOptions.RelayQueueBytes"/>).
    /// </summary>
    internal sealed class ByteBudget
    {
        private readonly object gate = new object();
        private long used;
        private TaskCompletionSource<bool> released;

        public ByteBudget(long limit)
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit), "The budget must be at least one byte.");
            Limit = limit;
        }

        public long Limit { get; }

        /// <summary>Bytes taken and not released yet.</summary>
        public long Used
        {
            get
            {
                lock (gate)
                    return used;
            }
        }

        /// <summary>Takes <paramref name="bytes"/> when they fit (or nothing is held) and returns true; false otherwise.</summary>
        public bool TryTake(int bytes)
        {
            lock (gate)
                return TakeLocked(bytes);
        }

        /// <summary>Waits until <paramref name="bytes"/> fit (or nothing is held), then takes them.</summary>
        public async Task TakeAsync(int bytes, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task wait;
                lock (gate)
                {
                    if (TakeLocked(bytes))
                        return;
                    released ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = released.Task;
                }
                await wait.WaitAsync(cancellationToken);
            }
        }

        /// <summary>Gives back bytes taken earlier and wakes a waiting <see cref="TakeAsync"/>.</summary>
        public void Release(int bytes)
        {
            TaskCompletionSource<bool> wake;
            lock (gate)
            {
                used = Math.Max(0, used - bytes);
                wake = released;
                released = null;
            }
            wake?.TrySetResult(true);
        }

        private bool TakeLocked(int bytes)
        {
            if (bytes < 0)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            if (used > 0 && used + bytes > Limit)
                return false;
            used += bytes;
            return true;
        }
    }
}
