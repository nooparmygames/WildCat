using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// One request on its way through a handler: the decoded frame, the connection it came on,
    /// the session and player the dispatcher checked (as the route requires), the cluster client,
    /// and helpers to read the payload and answer.
    /// </summary>
    public sealed class MessageContext
    {
        internal MessageContext(GatewayConnection connection, WildCatMessageBase request, MessageRoute route,
            IClusterClient grains, ILogger logger, SessionGameInfo session, Guid ticket, IPlayer player)
        {
            Connection = connection;
            Request = request;
            Route = route;
            Grains = grains;
            Logger = logger;
            Session = session;
            Ticket = ticket;
            Player = player;
        }

        /// <summary>The connection the request came on; use it to send other frames or read the login.</summary>
        public GatewayConnection Connection { get; }

        /// <summary>The decoded request frame.</summary>
        public WildCatMessageBase Request { get; }

        public MessageRoute Route { get; }

        /// <summary>The silo's cluster client, for grain calls.</summary>
        public IClusterClient Grains { get; }

        /// <summary>The dispatcher's logger, for handlers that need one without injecting their own.</summary>
        public ILogger Logger { get; }

        /// <summary>
        /// The session named by the frame header's ticket, checked and touched by the dispatcher;
        /// null when the route's requirement is <see cref="SessionRequirement.None"/>.
        /// </summary>
        public SessionGameInfo Session { get; }

        /// <summary>The frame header's session ticket; Guid.Empty when the route needs none.</summary>
        public Guid Ticket { get; }

        /// <summary>
        /// The player logged in on <see cref="Session"/>. Never null for
        /// <see cref="SessionRequirement.Player"/>; null for None, and for Session when nobody is
        /// logged in.
        /// </summary>
        public IPlayer Player { get; }

        /// <summary>Cancelled when the connection starts closing; an answer sent after that is dropped.</summary>
        public CancellationToken Closing
        {
            get { return Connection.Closing; }
        }

        /// <summary>
        /// Decodes the request payload. A payload that is not a <typeparamref name="T"/> throws
        /// WireFormatException, which the dispatcher answers with error code 1.
        /// </summary>
        public T ReadPayload<T>() where T : MessageBase, new()
        {
            return Request.GetPayload<T>();
        }

        /// <summary>Answers with a successful OptionalResponse on the route's response type.</summary>
        public bool Reply<T>(T payload) where T : MessageBase, new()
        {
            return Connection.Send(WildCatMessageBase.CreateResponse(OptionalResponse<T>.CreateSuccessful(payload),
                Route.ResponseType, Request));
        }

        /// <summary>Answers with a failed OptionalResponse on the route's response type.</summary>
        public bool ReplyFailure(int errorCode, string errorMessage)
        {
            return Connection.Send(WildCatMessageBase.CreateResponse(OptionalResponse<EmptyMessage>.CreateFailed(errorMessage, errorCode),
                Route.ResponseType, Request));
        }

        /// <summary>Sends any other frame to this connection, for example an event with no request id.</summary>
        public bool Send(WildCatMessageBase message)
        {
            return Connection.Send(message);
        }
    }
}
