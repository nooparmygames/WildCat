using System.Collections.Concurrent;
using WildCat.Interfaces;

namespace WildCat.Scripting
{
    /// <summary>
    /// Runs compiled scripts on a <see cref="ScriptWorkerPool"/> under their
    /// <see cref="ScriptBudget"/>, with a circuit breaker per game.
    ///
    /// The caller gets its answer (the result, or <see cref="ScriptTimeoutException"/> at the
    /// deadline) no later than the time limit. A run that is still going when its caller gave up
    /// is "overdue": normally only for a moment, until its next guard check throws, but for as long
    /// as it takes when it is inside a library call the guards cannot interrupt, or while it waits
    /// for a free worker. While a game has more overdue runs than the allowed number, its new runs
    /// fail at once with <see cref="ScriptThrottledException"/>, so one game's runaway scripts
    /// cannot fill the pool for every other game.
    /// </summary>
    internal sealed class ScriptExecutor : IDisposable
    {
        private static ScriptExecutor shared;

        private readonly ConcurrentDictionary<string, int> overdue = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly object overdueGate = new object();

        public ScriptExecutor(int workerThreads, string threadName = "WildCat script worker")
        {
            Pool = new ScriptWorkerPool(workerThreads, threadName);
        }

        public ScriptWorkerPool Pool { get; }

        /// <summary>
        /// The executor every grain of this process uses. The first caller's
        /// <see cref="ScriptOptions.WorkerThreads"/> decides the pool size.
        /// </summary>
        public static ScriptExecutor GetShared(ScriptOptions options)
        {
            var existing = Volatile.Read(ref shared);
            if (existing != null)
                return existing;
            var created = new ScriptExecutor((options ?? new ScriptOptions()).EffectiveWorkerThreads);
            existing = Interlocked.CompareExchange(ref shared, created, null);
            if (existing == null)
                return created;
            created.Dispose();
            return existing;
        }

        /// <summary>Runs of <paramref name="gameKey"/> still going after their time limit.</summary>
        public int OverdueRuns(string gameKey)
        {
            return overdue.TryGetValue(gameKey ?? "", out int count) ? count : 0;
        }

        /// <summary>
        /// Starts the script on the worker pool and waits for it no longer than the budget. A fast
        /// script returns as soon as it finishes. When the deadline passes the caller gets
        /// <see cref="ScriptTimeoutException"/> at once; the script itself stops at its next guard
        /// check. Throws <see cref="ScriptThrottledException"/> without starting the script while
        /// <paramref name="gameKey"/> has more than <paramref name="maxOverdueRuns"/> overdue runs.
        /// </summary>
        public async Task<object> RunAsync(CompiledScript compiled, Globals globals, ScriptBudget budget, string gameKey, int maxOverdueRuns)
        {
            gameKey ??= "";
            int stillRunning = OverdueRuns(gameKey);
            if (stillRunning > maxOverdueRuns)
            {
                throw new ScriptThrottledException(
                    $"The script event '{budget.EventName}' was not run: this game has {stillRunning} earlier script runs " +
                    "still going after their time limit. New runs are refused until they finish; check the game's scripts for " +
                    "long loops or very large collections.");
            }

            var runner = compiled.Runner;
            Task<object> run = Task.Factory.StartNew(() =>
            {
                // A run that waited for a worker past its deadline does not start at all.
                budget.ThrowIfExpired();
                return ScriptGuard.Run(budget, () => runner(globals, budget.Token));
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Pool).Unwrap();
            try
            {
                return await run.WaitAsync(budget.Token);
            }
            catch (Exception ex) when (ex is not ScriptTimeoutException && ex is not ScriptRecursionLimitException && budget.IsExpired)
            {
                throw budget.CreateTimeoutException();
            }
            finally
            {
                if (!run.IsCompleted)
                    TrackOverdue(gameKey, run);
                budget.ReleaseWhenDone(run);
            }
        }

        private void TrackOverdue(string gameKey, Task run)
        {
            Change(gameKey, +1);
            run.ContinueWith(static (_, state) =>
            {
                var (executor, game) = ((ScriptExecutor, string))state;
                executor.Change(game, -1);
            }, (this, gameKey), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void Change(string gameKey, int delta)
        {
            lock (overdueGate)
            {
                int count = OverdueRuns(gameKey) + delta;
                if (count > 0)
                    overdue[gameKey] = count;
                else
                    overdue.TryRemove(gameKey, out _);
            }
        }

        public void Dispose()
        {
            Pool.Dispose();
        }
    }
}
