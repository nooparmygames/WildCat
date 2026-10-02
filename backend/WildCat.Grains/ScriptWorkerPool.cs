using System.Collections.Concurrent;

namespace WildCat.Scripting
{
    /// <summary>
    /// A task scheduler with a fixed number of dedicated threads for script code. Scripts start on
    /// it, and the continuations of their awaits come back to it (the await captures
    /// TaskScheduler.Current), so no script code runs on the shared thread pool that the silo, the
    /// gateway and ASP.NET Core depend on. A script the guard calls cannot stop (a long library
    /// call, see <see cref="ScriptGuard"/>) keeps one of these threads until it returns, and
    /// runs queue behind it; everything else in the process keeps working.
    /// </summary>
    internal sealed class ScriptWorkerPool : TaskScheduler, IDisposable
    {
        [ThreadStatic]
        private static ScriptWorkerPool currentPool;

        private readonly BlockingCollection<Task> queue = new BlockingCollection<Task>(new ConcurrentQueue<Task>());
        private readonly Thread[] threads;
        private int running;
        private int peakRunning;
        private long executed;

        public ScriptWorkerPool(int threadCount, string name = "WildCat script worker")
        {
            if (threadCount < 1)
                throw new ArgumentOutOfRangeException(nameof(threadCount), threadCount, "A worker pool needs at least one thread.");
            threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(Work)
                {
                    IsBackground = true,
                    Name = $"{name} {i + 1}"
                };
                threads[i].Start();
            }
        }

        public override int MaximumConcurrencyLevel => threads.Length;

        /// <summary>Tasks executing right now.</summary>
        public int Running => Volatile.Read(ref running);

        /// <summary>The most tasks that ever executed at the same time; never above the thread count.</summary>
        public int PeakRunning => Volatile.Read(ref peakRunning);

        /// <summary>Tasks waiting for a free thread.</summary>
        public int Queued => queue.Count;

        /// <summary>Tasks this pool has executed, for tests and diagnostics.</summary>
        public long Executed => Interlocked.Read(ref executed);

        /// <summary>True on one of this pool's threads.</summary>
        public bool IsWorkerThread => currentPool == this;

        private void Work()
        {
            currentPool = this;
            foreach (var task in queue.GetConsumingEnumerable())
            {
                int now = Interlocked.Increment(ref running);
                int peak;
                while (now > (peak = Volatile.Read(ref peakRunning)) && Interlocked.CompareExchange(ref peakRunning, now, peak) != peak)
                {
                }
                try
                {
                    TryExecuteTask(task);
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                    Interlocked.Increment(ref executed);
                }
            }
        }

        protected override void QueueTask(Task task)
        {
            queue.Add(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // Only on our own threads, so inlining never moves script code onto another thread.
            return currentPool == this && TryExecuteTask(task);
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return queue.ToArray();
        }

        /// <summary>
        /// Stops taking tasks; the threads finish the queued ones and exit.
        /// </summary>
        public void Dispose()
        {
            queue.CompleteAdding();
        }
    }
}
