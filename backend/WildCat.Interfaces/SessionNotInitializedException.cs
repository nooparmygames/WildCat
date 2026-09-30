using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Thrown by <see cref="ISession"/> for a ticket that was never initialized or whose session
    /// has ended (logout, disconnect, expiry).
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.SessionNotInitializedException")]
    public class SessionNotInitializedException : Exception
    {
        public const string DefaultMessage = "The session does not exist or has ended. Initialize a new session.";

        public SessionNotInitializedException() : base(DefaultMessage)
        {
        }

        public SessionNotInitializedException(string message) : base(message)
        {
        }

        public SessionNotInitializedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
