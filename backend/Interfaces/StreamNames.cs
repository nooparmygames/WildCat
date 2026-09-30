using Orleans.Runtime;

namespace Interfaces
{
    /// <summary>
    /// Stream provider name and stream ids shared by the grains (producers) and the WebSocket
    /// gateway (subscriber). The legacy SMS provider keyed streams by (Guid, namespace); memory
    /// streams key them by (namespace, string), so the ids are built here in one place.
    /// </summary>
    public static class StreamNames
    {
        /// <summary>Memory stream provider registered on the silo (and the test client).</summary>
        public const string PROVIDER_NAME = "game";

        public const string CHALLENGE_EVENTS_STREAM_NAME = "ChallengeEvents";
        public const string FRIEND_EVENTS_STREAM_NAME = "FriendEvents";
        public const string PLAYER_CONNECTION_STREAM_NAME = "PlayerConnection";

        /// <summary>Per player stream the gateway node holding the socket subscribes to.</summary>
        public static StreamId PlayerConnection(string playerId) =>
            StreamId.Create(PLAYER_CONNECTION_STREAM_NAME, playerId);

        /// <summary>Friend list events for one player in one game.</summary>
        public static StreamId FriendEvents(Guid gameKey, string playerId) =>
            StreamId.Create(FRIEND_EVENTS_STREAM_NAME, $"{gameKey:N}/{playerId}");

        /// <summary>Challenge events for one player in one game.</summary>
        public static StreamId ChallengeEvents(Guid gameKey, string playerId) =>
            StreamId.Create(CHALLENGE_EVENTS_STREAM_NAME, $"{gameKey:N}/{playerId}");
    }
}
