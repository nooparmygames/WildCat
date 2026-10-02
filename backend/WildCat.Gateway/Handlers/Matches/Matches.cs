using System;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// What the match handlers share. A match is an IRelayServerGame keyed by its id and the game
    /// of the request's session, the same key the matchmaker, challenges and the relay use.
    /// </summary>
    /// <remarks>
    /// Match events (GamePlayerAddedEvent 9, GameFinishedEvent 10, GamePlayerRemovedEvent 125,
    /// GamePlayerRejoinedEvent 126, MatchRejoinSyncEvent 127, MatchPropertyChangedEvent 128) are
    /// built and encoded by RelayServerGame and sent through IPlayer.SendDataToConnection, which
    /// publishes them on the player's PlayerConnection stream; <see cref="PushSubscriptions"/>
    /// queues them on the player's socket. Answers go straight to the connection, so an answer can
    /// arrive before the events its own request caused.
    /// </remarks>
    internal static class Matches
    {
        public const string NoGameTarget = "LeaveGame needs the match id as the game target.";
        public const string InvalidGameTarget = "The game target is not a match id.";

        public static IRelayServerGame Get(MessageContext context, Guid matchId)
        {
            return context.Grains.GetGrain<IRelayServerGame>(matchId, context.Session.GameKey.ToString(), null);
        }

        /// <summary>
        /// The match named by the frame header's game target (LeaveGame carries its match there).
        /// Error code 1 when there is none or it is not a Guid.
        /// </summary>
        public static IRelayServerGame FromGameTarget(MessageContext context)
        {
            WildCatMessageBase request = context.Request;
            if (!request.HasGameTarget)
                throw new RequestFailedException(GatewayErrors.Refused, NoGameTarget);
            if (!Guid.TryParse(request.game, out Guid matchId))
                throw new RequestFailedException(GatewayErrors.Refused, InvalidGameTarget);
            return Get(context, matchId);
        }
    }
}
