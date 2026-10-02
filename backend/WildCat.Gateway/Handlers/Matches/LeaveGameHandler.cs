using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// LeaveGame (13 -> 14): the player has finished the match named by the frame header's game
    /// target (RelayServerGame.PlayerFinished). When every player has finished, the match pushes
    /// GameFinishedEvent (10) to all of them. Error code 1 without a game target or with one that
    /// is not a match id, and for a match the player is not in.
    /// </summary>
    /// <remarks>
    /// The legacy code sent no answer without a game target, retried a failed PlayerFinished up to
    /// five times, and sent its answer through the PlayerConnection stream, where building it
    /// threw every time; the answer now goes straight to the connection.
    /// </remarks>
    internal sealed class LeaveGameHandler : RequestHandler<EmptyMessage, EmptyMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.LeaveGameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.LeaveGameResponse;

        protected override async Task<EmptyMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            await Matches.FromGameTarget(context).PlayerFinished(context.Player);
            return new EmptyMessage();
        }
    }
}
