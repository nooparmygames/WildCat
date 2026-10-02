using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Thrown when a game key does not name a game of the developer it is used with.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.InvalidGameException")]
    public class InvalidGameException : Exception
    {
        public const string DefaultMessage = "The game key does not belong to a game of this developer.";

        public InvalidGameException() : base(DefaultMessage)
        {
        }

        public InvalidGameException(string message) : base(message)
        {
        }

        public InvalidGameException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
