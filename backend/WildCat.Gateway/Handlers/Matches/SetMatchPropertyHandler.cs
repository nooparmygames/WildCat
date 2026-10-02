using System.Threading.Tasks;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// SetMatchProperty (129 -> 130): sets a match property (pushed as MatchPropertyChangedEvent
    /// to every player in the match, the sender included) or one of the sender's own properties
    /// in the match (pushed to the other players). Players who rejoin get them all in
    /// MatchRejoinSyncEvent. Error code 1 when the match's properties would reach 10 KB, and for
    /// a match the player is not in.
    /// </summary>
    /// <remarks>
    /// The legacy code answered success when a value did not fit and nothing was stored, never
    /// stored a player property (the grain threw), and let anyone who knew a match id change its
    /// properties.
    /// </remarks>
    internal sealed class SetMatchPropertyHandler : RequestHandler<SetMatchPropertyData, EmptyMessage>
    {
        public static readonly string TooLarge =
            $"The properties of a match are limited to {RelayServerGameState.MaxPropertyBytes} bytes in total.";

        public override WildCatMsgType RequestType => WildCatMsgType.SetMatchPropertyRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.SetMatchPropertyResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, SetMatchPropertyData request)
        {
            IRelayServerGame match = Matches.Get(context, request.Game);
            bool stored = request.isPlayerProperty
                ? await match.SetPlayerProperty(context.Player, request.key, request.value)
                : await match.SetGameProperty(context.Player, request.key, request.value);
            if (!stored)
                throw new RequestFailedException(GatewayErrors.Refused, TooLarge);
            return new EmptyMessage();
        }
    }
}
