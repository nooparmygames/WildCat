using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// RejoinMatch (133 -> 134): puts the player back into a match it dropped out of (see
    /// GetPlayerPendingMatches). The match pushes GamePlayerRejoinedEvent (126) to its players and
    /// MatchRejoinSyncEvent (127), with every match and player property, to the player. Error code
    /// 1 for a match the player is not in and for one that has finished.
    /// </summary>
    /// <remarks>
    /// The legacy code went through IPlayer.RejoinMatch, which only forwarded to the match and
    /// made a player to match to player call cycle; the handler calls the match itself.
    /// </remarks>
    internal sealed class RejoinMatchHandler : RequestHandler<GuidMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.RejoinMatchRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.RejoinMatchResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, GuidMessage request)
        {
            await Matches.Get(context, request.value).PlayerRejoined(context.Player);
            return new EmptyMessage();
        }
    }
}
