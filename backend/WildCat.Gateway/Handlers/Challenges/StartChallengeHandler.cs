using System;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// StartChallenge (17 -> 18): challenges the player whose account id is the request's Player
    /// in the session's game, open for TimeOut seconds. The challengee gets a
    /// StartChallengeRequestedEvent (19) when online; an offline one finds the challenge in
    /// GetPlayerChallenges and can still accept it in time. Error code 1 for an unknown player
    /// ("Player not found.") and for the player themselves, code 2 when the challengee has an open
    /// challenge to the player already (accept that one instead). A new challenge to the same
    /// player replaces the pending one.
    /// </summary>
    internal sealed class StartChallengeHandler : RequestHandler<ChallengeMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.StartChallengeRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.StartChallengeResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, ChallengeMessage request)
        {
            var challengee = await Friends.FindByAccountIdAsync(context.Grains, request.Player);
            if (challengee == null)
                throw new RequestFailedException(GatewayErrors.Refused, Friends.PlayerNotFound);
            string playerId = context.Player.GetPrimaryKeyString();
            if (challengee.GetPrimaryKeyString() == playerId)
                throw new RequestFailedException(GatewayErrors.Refused, Challenges.CannotChallengeYourself);

            // Offline players can be challenged, as before; Disconnected only comes back when that
            // flag is false.
            var result = await Challenges.Of(context, playerId).SendChallengeRequest(challengee, request.data, request.TimeOut,
                isAcceptedOffline: true);
            switch (result)
            {
                case ChallengeAcceptionStatus.Received:
                    return new EmptyMessage();
                case ChallengeAcceptionStatus.Disconnected:
                    throw new RequestFailedException(GatewayErrors.Refused, Challenges.NotOnline);
                case ChallengeAcceptionStatus.RequestedYouAsWell:
                    throw new RequestFailedException(GatewayErrors.AlreadyExists, Challenges.ChallengedYouAlready);
                default:
                    throw new InvalidOperationException($"SendChallengeRequest answered {result}.");
            }
        }
    }
}
