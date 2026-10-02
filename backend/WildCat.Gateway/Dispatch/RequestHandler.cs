using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// The usual shape of a handler: one request type with a <typeparamref name="TRequest"/>
    /// payload, answered with an OptionalResponse&lt;<typeparamref name="TResponse"/>&gt; on
    /// <see cref="ResponseType"/>. Override <see cref="Session"/> and <see cref="IsSignInAttempt"/>
    /// when the defaults (logged in player, not a sign-in attempt) do not fit.
    /// </summary>
    public abstract class RequestHandler<TRequest, TResponse> : IMessageHandler
        where TRequest : MessageBase, new()
        where TResponse : MessageBase, new()
    {
        public abstract WildCatMsgType RequestType { get; }

        public abstract WildCatMsgType ResponseType { get; }

        /// <summary>What the dispatcher checks first. Defaults to a logged in player.</summary>
        public virtual SessionRequirement Session
        {
            get { return SessionRequirement.Player; }
        }

        /// <summary>True when the request counts against the sign-in limit. Defaults to false.</summary>
        public virtual bool IsSignInAttempt
        {
            get { return false; }
        }

        public IEnumerable<MessageRoute> Routes
        {
            get { yield return new MessageRoute(RequestType, ResponseType, Session, IsSignInAttempt); }
        }

        public async Task HandleAsync(MessageContext context)
        {
            TRequest request = context.ReadPayload<TRequest>();
            TResponse response = await HandleAsync(context, request);
            context.Reply(response);
        }

        /// <summary>
        /// Does the work and returns the payload of the successful answer. Throw
        /// <see cref="RequestFailedException"/> to answer with an error code the client sees.
        /// </summary>
        protected abstract Task<TResponse> HandleAsync(MessageContext context, TRequest request);
    }
}
