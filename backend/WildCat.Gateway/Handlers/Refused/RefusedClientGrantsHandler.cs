using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Answers SetLeaderboard (28 -> 29), AddItemToInventory (111 -> 112) and SetItemToInventory
    /// (113 -> 114) with error code 1 and <see cref="GatewayErrors.ClientGrantRejected"/>, without
    /// looking at the session. Clients used to pick their own scores and grant themselves items
    /// with these; since step 4 scores and grants come from server script events
    /// (WildCatScriptApi.SetScoreInLeaderBoard, AddInventoryItems, SetInventoryItems). Removing
    /// items (115) stays allowed, since players may spend their own.
    /// </summary>
    internal sealed class RefusedClientGrantsHandler : IMessageHandler
    {
        private static readonly MessageRoute[] RefusedRoutes =
        {
            new MessageRoute(WildCatMsgType.SetLeaderboardRequest, WildCatMsgType.SetLeaderboardResponse, SessionRequirement.None),
            new MessageRoute(WildCatMsgType.AddItemToInventoryRequest, WildCatMsgType.AddItemToInventoryResponse, SessionRequirement.None),
            new MessageRoute(WildCatMsgType.SetItemToInventoryRequest, WildCatMsgType.SetItemToInventoryResponse, SessionRequirement.None),
        };

        private readonly ILogger<RefusedClientGrantsHandler> logger;

        public RefusedClientGrantsHandler(ILogger<RefusedClientGrantsHandler> logger)
        {
            this.logger = logger;
        }

        public IEnumerable<MessageRoute> Routes
        {
            get { return RefusedRoutes; }
        }

        public Task HandleAsync(MessageContext context)
        {
            logger.LogInformation("Refused {MessageType} from connection {ConnectionId}: clients may not grant items or set scores",
                context.Request.msgType, context.Connection.Id);
            context.ReplyFailure(GatewayErrors.Refused, GatewayErrors.ClientGrantRejected);
            return Task.CompletedTask;
        }
    }
}
