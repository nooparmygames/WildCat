using System.Collections.Generic;
using System.Linq;

namespace WildCat.Interfaces
{
    /// <summary>
    /// The outcome of validating or compiling a script event: <see cref="Success"/> is false when
    /// any diagnostic is an error. Warnings are reported but do not block the script.
    /// </summary>
    [GenerateSerializer, Alias("wildcat.ScriptValidationResult")]
    public class ScriptValidationResult
    {
        [Id(0)] public bool Success { get; set; }
        [Id(1)] public List<ScriptDiagnostic> Diagnostics { get; set; } = new List<ScriptDiagnostic>();

        /// <summary>
        /// True when the event method returns a value (Task&lt;T&gt; or a non-void type).
        /// </summary>
        [Id(2)] public bool HasReturnValue { get; set; }

        /// <summary>
        /// The errors in one line, for exception messages and logs.
        /// </summary>
        public string Summarize(int maxErrors = 3)
        {
            var errors = Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error).ToList();
            if (errors.Count == 0)
                return "no errors";
            string text = string.Join("; ", errors.Take(maxErrors));
            return errors.Count > maxErrors ? $"{text}; and {errors.Count - maxErrors} more" : text;
        }
    }
}
