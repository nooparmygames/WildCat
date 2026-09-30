using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Streams;
using WildCat.Interfaces;

namespace WildCat.Gateway
{
    /// <summary>
    /// Subscribes a logged in connection to its player's streams and pushes what arrives on them
    /// to the client: PlayerConnection (frames the grains built, such as match events and relayed
    /// game messages), ChallengeEvents and FriendEvents. The callbacks only encode a frame and queue
    /// it on the connection (<see cref="GatewayConnection.TrySend"/>); they never wait for a socket,
    /// so one slow client cannot hold up the stream.
    /// </summary>
    /// <remarks>
    /// Stream subscriptions belong to this node's cluster client, not to a socket, so the callbacks
    /// look the player's connection up in the <see cref="ConnectionRegistry"/> each time. When the
    /// same player logs in on another socket of this node, that socket resumes the challenge and
    /// friend subscriptions and replaces the PlayerConnection one.
    /// </remarks>
    public sealed class PushSubscriptions
    {
        private const int SubscribeAttempts = 5;

        private readonly IClusterClient client;
        private readonly ConnectionRegistry registry;
        private readonly ILogger<PushSubscriptions> logger;
        private IStreamProvider streams;

        public PushSubscriptions(IClusterClient client, ConnectionRegistry registry, ILogger<PushSubscriptions> logger)
        {
            this.client = client;
            this.registry = registry;
            this.logger = logger;
        }

        // Resolved on first use rather than at construction, so the silo (and its stream
        // providers) is running before anything asks for it.
        private IStreamProvider Streams
        {
            get { return streams ??= client.GetStreamProvider(StreamNames.PROVIDER_NAME); }
        }

        /// <summary>
        /// Subscribes <paramref name="connection"/> to the streams of the player it just logged in
        /// as. Called by <see cref="LoginManager"/> with the connection's login lock held.
        /// </summary>
        internal async Task SubscribeAsync(GatewayConnection connection)
        {
            string playerId = connection.PlayerId;
            Guid gameKey = connection.GameKey;

            var connectionStream = Streams.GetStream<PlayerConnectionMessage>(StreamNames.PlayerConnection(playerId));
            await UnsubscribeAllAsync(connectionStream, connection.Id);
            connection.ConnectionHandle = await SubscribeWithRetriesAsync(connectionStream,
                (message, token) => OnPlayerConnectionMessage(message), connection.Id);

            var challenges = Streams.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(gameKey, playerId));
            connection.ChallengesHandle = await ResumeOrSubscribeAsync(challenges,
                (data, token) => OnChallengeEventAsync(playerId, data), connection.Id);

            var friends = Streams.GetStream<FriendListEventData>(StreamNames.FriendEvents(gameKey, playerId));
            connection.FriendsHandle = await ResumeOrSubscribeAsync(friends,
                (data, token) => OnFriendEvent(playerId, data), connection.Id);
        }

        /// <summary>Drops the connection's three subscriptions. Failures are logged, not thrown.</summary>
        internal async Task UnsubscribeAsync(GatewayConnection connection)
        {
            await UnsubscribeAsync(connection.ChallengesHandle, connection.Id);
            await UnsubscribeAsync(connection.FriendsHandle, connection.Id);
            await UnsubscribeAsync(connection.ConnectionHandle, connection.Id);
            connection.ChallengesHandle = null;
            connection.FriendsHandle = null;
            connection.ConnectionHandle = null;
        }

        private Task OnPlayerConnectionMessage(PlayerConnectionMessage message)
        {
            // The frame was encoded by the grain that sent it; it only has to be queued.
            if (message != null && message.eventType == PlayerConnectionMessage.EventType.DataTransfer
                                && message.player != null && message.data != null)
            {
                registry.SendToPlayer(message.player.GetPrimaryKeyString(), message.data);
            }
            return Task.CompletedTask;
        }

        private async Task OnChallengeEventAsync(string playerId, ChallengeEventData data)
        {
            var connection = registry.GetByPlayer(playerId);
            if (connection == null || data.TitleKey != connection.GameKey)
                return;
            string otherPlayerName = "";
            try
            {
                if (data.otherPlayer != null)
                    otherPlayerName = await data.otherPlayer.GetPlayerName();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading the other player's name for a {EventType} challenge event to player {PlayerId} failed",
                    data.Type, playerId);
            }

            try
            {
                connection.Send(ChallengeEventFrames.Build(data, otherPlayerName));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pushing a {EventType} challenge event to player {PlayerId} failed", data.Type, playerId);
            }
        }

        private Task OnFriendEvent(string playerId, FriendListEventData data)
        {
            var connection = registry.GetByPlayer(playerId);
            if (connection == null || data.GameKey != connection.GameKey)
                return Task.CompletedTask;
            try
            {
                connection.Send(FriendEventFrames.Build(data));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pushing a {EventType} friend event to player {PlayerId} failed", data.Type, playerId);
            }
            return Task.CompletedTask;
        }

        private async Task<StreamSubscriptionHandle<T>> SubscribeWithRetriesAsync<T>(IAsyncStream<T> stream,
            Func<T, StreamSequenceToken, Task> onNext, int connectionId)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return await stream.SubscribeAsync(onNext);
                }
                catch (Exception ex) when (attempt < SubscribeAttempts)
                {
                    logger.LogWarning(ex, "Subscribing connection {ConnectionId} to stream {StreamId} failed, trying again",
                        connectionId, stream.StreamId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Subscribing connection {ConnectionId} to stream {StreamId} failed {Attempts} times; it gets no events from it",
                        connectionId, stream.StreamId, SubscribeAttempts);
                    return null;
                }
            }
        }

        /// <summary>
        /// Resumes this node's subscription to the stream with the new callback, or subscribes when
        /// there is none; extra subscriptions are dropped so each event is pushed once.
        /// </summary>
        private async Task<StreamSubscriptionHandle<T>> ResumeOrSubscribeAsync<T>(IAsyncStream<T> stream,
            Func<T, StreamSequenceToken, Task> onNext, int connectionId)
        {
            try
            {
                var handles = await stream.GetAllSubscriptionHandles();
                if (handles.Count == 0)
                    return await stream.SubscribeAsync(onNext);
                for (int i = 1; i < handles.Count; ++i)
                    await UnsubscribeAsync(handles[i], connectionId);
                return await handles[0].ResumeAsync(onNext);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resuming stream {StreamId} for connection {ConnectionId} failed, subscribing again",
                    stream.StreamId, connectionId);
                return await SubscribeWithRetriesAsync(stream, onNext, connectionId);
            }
        }

        private async Task UnsubscribeAllAsync<T>(IAsyncStream<T> stream, int connectionId)
        {
            try
            {
                foreach (var handle in await stream.GetAllSubscriptionHandles())
                    await handle.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dropping the old subscriptions to stream {StreamId} for connection {ConnectionId} failed",
                    stream.StreamId, connectionId);
            }
        }

        private async Task UnsubscribeAsync<T>(StreamSubscriptionHandle<T> handle, int connectionId)
        {
            if (handle == null)
                return;
            try
            {
                await handle.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unsubscribing connection {ConnectionId} from stream {StreamId} failed",
                    connectionId, handle.StreamId);
            }
        }
    }
}
