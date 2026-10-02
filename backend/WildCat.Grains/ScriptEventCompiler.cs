using WildCat.Grains;
using WildCat.Interfaces;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
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
    public class ScriptEventCompiler : Grain, IScriptEventCompiler
    {
        private readonly IPersistentState<ScriptEventState> state;

        private ScriptEventState State => state.State;

        private readonly ScriptOptions options;

        public ScriptEventCompiler(
            [PersistentState(GrainStateNames.ScriptEventCompiler, GrainStorageNames.Default)] IPersistentState<ScriptEventState> state,
            IOptions<ScriptOptions> options)
        {
            this.state = state;
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
                throw new ScriptEventNotFoundException($"The script event '{GetEventName()}' does not exist.");
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
                await state.WriteStateAsync();
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
            await state.WriteStateAsync();
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

        public Task<ValidationResult> Validate(string scriptText)
        {
            return ValidateAgainst(State.Parameters, scriptText);
        }

        public Task<ValidationResult> ValidateWith(List<EventParameter> parameters, string scriptText)
        {
            return ValidateAgainst(parameters ?? new List<EventParameter>(), scriptText);
        }

        public async Task Delete()
        {
            if (State.IsCreated)
                ScriptCache.Shared.Remove(ScriptSource.FromState(State).CacheKey);
            await state.ClearStateAsync();
            // Providers differ in what they leave in State after a clear; start from an empty event.
            state.State = new ScriptEventState();
        }

        private async Task<ValidationResult> ValidateAgainst(List<EventParameter> parameters, string scriptText)
        {
            var result = await ScriptCompilationUtility.ValidateAsync(
                new ScriptSource(GetGameKey(), GetEventName(), parameters, scriptText));
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
