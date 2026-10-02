using WildCat.Interfaces;
using Microsoft.Extensions.Options;
using Orleans;
using System;
using System.Threading.Tasks;

namespace WildCat.Scripting
{
    /// <summary>
    /// The globals object a script runs against: scripts see <c>args</c> and <c>WildCat</c>.
    /// </summary>
    public sealed class Globals
    {
        /// <summary>
        /// The event's arguments, cast by the generated call line to the event's parameter types.
        /// They come from the game client and are untrusted.
        /// </summary>
        public object[] args;

        public WildCatScriptApi WildCat;
    }

    /// <summary>
    /// Runs a game's script events for one player (key: game key plus account id). The script
    /// source comes from the event's <see cref="IScriptEventCompiler"/> grain; the compiled
    /// version is cached on this silo by (game, event, script hash), so after the first run on a
    /// silo a run costs one GetInfo call plus the script itself.
    /// </summary>
    public class ScriptEventRunner : Grain, IScriptEventRunner
    {
        private readonly ScriptOptions options;
        private string player;
        private string gameKey;

        public ScriptEventRunner(IOptions<ScriptOptions> options)
        {
            this.options = options.Value;
            ScriptCache.Shared.Capacity = this.options.CacheCapacity;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var gameKeyGuid = this.GetPrimaryKey(out player);
            gameKey = gameKeyGuid.ToString();
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<object> Run(ISession session, string eventName, params object[] args)
        {
            var compiler = GrainFactory.GetGrain<IScriptEventCompiler>(ResourcesUtility.GetEventScriptKey(gameKey, eventName));
            var info = await compiler.GetInfo();
            return await ScriptSandbox.RunEventAsync(GrainFactory, info, session, player, args, options, TaskScheduler.Current);
        }
    }
}
