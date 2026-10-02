using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// A script event nested calls deeper than its limit (ScriptOptions.MaxCallDepth in
    /// WildCat.Grains) or came close to the end of the thread's stack. The sandbox throws this
    /// instead of letting the silo die of a stack overflow.
    /// </summary>
    [GenerateSerializer]
    [Alias("wildcat.ScriptRecursionLimitException")]
    public class ScriptRecursionLimitException : Exception
    {
        public ScriptRecursionLimitException()
        {
        }

        public ScriptRecursionLimitException(string message) : base(message)
        {
        }

        public ScriptRecursionLimitException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
