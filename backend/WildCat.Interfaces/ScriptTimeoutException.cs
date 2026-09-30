using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// A script event ran longer than its wall-clock limit (ScriptOptions.Timeout in
    /// WildCat.Grains, 3 seconds by default) and was stopped.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ScriptTimeoutException")]
    public class ScriptTimeoutException : Exception
    {
        public ScriptTimeoutException()
        {
        }

        public ScriptTimeoutException(string message) : base(message)
        {
        }

        public ScriptTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
