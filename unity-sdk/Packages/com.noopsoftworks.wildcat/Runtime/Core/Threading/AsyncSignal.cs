using System.Threading;
using System.Threading.Tasks;

namespace WildCat
{
    /// <summary>
    /// Wakes the connection's single writer without SemaphoreSlim's thread-pool continuations.
    /// Completions happen outside the lock, because the writer may run inline on the browser thread.
    /// </summary>
    internal sealed class AsyncSignal
    {
        private readonly object gate = new object();
        private readonly bool captureContext;
        private int count;
        private TaskCompletionSource<bool> waiter;

        internal AsyncSignal(bool captureContext) { this.captureContext = captureContext; }

        internal void Release()
        {
            TaskCompletionSource<bool> waiting;
            lock (gate)
            {
                waiting = waiter;
                waiter = null;
                if (waiting == null)
                    count++;
            }
            waiting?.TrySetResult(true);
        }

        internal async Task WaitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<bool> waiting;
            lock (gate)
            {
                if (count > 0)
                {
                    count--;
                    return;
                }
                waiting = new TaskCompletionSource<bool>();
                waiter = waiting;
            }
            using (cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken)))
            {
                try
                {
                    await waiting.Task.ConfigureAwait(captureContext);
                }
                finally
                {
                    lock (gate)
                    {
                        if (waiter == waiting)
                            waiter = null;
                    }
                }
            }
        }
    }
}
