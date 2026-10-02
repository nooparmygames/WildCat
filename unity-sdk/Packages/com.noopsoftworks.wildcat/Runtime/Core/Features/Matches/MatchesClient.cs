using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Relay matches: properties shared with the other players, leaving, rejoining after a
    /// disconnect, and the match events. Game messages between players go through
    /// <see cref="RelayClient"/>.
    /// </summary>
    public sealed class MatchesClient : FeatureClient
    {
        /// <summary>The largest property value the server takes.</summary>
        public const int MaxPropertyBytes = 10 * 1024;

        internal MatchesClient(WildCatConnection connection)
            : base(connection)
        {
            connection.RegisterHandler(WildCatMsgType.GamePlayerAddedEvent, frame =>
            {
                PlayerAddedToGameEvent message = frame.GetPayload<PlayerAddedToGameEvent>();
                Raise(PlayerJoined, new MatchPlayerEvent(message.Game, message.Player));
            });
            connection.RegisterHandler(WildCatMsgType.GamePlayerRemovedEvent, frame =>
            {
                MatchPlayerEventData message = frame.GetPayload<MatchPlayerEventData>();
                Raise(PlayerLeft, new MatchPlayerEvent(message.Game, message.Player));
            });
            connection.RegisterHandler(WildCatMsgType.GamePlayerRejoinedEvent, frame =>
            {
                MatchPlayerEventData message = frame.GetPayload<MatchPlayerEventData>();
                Raise(PlayerRejoined, new MatchPlayerEvent(message.Game, message.Player));
            });
            connection.RegisterHandler(WildCatMsgType.MatchRejoinSyncEvent, frame =>
            {
                MatchSyncEventData message = frame.GetPayload<MatchSyncEventData>();
                Raise(Resynced, new MatchState(message.Game, message.gameProperties, message.playerProperties));
            });
            connection.RegisterHandler(WildCatMsgType.MatchPropertyChangedEvent, frame =>
            {
                MatchPropertyChangeData message = frame.GetPayload<MatchPropertyChangeData>();
                Raise(PropertyChanged, new MatchPropertyChange(message.Game, message.isGameProperty ? null : message.Player,
                    message.key, message.value));
            });
            connection.RegisterHandler(WildCatMsgType.GameFinishedEvent, frame =>
            {
                GameFinishedEvent message = frame.GetPayload<GameFinishedEvent>();
                Raise(Finished, new MatchFinished(message.Game, message.FinishedWithDisconnect));
            });
        }

        /// <summary>A player entered the match (also sent for the logged in player). Carries the account id.</summary>
        public event Action<MatchPlayerEvent> PlayerJoined;

        /// <summary>A player disconnected from the match (game modes that go on without them).</summary>
        public event Action<MatchPlayerEvent> PlayerLeft;

        /// <summary>A player rejoined the match.</summary>
        public event Action<MatchPlayerEvent> PlayerRejoined;

        /// <summary>After <see cref="RejoinAsync"/>: every property of the match.</summary>
        public event Action<MatchState> Resynced;

        /// <summary>
        /// A property changed. Global changes reach every player, the setter included; player
        /// property changes reach the other players only.
        /// </summary>
        public event Action<MatchPropertyChange> PropertyChanged;

        /// <summary>The match ended: every player left it, or a player disconnected in a game mode that ends then.</summary>
        public event Action<MatchFinished> Finished;

        /// <summary>
        /// Leaves the match (LeaveGame). The match ends, with <see cref="Finished"/>, once every
        /// player has left. Error code 1 when the match is not running.
        /// </summary>
        public Task LeaveAsync(Guid matchId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Guard(() =>
            {
                RequireMatch(matchId);
                return Connection.SendRequestAsync<EmptyMessage>(WildCatMsgType.LeaveGameRequest, new EmptyMessage(),
                    WildCatMsgType.LeaveGameResponse, true, matchId.ToString(), null, false, cancellationToken);
            });
        }

        /// <summary>
        /// Sets a property of the logged in player in the match. The other players get
        /// <see cref="PropertyChanged"/>; a player who rejoins gets it in <see cref="Resynced"/>.
        /// </summary>
        public Task SetPlayerPropertyAsync(Guid matchId, string key, byte[] value, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SetPropertyAsync(matchId, true, key, value, cancellationToken);
        }

        /// <summary>Sets a player property to an encoded payload; read it with <see cref="MatchPropertyChange.GetValue{T}"/>.</summary>
        public Task SetPlayerPropertyAsync<T>(Guid matchId, string key, T value, CancellationToken cancellationToken = default(CancellationToken))
            where T : MessageBase
        {
            return Guard(() => SetPropertyAsync(matchId, true, key, WireCodec.EncodePayload(value), cancellationToken));
        }

        /// <summary>
        /// Sets a global property of the match. Every player, the setter included, gets
        /// <see cref="PropertyChanged"/>. When two players set the same key at once both changes
        /// arrive in the same order everywhere, so the last event holds the current value.
        /// </summary>
        public Task SetGlobalPropertyAsync(Guid matchId, string key, byte[] value, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SetPropertyAsync(matchId, false, key, value, cancellationToken);
        }

        /// <summary>Sets a global property to an encoded payload.</summary>
        public Task SetGlobalPropertyAsync<T>(Guid matchId, string key, T value, CancellationToken cancellationToken = default(CancellationToken))
            where T : MessageBase
        {
            return Guard(() => SetPropertyAsync(matchId, false, key, WireCodec.EncodePayload(value), cancellationToken));
        }

        private Task SetPropertyAsync(Guid matchId, bool isPlayerProperty, string key, byte[] value, CancellationToken cancellationToken)
        {
            return Guard(() =>
            {
                RequireMatch(matchId);
                RequireText(key, "key");
                byte[] bytes = value ?? new byte[0];
                if (bytes.Length > MaxPropertyBytes)
                    throw new ArgumentException("Match property values are limited to " + MaxPropertyBytes + " bytes.", nameof(value));
                return Request<EmptyMessage>(WildCatMsgType.SetMatchPropertyRequest,
                    new SetMatchPropertyData { Game = matchId, isPlayerProperty = isPlayerProperty, key = key, value = bytes },
                    WildCatMsgType.SetMatchPropertyResponse, cancellationToken);
            });
        }

        /// <summary>
        /// Rejoins a match from <see cref="GetPendingAsync"/> after a disconnect. The other players
        /// get <see cref="PlayerRejoined"/>, this player gets <see cref="Resynced"/>.
        /// </summary>
        public Task RejoinAsync(Guid matchId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Guard(() =>
            {
                RequireMatch(matchId);
                return Request<EmptyMessage>(WildCatMsgType.RejoinMatchRequest, new GuidMessage { value = matchId },
                    WildCatMsgType.RejoinMatchResponse, cancellationToken);
            });
        }

        /// <summary>The matches the logged in player is still part of, for example after a reconnection.</summary>
        public async Task<IReadOnlyList<PendingMatch>> GetPendingAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            PendingMatchesCollectionData answer = await Request<PendingMatchesCollectionData>(WildCatMsgType.GetPlayerPendingMatchesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerMatchesResponse, cancellationToken).ConfigureAwait(false);
            List<PendingMatch> result = new List<PendingMatch>();
            if (answer.matches != null)
            {
                foreach (PendingMatchesCollectionData.PendingMatchInfo match in answer.matches)
                    result.Add(new PendingMatch(match.matchId, match.startTime, match.players));
            }
            return result;
        }

        private static void RequireMatch(Guid matchId)
        {
            if (matchId == Guid.Empty)
                throw new ArgumentException("The match id must be set.", nameof(matchId));
        }
    }
}
