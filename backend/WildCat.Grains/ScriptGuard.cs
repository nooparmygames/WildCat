using System.Diagnostics;
using System.Runtime.CompilerServices;
using WildCat.Interfaces;

namespace WildCat.Scripting
{
    /// <summary>
    /// Runtime limits for script events. Scripts never call this class themselves (the validator
    /// rejects it); <see cref="ScriptGuardRewriter"/> inserts the calls before compiling:
    /// <see cref="Check"/> at the start of every loop body and before every goto,
    /// <see cref="Enter"/> around every method, local function, lambda, accessor, constructor and
    /// operator body, and <see cref="Probe"/> in front of instance field initializers.
    ///
    /// The budget of the running script lives in an AsyncLocal, so it follows the script across
    /// awaits and into the tasks it starts, and the call depth is counted per logical call chain.
    /// .NET cannot abort a thread, so a runaway script stops itself: once the deadline passes,
    /// every loop iteration, jump and call throws <see cref="ScriptTimeoutException"/>. Deep
    /// recursion throws <see cref="ScriptRecursionLimitException"/> before the stack runs out,
    /// because a stack overflow would take the whole silo down.
    /// </summary>
    public static class ScriptGuard
    {
        private static readonly AsyncLocal<Frame> current = new AsyncLocal<Frame>();

        /// <summary>
        /// Throws when the running script is past its deadline or close to the end of the stack.
        /// </summary>
        public static void Check()
        {
            Frame frame = current.Value ?? throw NotInSandbox();
            frame.Budget.ThrowIfExpired();
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw frame.Budget.CreateRecursionException();
        }

        /// <summary>
        /// <see cref="Check"/> plus one level of call depth, released when the returned scope is
        /// disposed.
        /// </summary>
        public static Scope Enter()
        {
            Frame frame = current.Value ?? throw NotInSandbox();
            frame.Budget.ThrowIfExpired();
            if (frame.Depth >= frame.Budget.MaxCallDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw frame.Budget.CreateRecursionException();
            current.Value = new Frame(frame.Budget, frame.Depth + 1);
            return new Scope(frame);
        }

        /// <summary>
        /// <see cref="Check"/> in expression form, for field initializers, which run before any
        /// constructor body: <c>field = ScriptGuard.Probe() ? value : default(T)</c>.
        /// </summary>
        public static bool Probe()
        {
            Check();
            return true;
        }

        /// <summary>
        /// Starts <paramref name="body"/> under <paramref name="budget"/>. Call it on a thread-pool
        /// thread (Task.Run) so the AsyncLocal change stays inside that task's execution context.
        /// </summary>
        internal static Task<object> Run(ScriptBudget budget, Func<Task<object>> body)
        {
            current.Value = new Frame(budget, 0);
            return body();
        }

        private static InvalidOperationException NotInSandbox()
        {
            return new InvalidOperationException("Script code can only run inside the script sandbox.");
        }

        internal sealed class Frame
        {
            public Frame(ScriptBudget budget, int depth)
            {
                Budget = budget;
                Depth = depth;
            }

            public ScriptBudget Budget { get; }
            public int Depth { get; }
        }

        /// <summary>
        /// Restores the call depth of the caller when a guarded body exits.
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            private readonly Frame saved;

            internal Scope(Frame saved)
            {
                this.saved = saved;
            }

            public void Dispose()
            {
                if (saved != null)
                    current.Value = saved;
            }
        }
    }

    /// <summary>
    /// The limits of one script run: a wall-clock deadline, checked both through a cancellation
    /// token (which also cancels the script API's pending grain calls) and against the stopwatch
    /// (so a starved thread pool cannot delay it), plus the maximum call depth.
    /// </summary>
    internal sealed class ScriptBudget
    {
        private readonly CancellationTokenSource source;
        private readonly long deadline;

        public ScriptBudget(string eventName, TimeSpan timeout, int maxCallDepth)
        {
            EventName = eventName;
            Timeout = timeout;
            MaxCallDepth = maxCallDepth;
            source = new CancellationTokenSource(timeout);
            Token = source.Token;
            deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        }

        public string EventName { get; }
        public TimeSpan Timeout { get; }
        public int MaxCallDepth { get; }
        public CancellationToken Token { get; }

        public bool IsExpired => Token.IsCancellationRequested || Stopwatch.GetTimestamp() >= deadline;

        public void ThrowIfExpired()
        {
            if (IsExpired)
                throw CreateTimeoutException();
        }

        public ScriptTimeoutException CreateTimeoutException()
        {
            return new ScriptTimeoutException(
                $"The script event '{EventName}' did not finish within {Timeout.TotalSeconds:0.###} seconds and was stopped.");
        }

        public ScriptRecursionLimitException CreateRecursionException()
        {
            return new ScriptRecursionLimitException(
                $"The script event '{EventName}' nested calls deeper than {MaxCallDepth} levels or ran out of stack. Check for unbounded recursion.");
        }

        /// <summary>
        /// Disposes the token source once <paramref name="run"/> has finished, which may be after
        /// the caller gave up on it, and observes its exception so it is not reported as unobserved.
        /// </summary>
        public void ReleaseWhenDone(Task run)
        {
            run.ContinueWith(static (task, state) =>
            {
                _ = task.Exception;
                ((CancellationTokenSource)state).Dispose();
            }, source, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
