using System.Collections.Concurrent;
using System.Diagnostics;
using WildCat.Interfaces;
using WildCat.Scripting;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The layers that keep a runaway script from holding threads after its time limit: guarded
    /// sequences for library loops, the dedicated worker pool and the per-game circuit breaker.
    /// Every test uses its own <see cref="ScriptExecutor"/>, so the pool size and the counts are
    /// the test's alone.
    /// </summary>
    public class ScriptWorkerTests
    {
        private static readonly TimeSpan ShortLimit = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan LongLimit = TimeSpan.FromSeconds(30);

        private static CompiledScript CompileValid(string code)
        {
            var compiled = ScriptCompilationUtility.Compile(new ScriptSource("game", "run", new List<EventParameter>(), code));
            Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Validation.Diagnostics));
            return compiled;
        }

        private static Task<object> Run(ScriptExecutor executor, CompiledScript compiled, TimeSpan limit, string game = "game",
            int maxOverdueRuns = int.MaxValue, params object[] args)
        {
            return executor.RunAsync(compiled, new Globals { args = args }, new ScriptBudget("run", limit, 64), game, maxOverdueRuns);
        }

        /// <summary>
        /// The instrumented source of a script, to check where the rewriter put guards.
        /// </summary>
        private static string Instrument(string code)
        {
            var analysis = ScriptCompilationUtility.Analyze(new ScriptSource("game", "run", new List<EventParameter>(), code));
            Assert.True(analysis.Validation.Success, string.Join(Environment.NewLine, analysis.Validation.Diagnostics));
            return new ScriptGuardRewriter(analysis.Model).Visit(analysis.Tree.GetRoot()).ToFullString();
        }

        private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(watch.Elapsed < timeout, "The condition did not come true in time.");
                await Task.Delay(20);
            }
        }

        // Guarded sequences.

        // Each of these spends seconds inside library code (Sum, Distinct, the HashSet constructor,
        // SequenceEqual, a query's Where) without calling back into the script, so before the
        // sequences were guarded the worker stayed busy long after the caller got its timeout.
        [Theory]
        [InlineData("long run() { return (long)Enumerable.Repeat(1, int.MaxValue).Sum(); }")]
        [InlineData("int run() { return Enumerable.Repeat(1, int.MaxValue).Distinct().Count(); }")]
        [InlineData("int run() { return Enumerable.Max(Enumerable.Repeat(1, int.MaxValue)); }")]
        [InlineData("int run() { return new HashSet<int>(Enumerable.Repeat(1, int.MaxValue)).Count; }")]
        [InlineData("int run() { var set = new HashSet<int>(); set.UnionWith(Enumerable.Repeat(1, int.MaxValue)); return set.Count; }")]
        [InlineData("int run() { return Enumerable.Repeat(1, int.MaxValue).SequenceEqual(Enumerable.Repeat(1, int.MaxValue)) ? 1 : 0; }")]
        [InlineData("int run() { IEnumerable<int> s = Enumerable.Repeat(1, int.MaxValue); return s?.Max() ?? 0; }")]
        [InlineData("int run() { return (from x in Enumerable.Repeat(1, int.MaxValue) where x == 2 select x).Count(); }")]
        [InlineData("int run() { return Enumerable.Repeat(1, int.MaxValue).Cast<int>().Max(); }")]
        [InlineData("int run() { return new[] { 1 }.SelectMany(x => Enumerable.Repeat(1, int.MaxValue)).Count(); }")]
        [InlineData("long run() { long total = 0; foreach (var n in Enumerable.Repeat(1, int.MaxValue).Distinct()) total += n; return total; }")]
        public async Task LibraryLoopsOverLongSequencesStopAtTheLimit(string code)
        {
            var compiled = CompileValid(code);
            var probe = CompileValid("int run() { return 7; }");
            using var executor = new ScriptExecutor(workerThreads: 1, "test script worker");

            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(executor, compiled, ShortLimit));
            // The probe needs the pool's only thread, so it finishes only after the stopped script
            // has really let go of it.
            Assert.Equal(7, (int)await Run(executor, probe, LongLimit));
            watch.Stop();

            Assert.True(watch.Elapsed < ShortLimit + TimeSpan.FromSeconds(1), $"The worker was busy for {watch.Elapsed}.");
        }

        [Fact]
        public async Task GuardedSequencesKeepTheirBehavior()
        {
            const string code = @"
class Bag : IEnumerable<int>
{
    private readonly List<int> items = new List<int> { 1, 2, 3 };
    public IEnumerator<int> GetEnumerator() => items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
T FirstOf<T>(IEnumerable<T> items) => items.First();
int run()
{
    var list = new List<int> { 3, 1, 2 };
    list.AddRange(list);
    IEnumerable<int> seq = list;
    var squares = new List<int>(from x in seq where x > 1 select x * x);
    var joined = string.Join("","", seq.OrderBy(x => x).ThenByDescending(x => x).Distinct());
    var pairs = from a in new[] { 1, 2 } from b in Enumerable.Range(0, 2) select a * 10 + b;
    var joins = from a in list join b in new[] { 1, 3 } on a equals b select a;
    object[] boxed = { 1, ""x"", 2 };
    int ints = boxed.OfType<int>().Sum() + boxed.Where(o => o is int).Cast<int>().Count();
    IEnumerable<int> none = null;
    int nullSafe = none?.Sum() ?? -1;
    int zipped = seq.Zip(Enumerable.Range(0, 100), (a, b) => a + b).Take(3).Sum();
    var dict = seq.Distinct().ToDictionary(x => x, x => x * 2);
    var groups = seq.GroupBy(x => x % 2).Select(g => g.Sum()).OrderBy(x => x).ToList();
    var bag = new Bag();
    int bagTotal = bag.Sum() + bag.Where(x => x > 1).Count() + FirstOf(bag);
    var set = new HashSet<int>(seq.Select(x => x + 1));
    set.UnionWith(Enumerable.Range(10, 2));
    int named = Enumerable.Aggregate(seed: 0, source: seq, func: (a, b) => a + b);
    int concat = seq.Concat(new[] { 4 }).Count();
    return squares.Sum() + joined.Length + pairs.Sum() + joins.Count() + ints + nullSafe + zipped + dict[3]
        + groups[0] * 100 + bagTotal + set.Count + named + concat;
}";
            string instrumented = Instrument(code);
            // Lazy sequences and IEnumerable-typed values going into library code are guarded ...
            Assert.Contains("Enumerate<int>(seq).OrderBy", instrumented);
            Assert.Contains("Enumerate<int>(Enumerable.Range(10, 2))", instrumented);
            Assert.Contains("source: global::WildCat.Scripting.ScriptGuard.Enumerate<int>(seq)", instrumented);
            Assert.Contains("from x in global::WildCat.Scripting.ScriptGuard.Enumerate<int>(seq)", instrumented);
            Assert.Contains("EnumerateUntyped(", instrumented);
            // ... in-memory collections, arrays and the script's own types are not.
            Assert.Contains("list.AddRange(list)", instrumented);
            Assert.Contains("boxed.OfType<int>()", instrumented);
            Assert.Contains("bag.Sum()", instrumented);

            // squares 26, "1,2,3" 5, pairs 62, joins 4, ints 5, nullSafe -1, zipped 9, dict[3] 6,
            // groups [4, 8] so 400, bag 9, set 5, named 12, concat 7.
            using var executor = new ScriptExecutor(workerThreads: 1, "test script worker");
            Assert.Equal(26 + 5 + 62 + 4 + 5 - 1 + 9 + 6 + 400 + 9 + 5 + 12 + 7, (int)await Run(executor, CompileValid(code), LongLimit));
        }

        // The worker pool.

        [Fact]
        public async Task ScriptCodeRunsOnTheWorkerPoolAlsoAfterAnAwait()
        {
            using var executor = new ScriptExecutor(workerThreads: 2, "test script worker");
            var threads = new ConcurrentQueue<(string Name, bool IsThreadPoolThread)>();
            Func<int> record = () =>
            {
                threads.Enqueue((Thread.CurrentThread.Name, Thread.CurrentThread.IsThreadPoolThread));
                return 1;
            };
            var later = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var compiled = CompileValid("async Task<int> run() { var record = (Func<int>)args[0]; int n = record(); n += await (Task<int>)args[1]; return n + record(); }");

            var run = Run(executor, compiled, LongLimit, args: new object[] { record, later.Task });
            await WaitUntil(() => threads.Count == 1, TimeSpan.FromSeconds(10));
            // Completed from a thread-pool thread; the script continues on the worker pool anyway.
            await Task.Run(() => later.SetResult(5));

            Assert.Equal(7, (int)await run.WaitAsync(TestSetup.Timeout));
            Assert.Equal(2, threads.Count);
            Assert.All(threads, t =>
            {
                Assert.StartsWith("test script worker", t.Name);
                Assert.False(t.IsThreadPoolThread);
            });
        }

        [Fact]
        public async Task TheWorkerPoolRunsNoMoreScriptsAtOnceThanItHasThreads()
        {
            using var executor = new ScriptExecutor(workerThreads: 2, "test script worker");
            using var gate = new ManualResetEventSlim(false);
            Func<int> block = () => gate.Wait(TestSetup.Timeout) ? 1 : 0;
            var compiled = CompileValid("int run() { return ((Func<int>)args[0])(); }");

            var runs = Enumerable.Range(0, 5).Select(_ => Run(executor, compiled, LongLimit, args: new object[] { block })).ToList();
            await WaitUntil(() => executor.Pool.Running == 2 && executor.Pool.Queued == 3, TimeSpan.FromSeconds(10));
            // The shared thread pool is not involved: it still runs work at once.
            Assert.Equal(1, await Task.Run(() => 1).WaitAsync(TimeSpan.FromSeconds(5)));

            gate.Set();
            var results = await Task.WhenAll(runs).WaitAsync(TestSetup.Timeout);
            Assert.All(results, r => Assert.Equal(1, (int)r));
            Assert.Equal(2, executor.Pool.PeakRunning);
        }

        // The circuit breaker.

        [Fact]
        public async Task AGameWithTooManyOverdueRunsIsRefusedUntilTheyFinish()
        {
            using var executor = new ScriptExecutor(workerThreads: 4, "test script worker");
            using var gate = new ManualResetEventSlim(false);
            int calls = 0;
            // A call the guards cannot interrupt, like a long library call.
            Func<int> stuck = () =>
            {
                Interlocked.Increment(ref calls);
                return gate.Wait(TestSetup.Timeout) ? 1 : 0;
            };
            var blocking = CompileValid("int run() { return ((Func<int>)args[0])(); }");
            var quick = CompileValid("int run() { return 7; }");
            const int maxOverdue = 2;

            // Three runs of game a time out and stay stuck; up to the third one the breaker allows
            // them, since only more than two overdue runs trip it.
            for (int i = 1; i <= 3; i++)
            {
                await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(executor, blocking, ShortLimit, "a", maxOverdue, stuck));
                Assert.Equal(i, executor.OverdueRuns("a"));
            }

            var watch = Stopwatch.StartNew();
            var refused = await Assert.ThrowsAsync<ScriptThrottledException>(() => Run(executor, blocking, LongLimit, "a", maxOverdue, stuck));
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Refusing took {watch.Elapsed}.");
            Assert.Contains("3 earlier script runs", refused.Message);
            Assert.Equal(3, calls);

            // Another game still runs on the free worker.
            Assert.Equal(7, (int)await Run(executor, quick, LongLimit, "b", maxOverdue));

            // Once the stuck runs return, game a runs again.
            gate.Set();
            await WaitUntil(() => executor.OverdueRuns("a") == 0, TimeSpan.FromSeconds(10));
            Assert.Equal(7, (int)await Run(executor, quick, LongLimit, "a", maxOverdue));
        }

        [Fact]
        public async Task ARunThatWaitedForAWorkerPastItsLimitDoesNotStart()
        {
            using var executor = new ScriptExecutor(workerThreads: 1, "test script worker");
            using var gate = new ManualResetEventSlim(false);
            int calls = 0;
            Func<int> stuck = () =>
            {
                Interlocked.Increment(ref calls);
                return gate.Wait(TestSetup.Timeout) ? 1 : 0;
            };
            var blocking = CompileValid("int run() { return ((Func<int>)args[0])(); }");

            var first = Run(executor, blocking, LongLimit, args: new object[] { stuck });
            await WaitUntil(() => calls == 1, TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(executor, blocking, ShortLimit, args: new object[] { stuck }));

            gate.Set();
            Assert.Equal(1, (int)await first.WaitAsync(TestSetup.Timeout));
            await WaitUntil(() => executor.Pool.Queued == 0 && executor.Pool.Running == 0, TimeSpan.FromSeconds(10));
            Assert.Equal(1, calls);
        }
    }
}
