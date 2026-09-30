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
    /// operator body, <see cref="Probe"/> in front of instance field initializers, and
    /// <see cref="Enumerate{T}"/> around every sequence handed to a library method or constructor
    /// (LINQ, <c>new HashSet&lt;T&gt;(sequence)</c>, <c>string.Join</c>, <c>AddRange</c>) and
    /// around the sources of query expressions.
    ///
    /// The budget of the running script lives in an AsyncLocal, so it follows the script across
    /// awaits and into the tasks it starts, and the call depth is counted per logical call chain.
    /// .NET cannot abort a thread, so a runaway script stops itself: once the deadline passes,
    /// every loop iteration, jump, call and item a library method pulls from a guarded sequence
    /// throws <see cref="ScriptTimeoutException"/>. Deep recursion throws
    /// <see cref="ScriptRecursionLimitException"/> before the stack runs out, because a stack
    /// overflow would take the whole silo down.
    ///
    /// What the guards cannot stop is a single library call that does a lot of work without
    /// calling back into script code or pulling from a guarded sequence: a huge allocation
    /// (<c>new int[500_000_000]</c>, <c>new string('x', int.MaxValue)</c>,
    /// <c>StringBuilder.Append(c, count)</c>), string operations on huge strings (Replace, Split,
    /// PadLeft, IndexOf), sorting a large collection (<c>Array.Sort</c>, <c>List.Sort()</c>, the
    /// comparisons inside <c>OrderBy</c>) and passes over large in-memory collections, which
    /// <see cref="Enumerate{T}"/> lets through unguarded (their size is bounded by memory the
    /// script had to fill under the guards first). Memory is not capped either. Such a run keeps
    /// its worker thread until the call returns; <see cref="ScriptWorkerPool"/> keeps it off the
    /// shared thread pool, and the circuit breaker in <see cref="ScriptExecutor"/> stops the game
    /// from starting more of them.
    /// </summary>
    public static class ScriptGuard
    {
        private static readonly AsyncLocal<Frame> current = new AsyncLocal<Frame>();

        /// <summary>
        /// A guarded sequence checks the cancellation token for every item and reads the clock
        /// every this many items, in case the timer that cancels the token is late.
        /// </summary>
        internal const int ClockCheckInterval = 1024;

        // Collection types whose instances hold all their items in memory. Enumerate passes them
        // through unwrapped, which keeps the library's fast paths (Count, CopyTo) and lets a
        // collection be passed to its own AddRange.
        private static readonly HashSet<Type> MaterializedCollections = new HashSet<Type>
        {
            typeof(List<>), typeof(HashSet<>), typeof(Dictionary<,>), typeof(Dictionary<,>.KeyCollection),
            typeof(Dictionary<,>.ValueCollection), typeof(Queue<>), typeof(Stack<>), typeof(SortedSet<>),
            typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(LinkedList<>)
        };

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
        /// Returns <paramref name="source"/> wrapped so that every item a library method pulls from
        /// it checks the running script's deadline. Null, arrays, strings, the in-memory collections
        /// of System.Collections.Generic and sequences that are already guarded come back as they
        /// are.
        /// </summary>
        public static IEnumerable<T> Enumerate<T>(IEnumerable<T> source)
        {
            if (source == null || source is GuardedSequence<T> || IsMaterialized(source))
                return source;
            Frame frame = current.Value ?? throw NotInSandbox();
            return new GuardedSequence<T>(source, frame.Budget);
        }

        /// <summary>
        /// <see cref="Enumerate{T}"/> for the non-generic IEnumerable that Cast and OfType take.
        /// </summary>
        public static System.Collections.IEnumerable EnumerateUntyped(System.Collections.IEnumerable source)
        {
            if (source == null || source is GuardedUntypedSequence || IsMaterialized(source))
                return source;
            Frame frame = current.Value ?? throw NotInSandbox();
            return new GuardedUntypedSequence(source, frame.Budget);
        }

        private static bool IsMaterialized(object source)
        {
            if (source is Array || source is string)
                return true;
            var type = source.GetType();
            return type.IsGenericType && MaterializedCollections.Contains(type.GetGenericTypeDefinition());
        }

        /// <summary>
        /// Starts <paramref name="body"/> under <paramref name="budget"/>. Call it inside a task
        /// (the executor starts one on its worker pool) so the AsyncLocal change stays inside that
        /// task's execution context.
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

        private sealed class GuardedSequence<T> : IEnumerable<T>
        {
            private readonly IEnumerable<T> source;
            private readonly ScriptBudget budget;

            public GuardedSequence(IEnumerable<T> source, ScriptBudget budget)
            {
                this.source = source;
                this.budget = budget;
            }

            public IEnumerator<T> GetEnumerator()
            {
                budget.ThrowIfExpired();
                return new Enumerator(source.GetEnumerator(), budget);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<T>
            {
                private readonly IEnumerator<T> inner;
                private readonly ScriptBudget budget;
                private int count;

                public Enumerator(IEnumerator<T> inner, ScriptBudget budget)
                {
                    this.inner = inner;
                    this.budget = budget;
                }

                public T Current => inner.Current;

                object System.Collections.IEnumerator.Current => inner.Current;

                public bool MoveNext()
                {
                    budget.CheckItem(ref count);
                    return inner.MoveNext();
                }

                public void Reset() => inner.Reset();

                public void Dispose() => inner.Dispose();
            }
        }

        private sealed class GuardedUntypedSequence : System.Collections.IEnumerable
        {
            private readonly System.Collections.IEnumerable source;
            private readonly ScriptBudget budget;

            public GuardedUntypedSequence(System.Collections.IEnumerable source, ScriptBudget budget)
            {
                this.source = source;
                this.budget = budget;
            }

            public System.Collections.IEnumerator GetEnumerator()
            {
                budget.ThrowIfExpired();
                return new Enumerator(source.GetEnumerator(), budget);
            }

            private sealed class Enumerator : System.Collections.IEnumerator, IDisposable
            {
                private readonly System.Collections.IEnumerator inner;
                private readonly ScriptBudget budget;
                private int count;

                public Enumerator(System.Collections.IEnumerator inner, ScriptBudget budget)
                {
                    this.inner = inner;
                    this.budget = budget;
                }

                public object Current => inner.Current;

                public bool MoveNext()
                {
                    budget.CheckItem(ref count);
                    return inner.MoveNext();
                }

                public void Reset() => inner.Reset();

                public void Dispose() => (inner as IDisposable)?.Dispose();
            }
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

        /// <summary>
        /// The per-item check of a guarded sequence: the cancellation token every time (one
        /// volatile read), the clock every <see cref="ScriptGuard.ClockCheckInterval"/> items.
        /// </summary>
        public void CheckItem(ref int count)
        {
            if (Token.IsCancellationRequested
                || (++count % ScriptGuard.ClockCheckInterval == 0 && Stopwatch.GetTimestamp() >= deadline))
            {
                throw CreateTimeoutException();
            }
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
