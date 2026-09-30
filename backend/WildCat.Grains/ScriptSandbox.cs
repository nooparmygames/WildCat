using System.Text.RegularExpressions;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Scripting
{
    /// <summary>
    /// Runs compiled script events under a <see cref="ScriptBudget"/> on the process's
    /// <see cref="ScriptExecutor"/>. Shared by <see cref="ScriptEventRunner"/> (the path game
    /// clients use) and <see cref="ScriptEventCompiler.Run"/>.
    /// </summary>
    internal static class ScriptSandbox
    {
        private static readonly Regex ParameterSuffix = new Regex(@" \(Parameter '[^']*'\)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Compiles the event on this silo if it is not cached yet, then runs it for
        /// <paramref name="player"/>. Must be called from a grain turn: the script API sends its
        /// grain calls back to <paramref name="scheduler"/>, the calling grain's scheduler.
        /// </summary>
        /// <remarks>
        /// Failures of the script or its request come out as <see cref="ScriptFailedException"/>
        /// with a message for the client (see <see cref="IsScriptFailure"/>); the time limit,
        /// call depth and throttling keep their own exceptions. An exception a grain call of the
        /// script API threw stays what it is even when its type is one scripts use (a grain's
        /// InvalidOperationException, say), unless the script caught it and threw its own.
        /// </remarks>
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
            {
                throw new ScriptFailedException($"The script event '{info.Name}' is not valid: {compiled.Validation.Summarize()}",
                    typeof(InvalidOperationException).FullName);
            }

            args ??= Array.Empty<object>();
            if (args.Length != compiled.ParameterCount)
            {
                throw new ScriptFailedException(
                    $"The script event '{info.Name}' takes {compiled.ParameterCount} arguments, but {args.Length} were sent.",
                    typeof(ArgumentException).FullName);
            }

            Guid.TryParse(info.GameKey, out Guid gameKey);
            var budget = new ScriptBudget(info.Name, options.EffectiveTimeout, options.EffectiveMaxCallDepth);
            var api = new WildCatScriptApi(grainFactory, session, gameKey, player, scheduler, budget);
            var executor = ScriptExecutor.GetShared(options);
            object result;
            try
            {
                result = await executor.RunAsync(compiled, new Globals { args = args, WildCat = api }, budget,
                    info.GameKey, options.EffectiveMaxOverdueRunsPerGame);
            }
            catch (Exception ex) when (IsScriptFailure(ex) && !api.IsServerFault(ex))
            {
                throw new ScriptFailedException(TextFor(ex), ex.GetType().FullName);
            }
            if (!ScriptCompilationUtility.IsAllowedReturnValue(result))
            {
                throw new ScriptFailedException(
                    $"The script event '{info.Name}' returned a {result.GetType().Name}; events can return bool, byte, short, int, long, float, double, string or byte[].",
                    typeof(InvalidOperationException).FullName);
            }
            return result;
        }

        /// <summary>
        /// Whether <paramref name="ex"/>, coming out of a script run, has a message written for the
        /// developer and the client: one the script threw itself (scripts can create Exception,
        /// InvalidOperationException and ArgumentException), a wrong argument type (the generated
        /// call line's InvalidCastException) or a refusal of the script API (ArgumentException,
        /// UnauthorizedAccessException). Exceptions from the script API's grain calls are ruled out
        /// separately (<see cref="WildCatScriptApi.IsServerFault"/>).
        /// </summary>
        internal static bool IsScriptFailure(Exception ex)
        {
            if (ex is ScriptTimeoutException || ex is ScriptThrottledException || ex is ScriptRecursionLimitException
                || ex is ScriptEventNotFoundException || ex is ScriptFailedException)
                return false;
            return ex is InvalidOperationException
                || ex is ArgumentException
                || ex is InvalidCastException
                || ex is UnauthorizedAccessException
                || ex.GetType() == typeof(Exception);
        }

        /// <summary>
        /// The exception's message, without the " (Parameter 'name')" .NET appends to an
        /// ArgumentException's.
        /// </summary>
        private static string TextFor(Exception ex)
        {
            return ex is ArgumentException ? ParameterSuffix.Replace(ex.Message, "") : ex.Message;
        }

        /// <summary>
        /// Runs a compiled script on the shared worker pool without a game (no circuit breaker);
        /// see <see cref="ScriptExecutor.RunAsync"/>. CPU-bound script code never occupies the grain
        /// scheduler or the thread pool.
        /// </summary>
        public static Task<object> RunAsync(CompiledScript compiled, Globals globals, ScriptBudget budget)
        {
            return ScriptExecutor.GetShared(null).RunAsync(compiled, globals, budget, gameKey: "", maxOverdueRuns: int.MaxValue);
        }
    }
}
