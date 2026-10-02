using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>
    /// The other player a request names by account id (GetPlayerName, GetPlayerPublicCloudData).
    /// </summary>
    internal static class RequestedPlayer
    {
        public const string IdRequired = "A player id is required.";

        /// <summary>
        /// The player grain for <paramref name="playerId"/>. An empty id is error code 1: Orleans
        /// refuses empty grain keys with an ArgumentException, which the old code sent to the client
        /// with code -1. So is an id longer than <see cref="InputLimits.MaxIdLength"/>. Any other id
        /// is looked up as it is; an id nobody has is a player without a name or data, as before.
        /// </summary>
        public static IPlayer Get(MessageContext context, string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new RequestFailedException(GatewayErrors.Refused, IdRequired);
            return context.Grains.GetGrain<IPlayer>(InputLimits.PlayerId(playerId));
        }
    }
}
