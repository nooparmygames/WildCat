using WildCat.Interfaces;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// The old gateway code for the message types that have no handler of their own yet, called
    /// through <see cref="LegacyAdapterHandler"/>. The dispatcher has already decoded the frame,
    /// applied the sign-in limit and checked the session. Roadmap step 6b moves each group to
    /// handlers under Handlers/ and deletes its code here; after that this file and the adapter go.
    /// </summary>
    public class FrontEndServer
    {
        private readonly ILogger _Logger;
        private readonly IClusterClient orleansClient;
        private const int MaxTrycount = 5;

        public FrontEndServer(ILogger<FrontEndServer> logger, IClusterClient clusterClient)
        {
            orleansClient = clusterClient;
            _Logger = logger;
        }

        /// <summary>Handles one request of a type listed in <see cref="LegacyAdapterHandler"/>.</summary>
        public async Task HandleAsync(MessageContext context)
        {
            SessionGameInfo session = context.Session;
            IPlayer player = context.Player;
            GatewayConnection connection = context.Connection;
            WildCatMessageBase msg = context.Request;
            Guid ticket = context.Ticket;
            const int channelId = 0;
            const bool isReliable = true;

            _Logger.LogTrace("Legacy handling of {MessageType} with a {Size} byte payload", msg.msgType, msg.RawData.Length);
            switch (msg.msgType)
            {
                default:
                    throw new InvalidOperationException($"The legacy gateway code has no case for {msg.msgType}.");
            }
        }
    }
}
