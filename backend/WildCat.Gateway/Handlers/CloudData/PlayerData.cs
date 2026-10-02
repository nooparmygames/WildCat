using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>
    /// Where the cloud data handlers (30-33, 70-75) and GetFriends (88) read and write player data:
    /// the IPlayerStorage of a player in the game of the request's session, "{gameKey}_{playerId}",
    /// which is also where that game's script events see it. (Before, they went through the Player
    /// grain, which used the game the player last logged in to: an account that played games of
    /// two developers could write one game's data into the other's storage, and other players read
    /// public data of whichever game the player had played last.)
    /// </summary>
    internal static class PlayerData
    {
        /// <summary>The storage of <paramref name="playerId"/> in the session's game.</summary>
        public static IPlayerStorage Of(MessageContext context, string playerId)
        {
            return context.Grains.GetGrain<IPlayerStorage>(
                ResourcesUtility.GetPlayPlayerStorageKey(context.Session.GameKey.ToString(), playerId));
        }

        /// <summary>The logged in player's storage in the session's game.</summary>
        public static IPlayerStorage Own(MessageContext context)
        {
            return Of(context, context.Player.GetPrimaryKeyString());
        }
    }
}
