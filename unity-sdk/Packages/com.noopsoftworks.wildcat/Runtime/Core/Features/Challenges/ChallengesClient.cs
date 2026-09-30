using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WildCat.Protocol;

namespace WildCat
{
    /// <summary>
    /// Direct challenges between players: one player challenges another, who accepts (both get a
    /// match, see <see cref="ChallengeAccepted"/>) or denies.
    /// </summary>
    public sealed class ChallengesClient : FeatureClient
    {
        internal ChallengesClient(WildCatConnection connection)
            : base(connection)
        {
            connection.RegisterHandler(WildCatMsgType.StartChallengeRequestedEvent, frame => OnChallengeEvent(frame, ChallengeReceived));
            connection.RegisterHandler(WildCatMsgType.ChallengeAcceptedEvent, frame => OnChallengeEvent(frame, ChallengeAccepted));
            connection.RegisterHandler(WildCatMsgType.ChallengeDeniedEvent, frame => OnChallengeEvent(frame, ChallengeDenied));
        }

        /// <summary>Another player challenged the logged in player; answer with AcceptAsync or DenyAsync.</summary>
        public event Action<ChallengeEvent> ChallengeReceived;

        /// <summary>A challenge was accepted and its match started (sent to both players); <see cref="ChallengeEvent.MatchId"/> is the match.</summary>
        public event Action<ChallengeEvent> ChallengeAccepted;

        /// <summary>The challenged player denied the logged in player's challenge.</summary>
        public event Action<ChallengeEvent> ChallengeDenied;

        /// <summary>
        /// Challenges another player, online or not (the challenge waits for them until it times
        /// out). Error code 1 for an unknown player or yourself, 2 when they challenged the logged
        /// in player already (accept theirs instead). A new challenge to the same player replaces
        /// the pending one.
        /// </summary>
        public Task SendAsync(string playerId, uint timeoutSeconds = 15, byte[] data = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Guard(() =>
            {
                RequirePlayerId(playerId, "playerId");
                return Request<EmptyMessage>(WildCatMsgType.StartChallengeRequest,
                    new ChallengeMessage { Player = playerId, TimeOut = timeoutSeconds, data = data ?? new byte[0] },
                    WildCatMsgType.StartChallengeResponse, cancellationToken);
            });
        }

        /// <summary>Accepts the challenge from <paramref name="playerId"/>. Error code 1 after it timed out.</summary>
        public Task AcceptAsync(string playerId, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Guard(() =>
            {
                RequirePlayerId(playerId, "playerId");
                return Request<EmptyMessage>(WildCatMsgType.AcceptChallengeRequest,
                    new ChallengeMessage { Player = playerId, data = data ?? new byte[0] },
                    WildCatMsgType.AcceptChallengeResponse, cancellationToken);
            });
        }

        /// <summary>Denies the challenge from <paramref name="playerId"/>.</summary>
        public Task DenyAsync(string playerId, byte[] data = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Guard(() =>
            {
                RequirePlayerId(playerId, "playerId");
                return Request<EmptyMessage>(WildCatMsgType.DenyChallengeRequest,
                    new ChallengeMessage { Player = playerId, data = data ?? new byte[0] },
                    WildCatMsgType.DenyChallengeResponse, cancellationToken);
            });
        }

        /// <summary>The challenges the logged in player sent and received.</summary>
        public async Task<ChallengeList> GetAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ChallengeRequestsMessage answer = await Request<ChallengeRequestsMessage>(WildCatMsgType.GetPlayerChallengesRequest,
                new EmptyMessage(), WildCatMsgType.GetPlayerChallengesResponse, cancellationToken).ConfigureAwait(false);
            return new ChallengeList(Convert(answer.sent), Convert(answer.received));
        }

        private static List<ChallengeInfo> Convert(List<ChallengeRequestsMessage.Request> requests)
        {
            List<ChallengeInfo> result = new List<ChallengeInfo>();
            if (requests == null)
                return result;
            foreach (ChallengeRequestsMessage.Request request in requests)
                result.Add(new ChallengeInfo(request.Player, request.PlayerName, (ChallengeStatus)(int)request.status, request.gameId, request.data));
            return result;
        }

        private void OnChallengeEvent(WildCatMessageBase frame, Action<ChallengeEvent> handler)
        {
            OptionalResponse<ChallengeMessage> pushed = frame.GetPayload<OptionalResponse<ChallengeMessage>>();
            if (!pushed.isSuccesful || pushed.msg == null)
                return;
            ChallengeMessage message = pushed.msg;
            Raise(handler, new ChallengeEvent(message.Player, message.PlayerName, message.TimeOut, message.GameKey, message.data));
        }
    }
}
