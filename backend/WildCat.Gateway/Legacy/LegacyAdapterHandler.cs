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
            // Profile and cloud data, publisher data (6b group 1)
            Route(WildCatMsgType.GetNameRequest, WildCatMsgType.GetNameResponse),
            Route(WildCatMsgType.SetNameRequest, WildCatMsgType.SetNameResponse),
            Route(WildCatMsgType.GetPlayerNameRequest, WildCatMsgType.GetPlayerNameResponse),
            Route(WildCatMsgType.GetCloudDataRequest, WildCatMsgType.GetCloudDataResponse),
            Route(WildCatMsgType.SetCloudDataRequest, WildCatMsgType.SetCloudDataResponse),
            Route(WildCatMsgType.SetPublicCloudDataRequest, WildCatMsgType.SetPublicCloudDataResponse),
            Route(WildCatMsgType.GetPublicCloudDataRequest, WildCatMsgType.GetPublicCloudDataResponse),
            Route(WildCatMsgType.GetPlayerPublicCloudDataRequest, WildCatMsgType.GetPlayerPublicCloudDataResponse),
            Route(WildCatMsgType.GetPublisherDataRequest, WildCatMsgType.GetPublisherDataResponse),
            // Server to server: the developer key and secret in the payload are the credentials.
            Route(WildCatMsgType.SetPublisherDataRequest_S, WildCatMsgType.SetPublisherDataResponse_S, SessionRequirement.None),
            Route(WildCatMsgType.GetPublisherDataRequest_S, WildCatMsgType.GetPublisherDataResponse_S, SessionRequirement.None),
            // Script events (group 1)
            Route(WildCatMsgType.ExecuteScriptEventRequest, WildCatMsgType.ExecuteScriptEventResponse),

            // Friends and challenges (group 2)
            Route(WildCatMsgType.GetFriendsRequest, WildCatMsgType.GetFriendsResponse),
            Route(WildCatMsgType.InviteFriendRequest, WildCatMsgType.InviteFriendResponse),
            Route(WildCatMsgType.AcceptFriendRequest, WildCatMsgType.AcceptFriendResponse),
            Route(WildCatMsgType.DenyFriendRequest, WildCatMsgType.DenyFriendResponse),
            Route(WildCatMsgType.StartChallengeRequest, WildCatMsgType.StartChallengeResponse),
            Route(WildCatMsgType.AcceptChallengeRequest, WildCatMsgType.AcceptChallengeResponse),
            Route(WildCatMsgType.DenyChallengeRequest, WildCatMsgType.DenyChallengeResponse),
            Route(WildCatMsgType.GetPlayerChallengesRequest, WildCatMsgType.GetPlayerChallengesResponse),

            // Matches, leaderboards and inventory (group 3)
            Route(WildCatMsgType.FindMatchRequest, WildCatMsgType.FindMatchResponse),
            Route(WildCatMsgType.LeaveGameRequest, WildCatMsgType.LeaveGameResponse),
            Route(WildCatMsgType.SetMatchPropertyRequest, WildCatMsgType.SetMatchPropertyResponse),
            Route(WildCatMsgType.RejoinMatchRequest, WildCatMsgType.RejoinMatchResponse),
            Route(WildCatMsgType.GetPlayerPendingMatchesRequest, WildCatMsgType.GetPlayerMatchesResponse),
            Route(WildCatMsgType.GetLeaderboardRequest, WildCatMsgType.GetLeaderboardResponse),
            // Refused from clients without looking at the session: grants and scores come from scripts.
            Route(WildCatMsgType.SetLeaderboardRequest, WildCatMsgType.SetLeaderboardResponse, SessionRequirement.None),
            Route(WildCatMsgType.AddItemToInventoryRequest, WildCatMsgType.AddItemToInventoryResponse, SessionRequirement.None),
            Route(WildCatMsgType.SetItemToInventoryRequest, WildCatMsgType.SetItemToInventoryResponse, SessionRequirement.None),
            Route(WildCatMsgType.GetInventoryRequest, WildCatMsgType.GetInventoryResponse),
            Route(WildCatMsgType.RemoveInventoryItemRequest, WildCatMsgType.RemoveInventoryItemResponse),
            Route(WildCatMsgType.UnlockInventoryRequest, WildCatMsgType.UnlockInventoryResponse),
            Route(WildCatMsgType.GetInventoryUnlockTimeRequest, WildCatMsgType.GetInventoryUnlockTimeResponse),
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
