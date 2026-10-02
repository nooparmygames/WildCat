using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace WildCat.Gateway
{
    /// <summary>
    /// The open connections of this gateway node, by connection id and by logged in player.
    /// Thread safe: every socket runs its own loops, and stream callbacks push from other threads.
    /// </summary>
    public sealed class ConnectionRegistry
    {
        private readonly ConcurrentDictionary<int, GatewayConnection> connections = new ConcurrentDictionary<int, GatewayConnection>();
        private readonly ConcurrentDictionary<string, GatewayConnection> players = new ConcurrentDictionary<string, GatewayConnection>(StringComparer.Ordinal);

        /// <summary>Open connections.</summary>
        public int Count
        {
            get { return connections.Count; }
        }

        /// <summary>Ids of the players logged in on this node.</summary>
        public IReadOnlyCollection<string> PlayerIds
        {
            get { return players.Keys.ToList(); }
        }

        public GatewayConnection Get(int connectionId)
        {
            return connections.TryGetValue(connectionId, out var connection) ? connection : null;
        }

        /// <summary>The connection the player is logged in on at this node, or null.</summary>
        public GatewayConnection GetByPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return null;
            return players.TryGetValue(playerId, out var connection) ? connection : null;
        }

        /// <summary>
        /// Queues a frame for the player's connection on this node. False when the player has no
        /// connection here (an event can still arrive just after a socket closed) or it is closing.
        /// </summary>
        public bool SendToPlayer(string playerId, byte[] frame)
        {
            var connection = GetByPlayer(playerId);
            return connection != null && connection.TrySend(frame);
        }

        /// <summary>True while <paramref name="connection"/> is registered, that is, its socket has not closed.</summary>
        public bool IsOpen(GatewayConnection connection)
        {
            return connection != null && connections.TryGetValue(connection.Id, out var current) && ReferenceEquals(current, connection);
        }

        internal void Add(GatewayConnection connection)
        {
            if (!connections.TryAdd(connection.Id, connection))
                throw new InvalidOperationException($"Connection {connection.Id} is registered already.");
        }

        /// <summary>Removes the connection; false when it was removed already.</summary>
        internal bool Remove(GatewayConnection connection)
        {
            return connections.TryRemove(new KeyValuePair<int, GatewayConnection>(connection.Id, connection));
        }

        internal void MapPlayer(string playerId, GatewayConnection connection)
        {
            players[playerId] = connection;
        }

        /// <summary>Removes the player's mapping only while it still points at <paramref name="connection"/>.</summary>
        internal bool UnmapPlayer(string playerId, GatewayConnection connection)
        {
            return players.TryRemove(new KeyValuePair<string, GatewayConnection>(playerId, connection));
        }
    }
}
