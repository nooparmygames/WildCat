using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Owns one script event of a game, keyed "{gameKey}_G_{eventName}"
    /// (<see cref="ResourcesUtility.GetEventScriptKey"/>). It stores the developer's C# source,
    /// validates it against the script sandbox rules and keeps the diagnostics of the last
    /// Initialize or Edit. Game clients run events through <see cref="IScriptEventRunner"/>; the
    /// arguments they send are untrusted input.
    /// </summary>
    public interface IScriptEventCompiler : IManagedResource
    {
        /// <summary>
        /// Creates or replaces the event. The script must declare a top-level method named
        /// <paramref name="name"/> whose parameters match <paramref name="parameters"/>. Throws
        /// InvalidOperationException when the script is not valid; <see cref="GetDiagnostics"/>
        /// then returns the reasons.
        /// </summary>
        Task Initialize(string name, List<EventParameter> parameters, string script);

        /// <summary>
        /// Replaces the parameters and source of an existing event. Throws
        /// InvalidOperationException when the script is not valid and keeps the previous version.
        /// </summary>
        Task Edit(List<EventParameter> parameters, string script);

        /// <summary>
        /// Runs the event for <paramref name="player"/> on <paramref name="session"/>, which must
        /// belong to that player and to this event's game once the script calls the WildCat API.
        /// Throws ScriptTimeoutException when the script runs past the configured limit.
        /// </summary>
        Task<object> Run(ISession session, string player, params object[] args);

        Task<ScriptEventState> GetInfo();

        /// <summary>
        /// Checks <paramref name="script"/> against the stored event name and parameters without
        /// storing it. <see cref="ValidationResult.Diagnostics"/> holds the structured reasons.
        /// </summary>
        Task<ValidationResult> Validate(string script);

        /// <summary>
        /// Compiles the stored script into this silo's script cache. Running an event compiles on
        /// demand, so this is only a warm-up. The argument is ignored; the return kind comes from
        /// the script itself.
        /// </summary>
        Task Compile(bool hasReturnValue);

        /// <summary>
        /// The result of the last Initialize or Edit, successful or not, with line and column
        /// numbers relative to the developer's script.
        /// </summary>
        Task<ScriptValidationResult> GetDiagnostics();
    }

    [GenerateSerializer]
    [Alias("wildcat.EventParameter")]
    public class EventParameter
    {
        [GenerateSerializer, Alias("wildcat.EventParameter.ParamType")]
        public enum ParamType { BoolParam, ByteParam, ShortParam, IntParam, LongParam, FloatParam, DoubleParam, StringParam, ByteArrayParam }
        [Id(0)] public ParamType Type { get; set; }
        [Id(1)] public string Name { get; set; }

        public string GetCastTypeString()
        {
            switch(Type)
            {
                case ParamType.BoolParam:
                    return "bool";
                case ParamType.ByteParam:
                    return "byte";
                case ParamType.ByteArrayParam:
                    return "byte[]";
                case ParamType.DoubleParam:
                    return "double";
                case ParamType.FloatParam:
                    return "float";
                case ParamType.IntParam:
                    return "int";
                case ParamType.LongParam:
                    return "long";
                case ParamType.ShortParam:
                    return "short";
                case ParamType.StringParam:
                    return "string";
                default:
                    throw new InvalidOperationException("Invalid parameter type");

            }
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.ScriptEventState")]
    public class ScriptEventState
    {
        [Id(0)] public bool IsCreated { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public string Script { get; set; }
        [Id(3)] public List<EventParameter> Parameters { get; set; }
        [Id(4)] public ulong Version { get; set; }
        [Id(5)] public bool HasReturnValue { get; set; }
        [Id(6)] public string GameKey { get; set; }

        /// <summary>
        /// SHA-256 of the event name, parameter types and source. Every silo caches compiled
        /// scripts under (game, event, hash), so a new version never hits a stale entry.
        /// </summary>
        [Id(7)] public string ScriptHash { get; set; }

        /// <summary>
        /// Diagnostics of the last Initialize or Edit, including a rejected one.
        /// </summary>
        [Id(8)] public ScriptValidationResult LastValidation { get; set; }
    }

    /// <summary>
    /// The legacy validation result. <see cref="Diagnostics"/> carries the same structured
    /// diagnostics as <see cref="ScriptValidationResult"/>; <see cref="ErrorMsg"/> summarizes
    /// the errors in one line.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ValidationResult")]
    public class ValidationResult
    {
        [Id(0)] public bool IsValid { get; set; }
        [Id(1)] public string ErrorMsg { get; set; }
        [Id(2)] public bool HasReturnValue { get; set; }
        [Id(3)] public List<ScriptDiagnostic> Diagnostics { get; set; } = new List<ScriptDiagnostic>();
    }
}
