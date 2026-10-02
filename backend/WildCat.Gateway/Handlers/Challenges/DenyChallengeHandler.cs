using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// DenyChallenge (23 -> 24): turns down the pending challenge of the player whose account id is
    /// the request's Player. The challenger gets a ChallengeDeniedEvent (25) with the request's
    /// data. Denying when there is no pending challenge succeeds and tells nobody; an empty id is
    /// error code 1.
    /// </summary>
    internal sealed class DenyChallengeHandler : RequestHandler<ChallengeMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.DenyChallengeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.DenyChallengeResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, ChallengeMessage request)
        {
            var challenger = Challenges.OfChallenger(context, request.Player);
            await challenger.DenyChallenge(context.Player, request.data);
            return new EmptyMessage();
        }
    }
}
