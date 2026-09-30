using Interfaces;
using Orleans;
using System;
using System.Threading.Tasks;
using Collections;
public class Globals
{
    public object[] args;
    public ScriptingAPI.Apadana Apadana;
}

namespace ScriptingAPI
{
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
                ScriptingAPI.Apadana apadana = new ScriptingAPI.Apadana(this.GrainFactory, session, player, TaskScheduler.Current);
                Globals globals = new Globals { args = args, Apadana = apadana };
                return await ScriptCompilationUtility.RunScript(script, globals);
            }
            catch (Exception x)
            {
                throw x;
            }
        }

        
    }
}
