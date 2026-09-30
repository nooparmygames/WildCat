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
