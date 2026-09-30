namespace WildCat.Gateway
{
    /// <summary>Error codes and the texts of the errors the gateway itself answers with.</summary>
    public static class GatewayErrors
    {
        /// <summary>A refused or failed request: a client error or an expected outcome.</summary>
        public const int Refused = 1;

        /// <summary>The thing to create exists already (and similar cases some messages document).</summary>
        public const int AlreadyExists = 2;

        /// <summary>Something went wrong on the server; the client gets <see cref="InternalError"/>.</summary>
        public const int Internal = -1;

        /// <summary>The text sent for any unexpected exception; the details stay in the server log.</summary>
        public const string InternalError = "Internal error";

        /// <summary>ErrorMsg text for a message id up to HighestInternal that has no handler.</summary>
        public const string UnsupportedMessageType = "Unsupported message type";

        /// <summary>ErrorMsg text for a frame that could not be decoded, and error text for a payload that could not.</summary>
        public const string MalformedMessage = "The message could not be read.";

        /// <summary>ErrorMsg text for a message that needs a session ticket but carries none.</summary>
        public const string SessionRequired = "This message needs a session ticket.";

        /// <summary>Text for a session ticket that is not a Guid.</summary>
        public const string InvalidTicket = "The session ticket is not valid.";

        /// <summary>ErrorMsg text for a valid session on which no player has logged in.</summary>
        public const string NotLoggedIn = "Log in before sending this message.";

        /// <summary>ErrorMsg text when the session grain could not be reached.</summary>
        public const string SessionCheckFailed = "The session could not be checked. Try again.";

        /// <summary>Text for a sign-in attempt over the per-connection limit (<see cref="GameGatewayOptions.AuthAttemptBurst"/>).</summary>
        public const string TooManyAuthAttempts = "Too many attempts, try again later";

        /// <summary>Text for the grant and score requests clients may no longer send (28, 111, 113).</summary>
        public const string ClientGrantRejected =
            "Not allowed from clients. Grant items and set scores from a server script event.";
    }
}
