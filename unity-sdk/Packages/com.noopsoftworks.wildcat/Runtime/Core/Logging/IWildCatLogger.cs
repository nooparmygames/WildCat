using System;

namespace WildCat
{
    /// <summary>
    /// Receives the SDK's log messages. Called from any thread, including the network threads, so
    /// implementations must be thread safe. The Unity layer forwards to UnityEngine.Debug.
    /// </summary>
    public interface IWildCatLogger
    {
        /// <param name="level">How important the message is.</param>
        /// <param name="message">Plain text; never contains passwords or tickets.</param>
        /// <param name="exception">The exception behind the message, or null.</param>
        void Log(WildCatLogLevel level, string message, Exception exception);
    }
}
