using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// The script event does not exist: it was never created, or it was deleted. Thrown by
    /// <see cref="IScriptEventCompiler"/> for every call on a missing event, and so by
    /// <see cref="IScriptEventRunner.Run"/>. It is an InvalidOperationException, which these calls
    /// threw before it existed, so code that catches that keeps working; the gateway uses the type
    /// to answer ExecuteScriptEvent (66) with an error the client can act on.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ScriptEventNotFoundException")]
    public class ScriptEventNotFoundException : InvalidOperationException
    {
        public ScriptEventNotFoundException()
        {
        }

        public ScriptEventNotFoundException(string message) : base(message)
        {
        }

        public ScriptEventNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
