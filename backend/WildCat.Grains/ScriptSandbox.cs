using Orleans;
using WildCat.Interfaces;

namespace WildCat.Scripting
{
    /// <summary>
    /// Runs compiled script events under a <see cref="ScriptBudget"/>. Shared by
    /// <see cref="ScriptEventRunner"/> (the path game clients use) and
    /// <see cref="ScriptEventCompiler.Run"/>.
    /// </summary>
    internal static class ScriptSandbox
    {
        /// <summary>
        /// Compiles the event on this silo if it is not cached yet, then runs it for
        /// <paramref name="player"/>. Must be called from a grain turn: the script API sends its
        /// grain calls back to <paramref name="scheduler"/>, the calling grain's scheduler.
        /// </summary>
        public static async Task<object> RunEventAsync(IGrainFactory grainFactory, ScriptEventState info, ISession session,
            string player, object[] args, ScriptOptions options, TaskScheduler scheduler)
        {
            var source = ScriptSource.FromState(info);
            CompiledScript compiled;
            try
            {
                compiled = await ScriptCache.Shared.GetOrAdd(source.CacheKey, () => ScriptCompilationUtility.CompileAsync(source));
            }
            catch
            {
                // An invalid script comes back as a result and stays cached; an exception (a
                // compile timeout on a busy silo) may not happen next time, so it is not kept.
                ScriptCache.Shared.Remove(source.CacheKey);
                throw;
            }
            if (!compiled.IsValid)
                throw new InvalidOperationException($"The script event '{info.Name}' is not valid: {compiled.Validation.Summarize()}");

            args ??= Array.Empty<object>();
            if (args.Length != compiled.ParameterCount)
            {
                throw new ArgumentException(
                    $"The script event '{info.Name}' takes {compiled.ParameterCount} arguments, but {args.Length} were sent.", nameof(args));
            }

            Guid.TryParse(info.GameKey, out Guid gameKey);
            var budget = new ScriptBudget(info.Name, options.EffectiveTimeout, options.EffectiveMaxCallDepth);
            var api = new WildCatScriptApi(grainFactory, session, gameKey, player, scheduler, budget);
            var result = await RunAsync(compiled, new Globals { args = args, WildCat = api }, budget);
            if (!ScriptCompilationUtility.IsAllowedReturnValue(result))
            {
                throw new InvalidOperationException(
                    $"The script event '{info.Name}' returned a {result.GetType().Name}; events can return bool, byte, short, int, long, float, double, string or byte[].");
            }
            return result;
        }

        /// <summary>
        /// Starts the script on the thread pool, so CPU-bound script code never occupies the grain
        /// scheduler, and waits for it no longer than the budget. A fast script returns as soon as
        /// it finishes. When the deadline passes the caller gets <see cref="ScriptTimeoutException"/>
        /// at once; the script itself stops at its next guard check.
        /// </summary>
        public static async Task<object> RunAsync(CompiledScript compiled, Globals globals, ScriptBudget budget)
        {
            var runner = compiled.Runner;
            Task<object> run = Task.Run(() => ScriptGuard.Run(budget, () => runner(globals, budget.Token)));
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
                budget.ReleaseWhenDone(run);
            }
        }
    }
}
