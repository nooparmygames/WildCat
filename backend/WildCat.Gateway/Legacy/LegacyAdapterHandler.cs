using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Temporary: serves the request types that have no handler of their own yet by calling the
    /// old <see cref="FrontEndServer"/> code, so every message keeps working while roadmap step 6b
    /// moves the groups to handlers. A type claimed by a real handler is never routed here (see
    /// <see cref="HandlerRegistry"/>), so porting a type does not require editing this list;
    /// remove the type here and its code from FrontEndServer in the same change anyway.
    /// </summary>
    public sealed class LegacyAdapterHandler : IMessageHandler
    {
        private static readonly MessageRoute[] LegacyRoutes =
        {
        };

        private readonly FrontEndServer frontEnd;

        public LegacyAdapterHandler(FrontEndServer frontEnd)
        {
            this.frontEnd = frontEnd;
        }

        public IEnumerable<MessageRoute> Routes
        {
            get { return LegacyRoutes; }
        }

        public Task HandleAsync(MessageContext context)
        {
            return frontEnd.HandleAsync(context);
        }

        private static MessageRoute Route(WildCatMsgType request, WildCatMsgType response,
            SessionRequirement session = SessionRequirement.Player)
        {
            return new MessageRoute(request, response, session);
        }
    }
}
