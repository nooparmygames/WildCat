namespace WildCat.Interfaces
{
    /// <summary>
    /// How serious a <see cref="ScriptDiagnostic"/> is. The values follow Roslyn's
    /// DiagnosticSeverity.
    /// </summary>
    [GenerateSerializer, Alias("wildcat.ScriptDiagnosticSeverity")]
    public enum ScriptDiagnosticSeverity
    {
        Hidden,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// One compiler or sandbox message about a script event. Compiler messages keep their Roslyn
    /// id (CS0103 and so on); sandbox rules use WC ids. Line and column are 1-based and relative to
    /// the developer's script. Line 0 means the call line the server generates in front of the
    /// script, which usually points at event parameters that do not match the method.
    /// </summary>
    [GenerateSerializer, Alias("wildcat.ScriptDiagnostic")]
    public class ScriptDiagnostic
    {
        [Id(0)] public ScriptDiagnosticSeverity Severity { get; set; }
        [Id(1)] public string Id { get; set; }
        [Id(2)] public string Message { get; set; }
        [Id(3)] public int Line { get; set; }
        [Id(4)] public int Column { get; set; }

        public override string ToString()
        {
            return Line > 0 ? $"({Line},{Column}) {Id}: {Message}" : $"{Id}: {Message}";
        }
    }
}
