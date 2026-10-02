using System.Collections.Generic;
using System.Threading.Tasks;

namespace WildCat.Gateway
{
    /// <summary>
    /// Serves one or more request types. Every non-abstract implementation in the WildCat.Gateway
    /// assembly is registered at startup as a singleton (see GatewayExtensions.AddGameGateway), so
    /// a new handler only has to exist. Handlers are shared by all connections and must not keep
    /// per-request state in fields. Most handlers derive from
    /// <see cref="RequestHandler{TRequest, TResponse}"/>; see docs/gateway.md.
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>The request types served, each with its answer type and requirements.</summary>
        IEnumerable<MessageRoute> Routes { get; }

        /// <summary>
        /// Handles one request that passed the route's checks. The dispatcher awaits it inside a
        /// try/catch: a <see cref="RequestFailedException"/> is answered with its code and message,
        /// any other exception with error code -1 and "Internal error" (the details go to the log).
        /// </summary>
        Task HandleAsync(MessageContext context);
    }
}
