using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Thrown by <see cref="ISession"/> for a session that sat idle past its timeout. The session
    /// is cleared, so the client has to initialize a new one.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.SessionExpiredException")]
    public class SessionExpiredException : Exception
    {
        public const string DefaultMessage = "The session expired after being idle too long. Initialize a new session.";

        public SessionExpiredException() : base(DefaultMessage)
        {
        }

        public SessionExpiredException(string message) : base(message)
        {
        }

        public SessionExpiredException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
