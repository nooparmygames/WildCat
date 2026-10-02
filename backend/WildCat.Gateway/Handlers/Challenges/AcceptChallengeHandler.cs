using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// AcceptChallenge (20 -> 21): accepts the challenge of the player whose account id is the
    /// request's Player. A match (RelayServerGame) starts with both players, and both get a
    /// ChallengeAcceptedEvent (22) with its id in GameKey and the request's data. Error code 1 when
    /// the challenge has timed out (ChallengeTimeoutException, answered by the dispatcher) or there
    /// is no open challenge from that player.
    /// </summary>
    internal sealed class AcceptChallengeHandler : RequestHandler<ChallengeMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.AcceptChallengeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.AcceptChallengeResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, ChallengeMessage request)
        {
            var challenger = Challenges.OfChallenger(context, request.Player);
            await Challenges.RefusalsAsErrors(challenger.AcceptChallenge(context.Player, request.data));
            return new EmptyMessage();
        }
    }
}
