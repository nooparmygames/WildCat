using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPlayerChallenges (161 -> 162): the challenges the logged in player sent and received in
    /// the session's game, with the other player's id and name, the status (pending or started),
    /// the match id of a started one and the data sent with it. The received ones used to be
    /// listed under sent (docs/legacy-backend-notes.md bug 21).
    /// </summary>
    internal sealed class GetPlayerChallengesHandler : RequestHandler<EmptyMessage, ChallengeRequestsMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPlayerChallengesRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPlayerChallengesResponse;

        protected override async Task<ChallengeRequestsMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            var (sent, received) = await Challenges.Of(context, context.Player.GetPrimaryKeyString()).GetChallengeRequests();
            return new ChallengeRequestsMessage
            {
                sent = await ToRequestsAsync(sent),
                received = await ToRequestsAsync(received),
            };
        }

        private static async Task<List<ChallengeRequestsMessage.Request>> ToRequestsAsync(List<ChallengeRequestState> states)
        {
            var names = await Task.WhenAll(states.Select(s => s.Player.GetPlayerName()));
            return states.Select((s, i) => new ChallengeRequestsMessage.Request
            {
                Player = s.Player.GetPrimaryKeyString(),
                PlayerName = names[i] ?? "",
                data = s.data ?? new byte[0],
                gameId = s.Game,
                status = (ChallengeRequestsMessage.Request.Status)s.State,
            }).ToList();
        }
    }
}
