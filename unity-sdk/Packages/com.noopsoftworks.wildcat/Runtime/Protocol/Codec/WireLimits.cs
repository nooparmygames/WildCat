namespace WildCat.Protocol
{
    /// <summary>
    /// Limits on what clients send, shared by the gateway and the SDK.
    /// <para>
    /// Collection counts: the decoder refuses a count above its limit with a
    /// <see cref="WireFormatException"/> before anything is allocated for it, so a hostile frame
    /// cannot make the server build a collection many times its own size (a megabyte of empty
    /// strings is a million array slots). Collections the server sends have no such limit.
    /// </para>
    /// <para>
    /// String lengths, in characters: the gateway answers a request with a longer id, name, key or
    /// value with error code 1 (they become grain keys or stored data), and the SDK refuses to send
    /// one.
    /// </para>
    /// </summary>
    public static class WireLimits
    {
        /// <summary>Player targets in a frame header; the server never uses them.</summary>
        public const int MaxPlayerTargets = 64;

        /// <summary>Public data keys a GetFriends request (88) asks for.</summary>
        public const int MaxPublicDataKeys = 64;

        /// <summary>Arguments of an ExecuteScriptEvent request (66); an event takes at most 16.</summary>
        public const int MaxScriptArguments = 64;

        /// <summary>Entries of the extra data map of Authenticate, CreateNewAccount and AddAuthMechanismToAccount (76, 78, 82).</summary>
        public const int MaxLoginDataEntries = 64;

        /// <summary>(item, count) pairs of one inventory request (111, 113, 115, 117).</summary>
        public const int MaxInventoryItemsPerRequest = 256;

        /// <summary>Account (player) ids, device ids and user names.</summary>
        public const int MaxIdLength = 128;

        /// <summary>Email addresses; no valid address is longer.</summary>
        public const int MaxEmailLength = 254;

        /// <summary>Display names and the names of leaderboards, inventories, items, game modes and script events.</summary>
        public const int MaxNameLength = 128;

        /// <summary>Cloud data and publisher data keys.</summary>
        public const int MaxKeyLength = 128;

        /// <summary>Cloud data values.</summary>
        public const int MaxValueLength = 64 * 1024;
    }
}
