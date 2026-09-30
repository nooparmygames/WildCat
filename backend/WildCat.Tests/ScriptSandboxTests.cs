using System.Diagnostics;
using WildCat.Interfaces;
using WildCat.Scripting;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// The script sandbox without a cluster: validator rules, guard instrumentation, time and
    /// depth limits, diagnostics and the compiled script cache. Scripts here declare an event
    /// method named "run".
    /// </summary>
    public class ScriptSandboxTests
    {
        private static readonly TimeSpan ShortLimit = TimeSpan.FromMilliseconds(400);

        private static List<EventParameter> Parameters(params EventParameter.ParamType[] types)
        {
            return types.Select(t => new EventParameter { Type = t }).ToList();
        }

        private static CompiledScript Compile(string code, params EventParameter.ParamType[] parameters)
        {
            return ScriptCompilationUtility.Compile(new ScriptSource("game", "run", Parameters(parameters), code));
        }

        private static CompiledScript CompileValid(string code, params EventParameter.ParamType[] parameters)
        {
            var compiled = Compile(code, parameters);
            Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Validation.Diagnostics));
            return compiled;
        }

        private static ScriptValidationResult Validate(string code, params EventParameter.ParamType[] parameters)
        {
            return ScriptCompilationUtility.Analyze(new ScriptSource("game", "run", Parameters(parameters), code)).Validation;
        }

        private static Task<object> Run(CompiledScript compiled, TimeSpan limit, int maxDepth = 64, params object[] args)
        {
            return ScriptSandbox.RunAsync(compiled, new Globals { args = args }, new ScriptBudget("run", limit, maxDepth));
        }

        private static Task<object> Run(CompiledScript compiled, params object[] args)
        {
            return Run(compiled, TimeSpan.FromSeconds(5), 64, args);
        }

        private static void AssertRejected(ScriptValidationResult result, string fragment)
        {
            Assert.False(result.Success, "The script should have been rejected.");
            Assert.Contains(result.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error
                && (d.Message.Contains(fragment, StringComparison.Ordinal) || d.Id == fragment));
        }

        // Validator: allowed scripts.

        [Fact]
        public void AllowsCollectionsLinqAndOwnTypes()
        {
            var result = Validate(@"
class Reward { public string Name; public int Count; public override string ToString() => Name + "":"" + Count; }
struct Point { public int X; public int Y; }
enum Tier { Bronze, Silver, Gold }
int run(int a)
{
    var list = new List<int> { 1, 2, 3, a };
    var map = new Dictionary<string, int> { [""x""] = 1 };
    foreach (var pair in map) list.Add(pair.Value);
    var set = new HashSet<string>(map.Keys);
    var rewards = list.Where(x => x > 1).Select(x => new Reward { Name = ""coin"", Count = x }).ToList();
    var sb = new System.Text.StringBuilder();
    sb.Append($""{rewards.Count} rewards"");
    (int, string) tuple = (1, ""one"");
    var p = new Point { X = 1, Y = 2 };
    int[] arr = { 3, 1, 2 };
    Array.Sort(arr);
    var tier = Tier.Gold;
    double d = Math.Max(1.5, a) + Random.Shared.Next(1);
    string s = tier.ToString() + DateTime.UtcNow.Year + TimeSpan.FromSeconds(1).TotalSeconds + Guid.NewGuid() + arr.Length + args.Length;
    Func<int, int> twice = x => x * 2;
    return list.Sum() + set.Count + twice(tuple.Item1) + p.X + (int)d + sb.Length + s.Length;
}", EventParameter.ParamType.IntParam);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.True(result.HasReturnValue);
        }

        [Fact]
        public void AllowsTheWildCatApiAndAwaitingTasks()
        {
            var result = Validate(@"
async Task<int> run()
{
    await WildCat.SetPlayerData(""k"", ""v"");
    string v = await WildCat.GetPlayerData(WildCat.CurrentPlayer, ""k"");
    var top = await WildCat.GetTopEntriesInLeaderBoard(""global"", 0, 5);
    var names = top.entries.Select(e => e.userName).ToList();
    await Task.WhenAll(WildCat.SetPublicPlayerData(""a"", ""1""), WildCat.SetPublicPlayerData(""b"", ""2""));
    var data = WildCat.DeserializeObjectFromJson<Dictionary<string, int>>(""{\""a\"":1}"");
    return names.Count + data[""a""] + (v ?? """").Length + (await Task.FromResult(1));
}");
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        [Fact]
        public void AllowsMethodGroupsThatDoNotReadASequence()
        {
            // Library method groups without a sequence parameter, and the script's own methods
            // (whose bodies are guarded), can still be passed as delegates.
            var result = Validate(@"
int Total(IEnumerable<int> items) { int t = 0; foreach (var i in items) t += i; return t; }
int run()
{
    Func<int, int> abs = Math.Abs;
    Func<IEnumerable<int>, int> total = Total;
    return new List<int> { -1, 2 }.Select(Math.Abs).Sum() + abs(-2) + total(new[] { 1, 2 });
}");
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        // Validator: rejected escapes, one per test case.

        [Theory]
        [InlineData("int run() { return new List<int>().GetType().Name.Length; }", "GetType")]
        [InlineData("class MyError : Exception { } int run() { return 0; }", "derive from Exception")]
        [InlineData("int run() { throw new InvalidOperationException(\"a\", new Exception()); }", "InvalidOperationException..ctor")]
        [InlineData("int run() { var t = typeof(string); return 0; }", "System.Type")]
        [InlineData("int run() { return System.IO.File.ReadAllText(\"/etc/passwd\").Length; }", "System.IO.File")]
        [InlineData("int run() { System.Diagnostics.Process.Start(\"sh\"); return 0; }", "System.Diagnostics.Process")]
        [InlineData("int run() { System.Threading.Thread.Sleep(10000); return 0; }", "System.Threading.Thread")]
        [InlineData("int run() { var o = Activator.CreateInstance<List<int>>(); return 0; }", "System.Activator")]
        [InlineData("int run() { dynamic d = 1; return d; }", "dynamic")]
        [InlineData("int run() { unsafe { int x = 1; int* p = &x; return *p; } }", WildCatErrorIds.Construct)]
        [InlineData("[System.Runtime.InteropServices.DllImport(\"libc\")] static extern int getpid(); int run() { return getpid(); }", WildCatErrorIds.Construct)]
        [InlineData("int run() { return typeof(string).GetMethods().Length; }", "System.Type")]
        [InlineData("int run() { Environment.Exit(1); return 0; }", "System.Environment")]
        [InlineData("int run() { Task.Run(() => 1); return 0; }", "Task.Run")]
        [InlineData("int run() { return Task.FromResult(1).Result; }", "Task.Result")]
        [InlineData("int run() { Task.Delay(100000).Wait(); return 0; }", "Task.Delay")]
        [InlineData("int run() { lock (args) { return 0; } }", "lock")]
        [InlineData("async void Boom() { await Task.CompletedTask; } int run() { Boom(); return 0; }", WildCatErrorIds.AsyncVoid)]
        [InlineData("int run() { var l = new List<int> { 1 }; l.ForEach(async x => await Task.CompletedTask); return 0; }", WildCatErrorIds.AsyncVoid)]
        [InlineData("static int counter; int run() { return ++counter; }", WildCatErrorIds.StaticState)]
        [InlineData("class C { static List<int> shared = new List<int>(); } int run() { return 0; }", WildCatErrorIds.StaticState)]
        [InlineData("record R(int X); int run() { return new R(1).X; }", "Records")]
        [InlineData("int run() { var a = new { X = 1 }; return a.X; }", "Anonymous types")]
        [InlineData("class C { ~C() { } } int run() { return 0; }", "Finalizers")]
        [InlineData("int run() { var kv = new KeyValuePair<string, object>(\"a\", null); return 0; }", "cannot be object")]
        [InlineData("int run() { (object, int) t = (null, 1); return t.Item2; }", "cannot be object")]
        [InlineData("struct Node { public object Next; } int run() { return 0; }", "script struct")]
        [InlineData("int run() { Span<int> s = stackalloc int[10]; return s[0]; }", WildCatErrorIds.Construct)]
        [InlineData("int run() { return System.Reflection.Assembly.GetExecutingAssembly().GetTypes().Length; }", "System.Reflection.Assembly")]
        [InlineData("int run() { var f = new Func<int>(() => 1); return f.Method.Name.Length; }", "Method")]
        [InlineData("int run() { return WildCat.GetType().Name.Length; }", "GetType")]
        [InlineData("int run() { System.GC.Collect(); return 0; }", "System.GC")]
        [InlineData("int run() { var x = new System.Threading.Tasks.Task(() => { }); return 0; }", "Task..ctor")]
        [InlineData("int run() { return ScriptGuard.Probe() ? 1 : 0; }", "WildCat.Scripting.ScriptGuard")]
        [InlineData("int run() { return ScriptGuard.Enumerate(Enumerable.Range(0, 3)).Count(); }", "WildCat.Scripting.ScriptGuard")]
        [InlineData("int run() { Func<IEnumerable<int>, int> sum = Enumerable.Sum; return sum(Enumerable.Repeat(1, int.MaxValue)); }", "method group")]
        [InlineData("int run() { var lists = new List<IEnumerable<int>>(); return lists.Select(Enumerable.Count).Sum(); }", "method group")]
        [InlineData("int run() { Func<int> count = Enumerable.Repeat(1, int.MaxValue).Count; return count(); }", "method group")]
        [InlineData("int run() { var f = new Func<IEnumerable<string>, string>(string.Concat); return f(new[] { \"a\" }); }", "method group")]
        public void RejectsEscapes(string code, string expected)
        {
            AssertRejected(Validate(code), expected);
        }

        [Theory]
        [InlineData("#r \"System.IO.FileSystem.dll\"\nint run() { return 0; }")]
        [InlineData("#load \"/etc/passwd\"\nint run() { return 0; }")]
        [InlineData("int run() {\n#line 1 \"x\"\nreturn 0; }")]
        public void RejectsDirectivesBeforeCompiling(string code)
        {
            AssertRejected(Validate(code), ScriptValidator.DirectiveNotAllowed);
        }

        [Fact]
        public void RejectsEventNamesThatAreNotIdentifiers()
        {
            var result = ScriptCompilationUtility.Analyze(new ScriptSource("game", "run(); System.IO.File.Delete(\"x\"); //", Parameters(), "int run() { return 0; }")).Validation;
            AssertRejected(result, ScriptCompilationUtility.InvalidEventName);
        }

        // Diagnostics.

        [Fact]
        public void DiagnosticsUseLinesOfTheDevelopersScript()
        {
            var result = Validate("int run()\n{\n    int a = 1;\n    return b;\n}");
            var error = Assert.Single(result.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);
            Assert.Equal("CS0103", error.Id);
            Assert.Equal(4, error.Line);
            Assert.Equal(12, error.Column);
        }

        [Fact]
        public void SandboxDiagnosticsUseLinesOfTheDevelopersScript()
        {
            var result = Validate("int run()\n{\n  var p = System.IO.Path.GetTempPath();\n  return 0;\n}");
            Assert.False(result.Success);
            Assert.All(result.Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error), d => Assert.Equal(3, d.Line));
            Assert.Contains(result.Diagnostics, d => d.Id == ScriptValidator.TypeNotAllowed && d.Column == 11);
        }

        [Fact]
        public void ParameterMismatchIsReportedOnTheGeneratedLine()
        {
            var result = Validate("int run(int a, int b) { return a + b; }", EventParameter.ParamType.IntParam);
            var error = Assert.Single(result.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);
            Assert.Equal(0, error.Line);
            Assert.Contains("generated call", error.Message);
        }

        [Fact]
        public void EventMethodsReturnWireTypesOnly()
        {
            AssertRejected(Validate("class Box { } Box run() { return new Box(); }"), ScriptCompilationUtility.ReturnTypeNotAllowed);
            AssertRejected(Validate("async Task<List<int>> run() { return await Task.FromResult(new List<int>()); }"), ScriptCompilationUtility.ReturnTypeNotAllowed);
            Assert.True(Validate("async Task<byte[]> run() { return await Task.FromResult(new byte[1]); }").Success);
            Assert.True(Validate("void run() { }").Success);
        }

        [Fact]
        public void MissingEventMethodIsReported()
        {
            AssertRejected(Validate("int other() { return 0; }"), ScriptCompilationUtility.EventMethodMissing);
        }

        [Fact]
        public void WarningsDoNotFailValidation()
        {
            var result = Validate("int run()\n{\n    int unused = 1;\n    return 1;\n}");
            Assert.True(result.Success);
            var warning = Assert.Single(result.Diagnostics);
            Assert.Equal("CS0219", warning.Id);
            Assert.Equal(ScriptDiagnosticSeverity.Warning, warning.Severity);
            Assert.Equal(3, warning.Line);
            Assert.Equal(9, warning.Column);
        }

        // Instrumented scripts still behave the same.

        [Fact]
        public async Task InstrumentedMembersKeepTheirBehavior()
        {
            var compiled = CompileValid(@"
class Counter
{
    private int value;
    public List<int> Items = new();
    public int[] Seeds = { 1, 2 };
    public List<int> More = [3, 4];
    public Func<int, int> Twice = x => x * 2;
    public int Value => value;
    public int this[int i] => Items[i];
    public int Double { get => value * 2; set => this.value = value / 2; }
    public Counter() => value = 1;
    public Counter(int start) { value = start; }
    public void Add(int x) => Items.Add(x);
    public static Counter operator +(Counter c, int x) => new Counter(c.value + x);
    public static implicit operator int(Counter c) => c.value;
    public IEnumerable<int> Evens() { for (int i = 0; i < 6; i++) if (i % 2 == 0) yield return i; }
    public int Fail() => throw new InvalidOperationException(""no"");
}
async Task Nothing() => await Task.CompletedTask;
async Task<int> Three() => await Task.FromResult(3);
async Task<int> run(int a)
{
    var c = new Counter();
    c.Add(a);
    int first = c[0];
    c.Double = 10;
    c = c + 2;
    int total = 0;
    foreach (var e in c.Evens()) total += e;
    int Local(int x) => x + 1;
    Func<int, int> square = x => x * x;
    Action<int> add = x => total += x;
    add(1);
    Func<Task<int>> later = async () => await Three();
    Func<Task> none = async () => await Nothing();
    Func<int, int> anonymous = delegate (int x) { return x; };
    int i = 0;
    do { i++; } while (i < 3);
    while (i < 5) i++;
    for (;;) { if (i >= 6) break; i++; }
    again:
    if (i < 7) { i++; goto again; }
    switch (i) { case 7: goto default; default: i++; break; }
    await none();
    var list = new List<int> { 3, 1, 2 };
    list.Sort((x, y) => x.CompareTo(y));
    return c + total + Local(1) + square(2) + await later() + i + c.Seeds.Length + c.More.Count
        + new Counter().Items.Count + first + c.Value + anonymous(1) + list[0] + c.Twice(1);
}", EventParameter.ParamType.IntParam);

            // c: Double = 10 sets value 5, plus 2 = 7; total: 0 + 2 + 4 evens plus 1 = 7;
            // Local(1) = 2; square(2) = 4; later = 3; i = 8; seeds 2; more 2; new items 0;
            // first = a = 4; c.Value = 7; anonymous = 1; list[0] = 1; Twice(1) = 2.
            object result = await Run(compiled, 4);
            Assert.Equal(7 + 7 + 2 + 4 + 3 + 8 + 2 + 2 + 0 + 4 + 7 + 1 + 1 + 2, (int)result);
        }

        [Fact]
        public async Task ThrowExpressionBodiesStillThrow()
        {
            var compiled = CompileValid("int Fail() => throw new InvalidOperationException(\"no\"); void Nothing() => throw new ArgumentException(\"x\"); int run() { try { Nothing(); } catch (Exception e) { if (e.Message != \"x\") return 0; } return Fail(); }");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(compiled));
            Assert.Equal("no", ex.Message);
        }

        // Time and depth limits.

        [Theory]
        [InlineData("int run() { while (true) { } }")]
        [InlineData("int run() { for (;;) ; }")]
        [InlineData("int run() { int i = 0; do { i++; } while (true); }")]
        [InlineData("int run() { long n = 0; foreach (var x in Enumerable.Repeat(1, int.MaxValue)) { n += x; } return (int)n; }")]
        [InlineData("int run() { top: goto top; }")]
        [InlineData("int run() { int x = 0; switch (x) { case 0: goto case 1; case 1: goto case 0; } return 0; }")]
        [InlineData("int run() { while (true) { try { while (true) { } } catch { } } }")]
        [InlineData("int run() { int n = 0; try { while (true) n++; } catch { } while (true) { try { n++; } catch { } } }")]
        [InlineData("async Task<int> run() { while (true) { await Task.FromResult(1); } }")]
        [InlineData("int run() { var all = Enumerable.Range(0, int.MaxValue).Select(x => x + 1).Count(); return all; }")]
        public async Task EndlessScriptsStopAtTheLimit(string code)
        {
            var compiled = CompileValid(code);
            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(compiled, ShortLimit));
            watch.Stop();
            Assert.True(watch.Elapsed < ShortLimit + TimeSpan.FromSeconds(1), $"Took {watch.Elapsed}.");
        }

        [Fact]
        public async Task AStoppedScriptDoesNotKeepRunning()
        {
            var compiled = CompileValid("int run() { int[] box = (int[])args[0]; while (true) { box[0]++; } }");
            var box = new int[1];
            await Assert.ThrowsAsync<ScriptTimeoutException>(() => Run(compiled, ShortLimit, 64, box));
            await Task.Delay(200);
            int afterStop = box[0];
            await Task.Delay(300);
            Assert.Equal(afterStop, box[0]);
        }

        [Theory]
        [InlineData("int F(int n) { return F(n + 1); } int run() { return F(0); }")]
        [InlineData("int F(int n) => F(n + 1); int run() { return F(0); }")]
        [InlineData("class C { public int P => P + 1; } int run() { return new C().P; }")]
        [InlineData("class C { C next = new C(); } int run() { new C(); return 0; }")]
        [InlineData("int run() { Func<int, int> f = null; f = x => f(x + 1); return f(0); }")]
        [InlineData("IEnumerable<int> F() { foreach (var x in F()) yield return x; yield return 1; } int run() { return F().First(); }")]
        [InlineData("async Task<int> F(int n) { await Task.CompletedTask; return await F(n + 1); } async Task<int> run() { return await F(0); }")]
        [InlineData("int run() { int L(int n) => L(n + 1); return L(0); }")]
        public async Task DeepRecursionIsStoppedBeforeTheStackRunsOut(string code)
        {
            var compiled = CompileValid(code);
            await Assert.ThrowsAsync<ScriptRecursionLimitException>(() => Run(compiled, TimeSpan.FromSeconds(10), maxDepth: 200));
        }

        [Fact]
        public async Task RecursionWithinTheLimitWorks()
        {
            var compiled = CompileValid("int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2); int run(int n) { return Fib(n); }", EventParameter.ParamType.IntParam);
            Assert.Equal(55, (int)await Run(compiled, 10));
        }

        [Fact]
        public async Task AFastScriptReturnsAtOnce()
        {
            var compiled = CompileValid("int run(int a, int b) { return a + b; }", EventParameter.ParamType.IntParam, EventParameter.ParamType.IntParam);
            await Run(compiled, 1, 1);
            var watch = Stopwatch.StartNew();
            object result = await Run(compiled, 2, 3);
            watch.Stop();
            Assert.Equal(5, (int)result);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"Took {watch.Elapsed}.");
        }

        [Fact]
        public async Task ScriptCodeOutsideTheSandboxThrows()
        {
            var compiled = CompileValid("public class Box { public int Get() { return 1; } } void run() { ((object[])args[0])[0] = new Box(); }");
            var holder = new object[1];
            await Run(compiled, (object)holder);
            object box = holder[0];
            var get = box.GetType().GetMethod("Get");
            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => get.Invoke(box, null));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        // Cache.

        [Fact]
        public async Task CacheCompilesEachKeyOnceAndEvictsTheLeastRecentlyUsed()
        {
            var cache = new ScriptCache(2);
            int compiles = 0;
            Func<Task<CompiledScript>> compile = () =>
            {
                Interlocked.Increment(ref compiles);
                return Task.Run(() => CompileValid("int run() { return 1; }"));
            };
            var a = new ScriptCacheKey("g", "a", "1");
            var b = new ScriptCacheKey("g", "b", "1");
            var c = new ScriptCacheKey("g", "c", "1");

            var first = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.GetOrAdd(a, compile))));
            Assert.Equal(1, compiles);
            Assert.All(first, compiled => Assert.Same(first[0], compiled));

            await cache.GetOrAdd(b, compile);
            await cache.GetOrAdd(a, compile);
            await cache.GetOrAdd(c, compile);
            Assert.Equal(3, compiles);
            Assert.True(cache.Contains(a));
            Assert.False(cache.Contains(b));
            Assert.True(cache.Contains(c));
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void EditedScriptsGetANewHash()
        {
            var parameters = Parameters(EventParameter.ParamType.IntParam);
            string one = ScriptCompilationUtility.ComputeScriptHash("run", parameters, "int run(int a) { return a; }");
            string same = ScriptCompilationUtility.ComputeScriptHash("run", parameters, "int run(int a) { return a; }");
            string edited = ScriptCompilationUtility.ComputeScriptHash("run", parameters, "int run(int a) { return a + 1; }");
            string otherParameters = ScriptCompilationUtility.ComputeScriptHash("run", Parameters(EventParameter.ParamType.LongParam), "int run(int a) { return a; }");
            Assert.Equal(one, same);
            Assert.NotEqual(one, edited);
            Assert.NotEqual(one, otherParameters);
        }
    }

    internal static class WildCatErrorIds
    {
        public const string Construct = ScriptValidator.ConstructNotAllowed;
        public const string AsyncVoid = ScriptValidator.AsyncVoidNotAllowed;
        public const string StaticState = ScriptValidator.StaticStateNotAllowed;
    }
}
