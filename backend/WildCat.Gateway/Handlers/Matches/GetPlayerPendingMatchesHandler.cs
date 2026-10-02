using System.Linq;
using System.Threading.Tasks;
using Orleans;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetPlayerPendingMatches (131 -> 132): the running matches the player dropped out of and
    /// can rejoin with RejoinMatch, with their start time and players (IPlayer.GetPendingMatches).
    /// </summary>
    internal sealed class GetPlayerPendingMatchesHandler : RequestHandler<EmptyMessage, PendingMatchesCollectionData>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetPlayerPendingMatchesRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetPlayerMatchesResponse;

        protected override async Task<PendingMatchesCollectionData> HandleAsync(MessageContext context, EmptyMessage request)
        {
            var matches = await context.Player.GetPendingMatches();
            return new PendingMatchesCollectionData
            {
                matches = matches.Select(m => new PendingMatchesCollectionData.PendingMatchInfo
                {
                    matchId = m.MatchId,
                    startTime = m.StartTime,
                    players = m.players.Select(p => p.GetPrimaryKeyString()).ToArray(),
                }).ToArray(),
            };
        }
    }
}
