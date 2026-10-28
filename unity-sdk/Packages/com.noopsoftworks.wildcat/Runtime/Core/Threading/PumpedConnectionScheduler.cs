using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WildCat
{
    /// <summary>
    /// Connection timers driven by the main loop, with no managed timers or worker threads.
    /// Create and use the client on the pumping thread. Call Pump every frame, including while
    /// connecting or reconnecting. WildCatBehaviour does this automatically in Web players.
    /// </summary>
    public sealed class PumpedConnectionScheduler : IConnectionScheduler
    {
        private readonly Func<TimeSpan> clock;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly List<Registration> pending = new List<Registration>();

        /// <summary>Uses a monotonic clock, so deadlines include time spent between frames.</summary>
        public PumpedConnectionScheduler() : this(CreateClock()) { }

        internal PumpedConnectionScheduler(Func<TimeSpan> clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool CaptureContext => true;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            CheckThread();
            if (delay < TimeSpan.Zero || delay.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(delay));
            var registration = new Registration(this, clock() + delay,
                callback ?? throw new ArgumentNullException(nameof(callback)));
            pending.Add(registration);
            return registration;
        }

        /// <summary>Runs the deadlines due at this frame. New timers wait until the next pump.</summary>
        public void Pump()
        {
            CheckThread();
            TimeSpan now = clock();
            var due = pending.FindAll(item => item.Due <= now);
            foreach (var item in due)
            {
                if (!pending.Remove(item))
                    continue;
                Action callback = item.Callback;
                item.Callback = null;
                callback?.Invoke();
            }
        }

        private static Func<TimeSpan> CreateClock()
        {
            var stopwatch = Stopwatch.StartNew();
            return () => stopwatch.Elapsed;
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Pump and use this connection on the thread that created its scheduler.");
        }

        private sealed class Registration : IDisposable
        {
            private readonly PumpedConnectionScheduler owner;
            internal readonly TimeSpan Due;
            internal Action Callback;

            internal Registration(PumpedConnectionScheduler owner, TimeSpan due, Action callback)
            {
                this.owner = owner;
                Due = due;
                Callback = callback;
            }

            public void Dispose()
            {
                owner.CheckThread();
                owner.pending.Remove(this);
                Callback = null;
            }
        }
    }
}
