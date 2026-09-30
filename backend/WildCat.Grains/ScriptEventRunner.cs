using WildCat.Interfaces;
using Orleans;
using System;
using System.Threading.Tasks;
using WildCat.Grains;

namespace WildCat.Scripting
{
    /// <summary>
    /// The globals object a script runs against: scripts see <c>args</c> and <c>WildCat</c>.
    /// </summary>
    public class Globals
    {
        public object[] args;
        public WildCatScriptApi WildCat;
    }

    public class ScriptEventRunner : Grain, IScriptEventRunner
    {
        private string player;
        private string gameKey;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var gameKeyGuid = this.GetPrimaryKey(out player);
            gameKey = gameKeyGuid.ToString();
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<object> Run(ISession session, string eventName, params object[] args)
        {
            try
            {
                var script = await ScriptCompilationUtility.GetScript(this.GrainFactory,gameKey,eventName);
                WildCatScriptApi api = new WildCatScriptApi(this.GrainFactory, session, player, TaskScheduler.Current);
                Globals globals = new Globals { args = args, WildCat = api };
                return await ScriptCompilationUtility.RunScript(script, globals);
            }
            catch (Exception x)
            {
                throw x;
            }
        }

        
    }
}
