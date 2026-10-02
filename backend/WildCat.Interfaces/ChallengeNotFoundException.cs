using System;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Thrown by <see cref="IPlayerChallengeHandler.AcceptChallenge"/> when the challenger has no
    /// open challenge to the accepting player (never sent, denied, or accepted already). The
    /// message is written for clients; the gateway answers it with error code 1.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    [Alias("wildcat.ChallengeNotFoundException")]
    public class ChallengeNotFoundException : Exception
    {
        public const string DefaultMessage = "That player has no open challenge to you.";

        public ChallengeNotFoundException() : base(DefaultMessage)
        {
        }

        public ChallengeNotFoundException(string message) : base(message)
        {
        }

        public ChallengeNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
