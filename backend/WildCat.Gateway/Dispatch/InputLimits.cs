using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// Size limits for the strings clients send that become grain keys or stored values. A longer
    /// string is refused with error code 1 before any grain is called, so a client cannot make the
    /// server key grains or store data by strings of up to a megabyte (Gateway:MaxMessageBytes).
    /// Lengths are counted in UTF-16 characters, as .NET and the SDK count them. The values live in
    /// <see cref="WireLimits"/> so the SDK checks the same ones before sending.
    /// </summary>
    public static class InputLimits
    {
        /// <summary>Account (player) ids, device ids and user names.</summary>
        public const int MaxIdLength = WireLimits.MaxIdLength;

        /// <summary>Email addresses; no valid address is longer.</summary>
        public const int MaxEmailLength = WireLimits.MaxEmailLength;

        /// <summary>Display names and the names of leaderboards, inventories, items, game modes and script events.</summary>
        public const int MaxNameLength = WireLimits.MaxNameLength;

        /// <summary>Cloud data and publisher data keys.</summary>
        public const int MaxKeyLength = WireLimits.MaxKeyLength;

        /// <summary>Cloud data values.</summary>
        public const int MaxValueLength = WireLimits.MaxValueLength;

        /// <summary>The error text for a <paramref name="what"/> longer than <paramref name="max"/> characters.</summary>
        public static string TooLong(string what, int max)
        {
            return $"The {what} is longer than {max} characters.";
        }

        /// <summary>
        /// Returns <paramref name="value"/>, or throws a <see cref="RequestFailedException"/> with
        /// code 1 when it is longer than <paramref name="max"/> characters. Null passes.
        /// </summary>
        public static string Check(string value, int max, string what)
        {
            if (value != null && value.Length > max)
                throw new RequestFailedException(GatewayErrors.Refused, TooLong(what, max));
            return value;
        }

        /// <summary>A player (account) id; see <see cref="MaxIdLength"/>.</summary>
        public static string PlayerId(string value)
        {
            return Check(value, MaxIdLength, "player id");
        }

        /// <summary>A cloud data or publisher data key; see <see cref="MaxKeyLength"/>.</summary>
        public static string Key(string value)
        {
            return Check(value, MaxKeyLength, "key");
        }

        /// <summary>A cloud data value; see <see cref="MaxValueLength"/>.</summary>
        public static string Value(string value)
        {
            return Check(value, MaxValueLength, "value");
        }

        /// <summary>A name of the kind <paramref name="what"/> names; see <see cref="MaxNameLength"/>.</summary>
        public static string Name(string value, string what)
        {
            return Check(value, MaxNameLength, what);
        }
    }
}
