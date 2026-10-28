using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// FindMatch (15 -> 16): queues the player with the matchmaker of the requested game mode
    /// (ICustomMatchMaker keyed by the session's game and the mode name; a mode the game does not
    /// define plays by the default rules, two players) and waits for a match. On a match it joins
    /// the player to it (RelayServerGame.PlayerStarted, which pushes GamePlayerAddedEvent to the
    /// players already in the match) and answers with the match id and its players. Error code 1,
    /// "Match not found", when the wait runs out, and for an empty mode name or one longer than
    /// <see cref="InputLimits.MaxNameLength"/>.
    /// </summary>
    /// <remarks>
    /// The request waits up to its wait time in one of the connection's request slots; the
    /// dispatcher keeps serving the connection's other requests and its relay meanwhile. The wait
    /// is capped at <see cref="MaxWaitSeconds"/>: the matchmaker checks every 3 seconds, and the
    /// grain call has to return within Orleans' 30 second response timeout (a longer wait used to
    /// fail with a timeout while the player stayed queued).
    /// Joining is part of FindMatch because the SDK has no separate join message; the legacy code
    /// did the same under a "make it not automatic" TODO.
    /// </remarks>
    internal sealed class FindMatchHandler : RequestHandler<FindMatchRequest, FindMatchResponse>
    {
        public const uint MaxWaitSeconds = 20;
        public const string MatchNotFound = "Match not found";
        public const string NoGameMode = "The game mode name is empty.";

        public override WildCatMsgType RequestType => WildCatMsgType.FindMatchRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.FindMatchResponse;

        protected override async Task<FindMatchResponse> HandleAsync(MessageContext context, FindMatchRequest request)
        {
            // Orleans refuses an empty key extension, so this used to be an internal error.
            if (string.IsNullOrWhiteSpace(request.matchType))
                throw new RequestFailedException(GatewayErrors.Refused, NoGameMode);
            InputLimits.Name(request.matchType, "game mode name");

            var login = context.Connection.Login;
            if (login == null || !login.Player.Equals(context.Player) || login.GameKey != context.Session.GameKey)
                throw new RequestFailedException(GatewayErrors.Refused, MatchNotFound);
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(context.Closing, login.Ended);
            var matchMaker = context.Grains.GetGrain<ICustomMatchMaker>(context.Session.GameKey, request.matchType, null);
            FoundRelayMatchResult found;
            try
            {
                found = await matchMaker.FindMatch(context.Player, request.skill,
                    Math.Min(request.waitTime, MaxWaitSeconds), login.LoginTicket, canceled.Token);
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested)
            {
                throw new RequestFailedException(GatewayErrors.Refused, MatchNotFound);
            }
            if (found?.game == null)
                throw new RequestFailedException(GatewayErrors.Refused, MatchNotFound);

            IRelayServerGame match = found.game;
            try
            {
                canceled.Token.ThrowIfCancellationRequested();
                Guid matchId = match.GetPrimaryKey(out string _);
                var players = await match.GetPlayers();
                canceled.Token.ThrowIfCancellationRequested();
                if (!await context.Player.TryJoinMatch(match, login.LoginTicket))
                {
                    await match.PlayerFinished(context.Player);
                    throw new RequestFailedException(GatewayErrors.Refused, MatchNotFound);
                }
                canceled.Token.ThrowIfCancellationRequested();
                return new FindMatchResponse(matchId, "", players.Select(p => p.GetPrimaryKeyString()).ToArray());
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested)
            {
                // This login may have disconnected before StartGame added its match. Repeat the
                // ticket-aware cleanup so it cannot disconnect an account that has logged in again.
                await context.Player.Disconnected(login.LoginTicket);
                throw new RequestFailedException(GatewayErrors.Refused, MatchNotFound);
            }
        }
    }
}
