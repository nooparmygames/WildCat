using WildCat.Interfaces;
using Microsoft.Extensions.Options;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace WildCat.Scripting
{
    /// <summary>
    /// Stores one script event and its diagnostics. Initialize and Edit validate and compile the
    /// new source before anything is stored, so a rejected script never replaces a working one;
    /// the compiled result goes into this silo's <see cref="ScriptCache"/>, and other silos compile
    /// from <see cref="GetInfo"/> on their first run.
    /// </summary>
    public class ScriptEventCompiler : Grain<ScriptEventState>, IScriptEventCompiler
    {
        private readonly ScriptOptions options;

        public ScriptEventCompiler(IOptions<ScriptOptions> options)
        {
            this.options = options.Value;
            ScriptCache.Shared.Capacity = this.options.CacheCapacity;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // Compiling happens on demand in the cache, so a script the current rules reject can
            // still be read and edited.
            if (State.IsCreated && string.IsNullOrEmpty(State.ScriptHash))
                State.ScriptHash = ScriptCompilationUtility.ComputeScriptHash(State.Name, State.Parameters, State.Script);
            return base.OnActivateAsync(cancellationToken);
        }

        private void AssertIsCreated()
        {
            if (!State.IsCreated)
                throw new InvalidOperationException($"The script event {this.GetPrimaryKeyString()} does not exist.");
        }

        public Task Initialize(string name, List<EventParameter> parameters, string script)
        {
            return Store(name, parameters, script);
        }

        public Task Edit(List<EventParameter> parameters, string script)
        {
            AssertIsCreated();
            return Store(State.Name, parameters, script);
        }

        private async Task Store(string name, List<EventParameter> parameters, string script)
        {
            parameters ??= new List<EventParameter>();
            var source = new ScriptSource(GetGameKey(), name, parameters, script);
            var compiled = await ScriptCompilationUtility.CompileAsync(source);
            State.LastValidation = compiled.Validation;
            if (!compiled.IsValid)
            {
                await WriteStateAsync();
                throw new InvalidOperationException($"The script is not valid: {compiled.Validation.Summarize()}");
            }

            ScriptCache.Shared.Set(source.CacheKey, compiled);
            State.Name = name;
            State.Script = script;
            State.Parameters = parameters;
            State.GameKey = source.GameKey;
            State.HasReturnValue = compiled.Validation.HasReturnValue;
            State.ScriptHash = source.Hash;
            State.Version++;
            State.IsCreated = true;
            await WriteStateAsync();
        }

        /// <summary>
        /// The event name: the stored one once the event exists, otherwise the part of the grain key
        /// after "_G_", so a script for a new event can be validated before Initialize succeeds.
        /// </summary>
        private string GetEventName()
        {
            if (!string.IsNullOrEmpty(State.Name))
                return State.Name;
            string key = this.GetPrimaryKeyString();
            int separator = key.IndexOf("_G_", StringComparison.Ordinal);
            return separator < 0 ? key : key.Substring(separator + 3);
        }

        private string GetGameKey()
        {
            string key = this.GetPrimaryKeyString();
            int separator = key.IndexOf("_G_", StringComparison.Ordinal);
            return separator < 0 ? key : key.Substring(0, separator);
        }

        public Task<string> GetKey()
        {
            return Task.FromResult(this.GetPrimaryKeyString());
        }

        public Task<string> GetName()
        {
            return Task.FromResult(State.Name);
        }

        public async Task Compile(bool hasReturnType)
        {
            AssertIsCreated();
            var source = ScriptSource.FromState(State);
            await ScriptCache.Shared.GetOrAdd(source.CacheKey, () => ScriptCompilationUtility.CompileAsync(source));
        }

        public Task<object> Run(ISession session, string player, params object[] args)
        {
            AssertIsCreated();
            return ScriptSandbox.RunEventAsync(GrainFactory, State, session, player, args, options, TaskScheduler.Current);
        }

        public async Task<ValidationResult> Validate(string scriptText)
        {
            var result = await ScriptCompilationUtility.ValidateAsync(
                new ScriptSource(GetGameKey(), GetEventName(), State.Parameters, scriptText));
            return new ValidationResult
            {
                IsValid = result.Success,
                ErrorMsg = result.Success ? null : result.Summarize(),
                HasReturnValue = result.HasReturnValue,
                Diagnostics = result.Diagnostics
            };
        }

        public Task<ScriptValidationResult> GetDiagnostics()
        {
            return Task.FromResult(State.LastValidation ?? new ScriptValidationResult { Success = State.IsCreated });
        }

        public Task<ScriptEventState> GetInfo()
        {
            AssertIsCreated();
            return Task.FromResult(State);
        }
    }
}
