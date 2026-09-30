using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// A script event was refused without running because its game already has more runs still
    /// going after their time limit than ScriptOptions.MaxOverdueRunsPerGame (in WildCat.Grains)
    /// allows. New runs of the game work again once those finish.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ScriptThrottledException")]
    public class ScriptThrottledException : Exception
    {
        public ScriptThrottledException()
        {
        }

        public ScriptThrottledException(string message) : base(message)
        {
        }

        public ScriptThrottledException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
