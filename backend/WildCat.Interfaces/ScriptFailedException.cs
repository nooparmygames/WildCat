using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// A script event run failed for a reason of the script or of its request, with a message
    /// written for the game client: the script threw (Exception, InvalidOperationException or
    /// ArgumentException, the ones scripts can create), its arguments did not fit the event, the
    /// script API refused a call, the stored script is not valid, or it returned a type events
    /// cannot return. <see cref="IScriptEventRunner.Run"/> wraps those in this type, and the gateway
    /// answers only this type (with the script timeouts and limits) with error code 1 and the
    /// message. Anything else from a run, such as a grain, storage or cluster failure inside a
    /// script API call, keeps its own type and is answered as an internal error.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ScriptFailedException")]
    public class ScriptFailedException : Exception
    {
        public ScriptFailedException()
        {
        }

        public ScriptFailedException(string message) : base(message)
        {
        }

        public ScriptFailedException(string message, string scriptExceptionType) : base(message)
        {
            ScriptExceptionType = scriptExceptionType;
        }

        public ScriptFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// The full name of the .NET exception type the failure started as (for example
        /// System.ArgumentException), for logs and tests. The original exception does not travel
        /// with it.
        /// </summary>
        [Id(0)]
        public string ScriptExceptionType { get; set; } = "";
    }
}
