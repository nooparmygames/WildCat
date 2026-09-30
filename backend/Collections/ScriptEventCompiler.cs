using Interfaces;
using Microsoft.CodeAnalysis.Scripting;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Collections;

namespace ScriptingAPI
{
    public class ScriptEventCompiler : Grain<ScriptEventState>, IScriptEventCompiler
    {
        private bool _isCreated;
        private Script<object> _script;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _isCreated = State.IsCreated;
            if (_isCreated)
            {

                var validationResult = await Validate(State.Script);
                if (!validationResult.IsValid)
                    throw new InvalidOperationException("The script is not valid");
                await Compile(validationResult.HasReturnValue);
                await WriteStateAsync();
            }

            await base.OnActivateAsync(cancellationToken);
        }

        private void AssertIsCreated()
        {
            if (!_isCreated)
                throw new InvalidOperationException();
        }

        public async Task Edit(List<EventParameter> parameters, string script)
        {
            AssertIsCreated();
            State.Script = script;
            State.Parameters = parameters;

            var validationResult = await Validate(State.Script);
            if (!validationResult.IsValid)
                throw new InvalidOperationException("The script is not valid");
            State.Version++;
            await Compile(validationResult.HasReturnValue);
            await WriteStateAsync();
        }

        public Task<string> GetKey()
        {
            return Task.FromResult(this.GetPrimaryKeyString());
        }

        public Task<string> GetName()
        {
            return Task.FromResult(State.Name);
        }

        public async Task Initialize(string name, List<EventParameter> parameters, string script)
        {
            State.Name = name;
            State.Script = script;
            State.Parameters = parameters;
            State.IsCreated = true;
            _isCreated = true;
            string pk = this.GetPrimaryKeyString();
            State.GameKey = pk.Substring(0, pk.IndexOf("_G_"));
            var validationResult = await Validate(State.Script);
            if (!validationResult.IsValid)
                throw new InvalidOperationException("The script is not valid");
            if (!State.IsCreated)
                State.Version = 0;
            else
                State.Version++;
            await Compile(validationResult.HasReturnValue);
            State.IsCreated = true;
            _isCreated = true;
            await WriteStateAsync();
        }

        public async Task Compile(bool hasReturnType)
        {
            AssertIsCreated();
            _script = await ScriptCompilationUtility.CompileScript(State.Script, State.Name, State.Parameters, hasReturnType, State.GameKey, State.Version);
        }

        public async Task<object> Run(ISession session, string player, params object[] args)
        {
            AssertIsCreated();
            try
            {
                ScriptingAPI.Apadana apadana = new ScriptingAPI.Apadana(this.GrainFactory, session, player, TaskScheduler.Current);
                Globals globals = new Globals { args = args, Apadana = apadana };
                return await ScriptCompilationUtility.RunScript(_script, globals);
            }
            catch 
            {
                throw;
            }

        }



        public async Task<ValidationResult> Validate(string scriptText)
        {
            ValidationResult result =
             await ScriptCompilationUtility.ValidateEventScript(scriptText, State.Name);
            State.HasReturnValue = result.HasReturnValue;
            return result;
        }



        public Task<ScriptEventState> GetInfo()
        {
            AssertIsCreated();
            return Task.FromResult(State);
        }
    }
}
