using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>One request type a handler serves, with what the dispatcher needs to know about it.</summary>
    public sealed class MessageRoute
    {
        public MessageRoute(WildCatMsgType requestType, WildCatMsgType responseType, SessionRequirement session,
            bool isSignInAttempt = false)
        {
            RequestType = requestType;
            ResponseType = responseType;
            Session = session;
            IsSignInAttempt = isSignInAttempt;
        }

        public WildCatMsgType RequestType { get; }

        /// <summary>
        /// The type of the answer. The dispatcher also uses it for the error answers it sends on
        /// the handler's behalf (sign-in limit, exceptions).
        /// </summary>
        public WildCatMsgType ResponseType { get; }

        public SessionRequirement Session { get; }

        /// <summary>
        /// True for requests that check a password or a code or send an email. They share each
        /// connection's sign-in allowance (<see cref="GameGatewayOptions.AuthAttemptBurst"/>); the
        /// dispatcher answers one over the limit itself, in arrival order, before any grain call.
        /// </summary>
        public bool IsSignInAttempt { get; }

        public override string ToString()
        {
            return $"{RequestType} -> {ResponseType} ({Session}{(IsSignInAttempt ? ", sign-in attempt" : "")})";
        }
    }
}
