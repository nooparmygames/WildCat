using System;

namespace WildCat
{
    /// <summary>Drops every message. The default when no logger is given.</summary>
    public sealed class NullWildCatLogger : IWildCatLogger
    {
        public static readonly NullWildCatLogger Instance = new NullWildCatLogger();

        public void Log(WildCatLogLevel level, string message, Exception exception)
        {
        }
    }
}
