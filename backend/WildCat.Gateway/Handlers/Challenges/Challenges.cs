using System.Threading.Tasks;
using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>What the challenge handlers share.</summary>
    internal static class Challenges
    {
        public const string CannotChallengeYourself = "You cannot challenge yourself.";
        public const string NotOnline = "That player is not online.";
        public const string ChallengedYouAlready = "That player has challenged you already. Accept their challenge instead.";

        /// <summary>The challenges of player <paramref name="playerId"/> in the session's game.</summary>
        public static IPlayerChallengeHandler Of(MessageContext context, string playerId)
        {
            return context.Grains.GetGrain<IPlayerChallengeHandler>(context.Session.GameKey, playerId, null);
        }

        /// <summary>The challenger named by an accept or deny request; error code 1 for an empty id.</summary>
        public static IPlayerChallengeHandler OfChallenger(MessageContext context, string challengerId)
        {
            if (string.IsNullOrWhiteSpace(challengerId))
                throw new RequestFailedException(GatewayErrors.Refused, ChallengeNotFoundException.DefaultMessage);
            return Of(context, challengerId);
        }

        /// <summary>Runs a challenge call and answers a missing challenge with error code 1.</summary>
        public static async Task RefusalsAsErrors(Task call)
        {
            try
            {
                await call;
            }
            catch (ChallengeNotFoundException ex)
            {
                throw new RequestFailedException(GatewayErrors.Refused, ex.Message);
            }
        }
    }
}
