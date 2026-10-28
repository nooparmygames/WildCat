using System;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat
{
    /// <summary>Schedules connection deadlines and delays on the platform that owns the socket.</summary>
    public interface IConnectionScheduler
    {
        /// <summary>Keep async continuations on the caller's synchronization context.</summary>
        bool CaptureContext { get; }

        /// <summary>Runs a callback once after a delay. Disposing the registration cancels it.</summary>
        IDisposable Schedule(TimeSpan delay, Action callback);
    }

    internal sealed class SystemConnectionScheduler : IConnectionScheduler
    {
        internal static readonly SystemConnectionScheduler Instance = new SystemConnectionScheduler();
        public bool CaptureContext => false;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            return new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    internal static class ConnectionSchedulerExtensions
    {
        internal static async Task DelayAsync(this IConnectionScheduler scheduler, TimeSpan delay,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<bool>();
            using (scheduler.Schedule(delay, () => completion.TrySetResult(true)))
            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                await completion.Task.ConfigureAwait(scheduler.CaptureContext);
        }
    }
}
