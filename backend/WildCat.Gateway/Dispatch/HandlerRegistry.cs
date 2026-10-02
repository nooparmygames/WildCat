using System;
using System.Collections.Generic;
using System.Linq;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// The request type to handler map, built once at startup from every registered
    /// <see cref="IMessageHandler"/>. Two handlers for one type fail the build. A request type no
    /// handler claims is answered with an unsupported message error by the dispatcher.
    /// </summary>
    public sealed class HandlerRegistry
    {
        private readonly Dictionary<WildCatMsgType, Entry> entries = new Dictionary<WildCatMsgType, Entry>();

        public HandlerRegistry(IEnumerable<IMessageHandler> handlers)
        {
            foreach (var handler in handlers ?? Enumerable.Empty<IMessageHandler>())
            {
                foreach (var route in handler.Routes)
                {
                    if (route.RequestType > WildCatMsgType.HighestInternal)
                        throw new InvalidOperationException(
                            $"{handler.GetType().Name} claims {route.RequestType}, a game message id; those are relayed, not handled.");
                    if (entries.TryGetValue(route.RequestType, out var existing))
                        throw new InvalidOperationException(
                            $"{route.RequestType} has two handlers: {existing.Handler.GetType().Name} and {handler.GetType().Name}.");
                    entries.Add(route.RequestType, new Entry(route, handler));
                }
            }
        }

        /// <summary>Every served route, in message id order.</summary>
        public IReadOnlyList<MessageRoute> Routes
        {
            get { return entries.Values.Select(e => e.Route).OrderBy(r => r.RequestType).ToList(); }
        }

        public bool TryGet(WildCatMsgType requestType, out MessageRoute route, out IMessageHandler handler)
        {
            if (entries.TryGetValue(requestType, out var entry))
            {
                route = entry.Route;
                handler = entry.Handler;
                return true;
            }
            route = null;
            handler = null;
            return false;
        }

        private sealed class Entry
        {
            public Entry(MessageRoute route, IMessageHandler handler)
            {
                Route = route;
                Handler = handler;
            }

            public MessageRoute Route { get; }
            public IMessageHandler Handler { get; }
        }
    }
}
