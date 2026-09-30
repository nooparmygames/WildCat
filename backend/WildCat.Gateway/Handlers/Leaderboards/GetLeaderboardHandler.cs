using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// GetLeaderboard (26 -> 27): reads a leaderboard of the session's game (ILeaderBoard keyed
    /// "{gameKey}_L_{name}"). Me answers the player's rank, Around the player's rank and up to
    /// count entries around it, Top the number of entries on the board and up to count entries
    /// from offset. Ranks count from 0; -1 means the player has no score on the board, and a board
    /// nobody has scored on answers like an empty one. The answer repeats the query type.
    /// Error code 1 for a negative count or offset and an unknown query type.
    /// </summary>
    /// <remarks>
    /// Scores are set by server script events (WildCatScriptApi.SetScoreInLeaderBoard); clients
    /// may not send SetLeaderboard (28), see <see cref="RefusedClientGrantsHandler"/>.
    /// The count is capped at <see cref="MaxEntries"/>; the legacy code sent as many entries as a
    /// client asked for, up to the whole board.
    /// </remarks>
    internal sealed class GetLeaderboardHandler : RequestHandler<GetLeaderBoardRequest, GetLeaderBoardResponse>
    {
        public const int MaxEntries = 100;
        public const string NegativeRange = "The count and the offset must not be negative.";
        public const string UnknownQueryType = "Unknown leaderboard query type.";

        public override WildCatMsgType RequestType => WildCatMsgType.GetLeaderboardRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetLeaderboardResponse;

        protected override async Task<GetLeaderBoardResponse> HandleAsync(MessageContext context, GetLeaderBoardRequest request)
        {
            if (request.count < 0 || request.offset < 0)
                throw new RequestFailedException(GatewayErrors.Refused, NegativeRange);
            int count = Math.Min(request.count, MaxEntries);
            var board = context.Grains.GetGrain<ILeaderBoard>(
                ResourcesUtility.GetLeaderBoardKey(context.Session.GameKey.ToString(), request.leaderBoardName));
            var response = new GetLeaderBoardResponse { type = request.type };
            switch (request.type)
            {
                case GetLeaderBoardRequest.RequestType.Me:
                    response.playerRank = await board.GetPlayerRank(context.Player);
                    break;
                case GetLeaderBoardRequest.RequestType.Around:
                    var around = await board.GetAroundMe(context.Player, count);
                    response.playerRank = around.Item1;
                    AddEntries(response, around.Item2);
                    break;
                case GetLeaderBoardRequest.RequestType.Top:
                    var top = await board.GetTopEntries(count, request.offset);
                    response.count = top.Item1;
                    AddEntries(response, top.Item2);
                    break;
                default:
                    throw new RequestFailedException(GatewayErrors.Refused, UnknownQueryType);
            }
            return response;
        }

        private static void AddEntries(GetLeaderBoardResponse response, List<WildCat.Interfaces.LeaderBoardEntry> entries)
        {
            if (entries == null)
                return;
            foreach (var entry in entries)
            {
                response.entries.Add(new WildCat.Protocol.LeaderBoardEntry
                {
                    score = entry.Score,
                    userName = entry.Name,
                    playerId = entry.PlayerID,
                    entryTime = entry.EntryTime,
                });
            }
        }
    }
}
