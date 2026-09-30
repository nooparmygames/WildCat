using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>
    /// Binds logins to connections and releases them when a socket closes: the player to
    /// connection mapping in the <see cref="ConnectionRegistry"/>, the stream subscriptions
    /// (<see cref="PushSubscriptions"/>) and the IPlayer.Disconnected call. Both directions take
    /// the connection's login lock, so a login racing the socket's close cannot be left bound to a
    /// dead socket.
    /// </summary>
    public sealed class LoginManager
    {
        private readonly ConnectionRegistry registry;
        private readonly PushSubscriptions push;
        private readonly ILogger<LoginManager> logger;

        public LoginManager(ConnectionRegistry registry, PushSubscriptions push, ILogger<LoginManager> logger)
        {
            this.registry = registry;
            this.push = push;
            this.logger = logger;
        }

        /// <summary>
        /// Binds a successful login to <paramref name="connection"/>: the player mapping and the
        /// stream subscriptions. It checks under the login lock that the connection is still open,
        /// so a socket that closed while the login was in flight either is seen here, and the login
        /// is undone by telling the player grain it disconnected with this login's ticket, or waits
        /// and finds a finished login to release. Logging in as another player on the same socket
        /// releases the first player's streams and then tells that player it was disconnected.
        /// </summary>
        public async Task BindAsync(GatewayConnection connection, IPlayer player, Guid gameKey, Guid loginTicket)
        {
            logger.LogInformation("Connection {ConnectionId} logged in to game {GameKey}", connection.Id, gameKey);
            IPlayer previousPlayer = null;
            Guid previousLoginTicket = Guid.Empty;
            bool connectionClosed;
            await connection.LoginLock.WaitAsync();
            try
            {
                connectionClosed = !registry.IsOpen(connection);
                if (!connectionClosed)
                {
                    // Logging in as someone else on the same socket gives up the first player's
                    // streams, or that player's events would keep arriving on this socket.
                    if (connection.IsLoggedIn && connection.Player != null && !connection.Player.Equals(player))
                    {
                        previousPlayer = connection.Player;
                        previousLoginTicket = connection.LoginTicket;
                        await LogOutAsync(connection);
                    }
                    connection.Player = player;
                    connection.IsLoggedIn = true;
                    connection.LoginTicket = loginTicket;
                    connection.GameKey = gameKey;
                    registry.MapPlayer(player.GetPrimaryKeyString(), connection);
                    await push.SubscribeAsync(connection);
                }
            }
            finally
            {
                connection.LoginLock.Release();
            }

            if (connectionClosed)
            {
                // The socket closed while this login was in flight and the disconnect cleanup had
                // nothing to release. Tell the player grain now instead of leaving it logged in.
                logger.LogInformation("Connection {ConnectionId} closed while player {PlayerId} was logging in",
                    connection.Id, player.GetPrimaryKeyString());
                await TellPlayerDisconnectedAsync(player, loginTicket, connection.Id);
                return;
            }

            // The first player's login no longer has a socket. Its own ticket makes the grain log it
            // out, unless it has logged in again somewhere else since.
            if (previousPlayer != null)
                await TellPlayerDisconnectedAsync(previousPlayer, previousLoginTicket, connection.Id);
        }

        /// <summary>
        /// Runs once when a socket closes, however it closed. Takes the connection out of the
        /// registry, releases the login it held and tells the player grain, passing the ticket of
        /// that login so the grain can ignore a stale disconnect after the player logged in again
        /// elsewhere. Never throws.
        /// </summary>
        internal async Task ReleaseAsync(GatewayConnection connection)
        {
            if (!registry.Remove(connection))
                return;
            logger.LogInformation("Connection {ConnectionId} disconnected", connection.Id);

            IPlayer player;
            Guid loginTicket;
            await connection.LoginLock.WaitAsync();
            try
            {
                player = connection.IsLoggedIn ? connection.Player : null;
                if (player == null)
                    return;
                loginTicket = connection.LoginTicket;
                try
                {
                    await LogOutAsync(connection);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Releasing the streams of connection {ConnectionId} failed", connection.Id);
                }
            }
            finally
            {
                connection.LoginLock.Release();
            }

            await TellPlayerDisconnectedAsync(player, loginTicket, connection.Id);
        }

        /// <summary>
        /// Undoes <see cref="BindAsync"/> for one connection. The stream subscriptions and the
        /// player mapping are released only while the mapping still points at this connection:
        /// once the same player logs in on another socket of this node, that socket owns them.
        /// </summary>
        private async Task LogOutAsync(GatewayConnection connection)
        {
            logger.LogInformation("Connection {ConnectionId} logged out", connection.Id);
            string playerId = connection.PlayerId;
            if (playerId != null && registry.UnmapPlayer(playerId, connection))
                await push.UnsubscribeAsync(connection);

            connection.ChallengesHandle = null;
            connection.FriendsHandle = null;
            connection.ConnectionHandle = null;
            connection.Player = null;
            connection.IsLoggedIn = false;
            connection.GameKey = Guid.Empty;
        }

        /// <summary>
        /// Tells a player grain that the login identified by <paramref name="loginTicket"/> no
        /// longer has a socket. Failures are logged, not thrown.
        /// </summary>
        private async Task TellPlayerDisconnectedAsync(IPlayer player, Guid loginTicket, int connectionId)
        {
            try
            {
                await player.Disconnected(loginTicket);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telling player {PlayerId} that connection {ConnectionId} closed failed",
                    player.GetPrimaryKeyString(), connectionId);
            }
        }
    }
}
