using System;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// What the friend and challenge handlers share: finding another player's account and the
    /// logged in player's friend list in the game of the session.
    /// </summary>
    internal static class Friends
    {
        public const string PlayerNotFound = "Player not found.";

        /// <summary>The logged in player's friend list in the session's game.</summary>
        public static IPlayerFriendList ListOf(MessageContext context)
        {
            return context.Grains.GetGrain<IPlayerFriendList>(context.Session.GameKey, context.Player.GetPrimaryKeyString(), null);
        }

        /// <summary>
        /// The player whose account id is <paramref name="accountId"/>, or null when there is no
        /// such account (the old code invited and challenged ids nobody had created).
        /// </summary>
        public static async Task<IPlayer> FindByAccountIdAsync(IGrainFactory grains, string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                return null;
            var player = grains.GetGrain<IPlayer>(accountId);
            try
            {
                await player.GetState();
                return player;
            }
            catch (InvalidOperationException)
            {
                // GetState refuses an account that was never created.
                return null;
            }
        }

        /// <summary>
        /// The player an InviteFriend request names: by account id, player name, email or user
        /// name. Null when nobody matches. Player names are looked up in the name map, which
        /// nothing fills yet (docs/legacy-backend-notes.md bug 20), so they never match.
        /// </summary>
        public static async Task<IPlayer> FindAsync(IGrainFactory grains, InvitationType type, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string accountId;
            switch (type)
            {
                case InvitationType.AccountId:
                    return await FindByAccountIdAsync(grains, value);
                case InvitationType.playerName:
                    accountId = await grains.GetGrain<INameToAccountIdMap>(value).GetId();
                    break;
                case InvitationType.Email:
                    accountId = await grains.GetGrain<IEmailToAccountIdMap>(SignIn.NormalizeEmail(value)).GetAccountID();
                    break;
                case InvitationType.UserId:
                    accountId = await grains.GetGrain<IUserIdToAccountIdMap>(value).GetAccountID();
                    break;
                default:
                    return null;
            }
            return string.IsNullOrEmpty(accountId) ? null : grains.GetGrain<IPlayer>(accountId);
        }

        /// <summary>Runs a friend list call and answers its refusals with error code 1.</summary>
        public static async Task RefusalsAsErrors(Task call)
        {
            try
            {
                await call;
            }
            catch (FriendListException ex)
            {
                throw new RequestFailedException(GatewayErrors.Refused, ex.Message);
            }
        }
    }
}
