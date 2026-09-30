using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Orleans.Concurrency;
using WildCat.Interfaces;
using Orleans.Streams;

namespace WildCat.Grains
{
    /// <summary>
    /// One player's direct 1v1 challenges in one game (key: game key plus player id). The
    /// challenger's grain keeps the sent requests, the challengee's the received ones; accepting
    /// creates a RelayServerGame and pushes its id to both players on their ChallengeEvents streams.
    /// </summary>
    public class PlayerChallengeHandler : Grain, IPlayerChallengeHandler
    {
        private readonly IPersistentState<PlayerChallengesState> state;

        public PlayerChallengeHandler([PersistentState(GrainStateNames.PlayerChallengeHandler, GrainStorageNames.Default)] IPersistentState<PlayerChallengesState> state)
        {
            this.state = state;
        }

        private PlayerChallengesState State => state.State;

        private IPlayer player;
        private Guid gameKey;
        private IAsyncStream<ChallengeEventData> challengeEvents;
        private IStreamProvider provider;

        public async override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            string playerId;
            gameKey = this.GetPrimaryKey(out playerId);

            player = GrainFactory.GetGrain<IPlayer>(playerId);
            provider = this.GetStreamProvider(StreamNames.PROVIDER_NAME);
            challengeEvents = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(gameKey, playerId));
            await base.OnActivateAsync(cancellationToken);
        }

        private static bool IsFrom(ChallengeRequestState request, IPlayer other)
        {
            return request.Player.GetPrimaryKeyString() == other.GetPrimaryKeyString();
        }

        private static bool IsPending(ChallengeRequestState request, IPlayer other)
        {
            return request.State == ChallengeRequestState.Status.Pending && IsFrom(request, other);
        }

        private static bool HasTimedOut(ChallengeRequestState request)
        {
            return (DateTime.UtcNow - request.RequestTime).TotalSeconds >= request.Timeout;
        }

        public async Task AcceptChallenge(IPlayer accepter, byte[] data)
        {
            // Only a pending challenge can be accepted: accepting a started one again used to
            // start a second match, and a missing one threw from First.
            int index = State.RequestsSent.FindLastIndex(x => IsPending(x, accepter));
            if (index < 0)
                throw new ChallengeNotFoundException();
            var req = State.RequestsSent[index];
            State.RequestsSent.RemoveAll(x => IsFrom(x, accepter));

            if (HasTimedOut(req))
            {
                // The expired challenge goes from both lists, so neither player keeps listing it.
                await state.WriteStateAsync();
                GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, accepter.GetPrimaryKeyString(), null).RemovedFromReceivedList(player).Ignore();
                throw new ChallengeTimeoutException();
            }

            var createdGame = await CreateGame(new IPlayer[] { player, accepter }, $"challenge-{DateTime.UtcNow:O}");
            string unUsed;
            Guid gameId = createdGame.GetPrimaryKey(out unUsed);
            req.Game = gameId;
            req.State = ChallengeRequestState.Status.Started;
            State.RequestsSent.Add(req);
            await state.WriteStateAsync();
            ChallengeEventData myStreamEventData = new ChallengeEventData
            {
                TitleKey = this.gameKey,
                otherPlayer = accepter,
                data = data,
                Type = ChallengeEventData.EventType.Started,
                gameID = gameId
            };

            ChallengeEventData accepterStreamEventData = new ChallengeEventData
            {
                TitleKey = this.gameKey,
                otherPlayer = player,
                data = data,
                Type = ChallengeEventData.EventType.Started,
                gameID = gameId
            };

            challengeEvents.OnNextAsync(myStreamEventData).Ignore();
            var otherPlayerStream = provider.GetStream<ChallengeEventData>(StreamNames.ChallengeEvents(gameKey, accepter.GetPrimaryKeyString()));
            otherPlayerStream.OnNextAsync(accepterStreamEventData).Ignore();
            GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, accepter.GetPrimaryKeyString(), null).UpdateInReceiveList(player, gameId).Ignore();
        }

        public async Task RemovedFromReceivedList(IPlayer other)
        {
            State.RequestsReceived.RemoveAll(x => IsFrom(x, other));
            await state.WriteStateAsync();
        }

        public Task UpdateInReceiveList(IPlayer other, Guid gameKey)
        {
            // FindLastIndex rather than First, which threw when the entry was missing.
            int index = State.RequestsReceived.FindLastIndex(x => IsPending(x, other));
            var req = index < 0 ? new ChallengeRequestState { Player = other } : State.RequestsReceived[index];
            State.RequestsReceived.RemoveAll(x => IsFrom(x, other));
            req.Game = gameKey;
            req.State = ChallengeRequestState.Status.Started;
            State.RequestsReceived.Add(req);
            return state.WriteStateAsync();
        }

        public async Task<IRelayServerGame> CreateGame(IPlayer[] players, string gameName)
        {
            var game = GrainFactory.GetGrain<IRelayServerGame>(Guid.NewGuid(), gameKey.ToString(), null);
            GameMode gameMode = GameMode.CreateDefault();
            gameMode.FinishGameWithOneDisconnect = false;
            var names = await game.StartGame(players, gameMode);
            foreach (var p in players)
            {
                await game.PlayerStarted(p);
            }
            return game;
        }

        public async Task DenyChallenge(IPlayer denyer, byte[] data)
        {
            // Only a pending challenge to the denying player is denied and reported; before, any
            // player could push a ChallengeDeniedEvent to any other player of the game.
            if (!State.RequestsSent.Any(x => IsPending(x, denyer)))
                return;
            State.RequestsSent.RemoveAll(x => IsFrom(x, denyer));
            await state.WriteStateAsync();
            ChallengeEventData myStreamEventData = new ChallengeEventData
            {
                TitleKey = this.gameKey,
                otherPlayer = denyer,
                data = data,
                Type = ChallengeEventData.EventType.Deny
            };
            challengeEvents.OnNextAsync(myStreamEventData).Ignore();
            GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, denyer.GetPrimaryKeyString(), null).RemovedFromReceivedList(player).Ignore();
        }

        //called by other player's PlayerChallengeHandler
        public async Task<ChallengeAcceptionStatus> ChallengeRequested(IPlayer challenger, byte[] data, bool acceptIfOffline)
        {
            if (!acceptIfOffline)
            {
                bool isLoggedIn = await player.IsLoggedIn();
                if (!isLoggedIn)
                {
                    return ChallengeAcceptionStatus.Disconnected;
                }
            }
            // This player challenged the challenger already and that challenge is still open.
            // (Any earlier entry used to count, so after one challenge between two players the
            // other one could never challenge back.)
            if (State.RequestsSent.Any(x => IsPending(x, challenger) && !HasTimedOut(x)))
            {
                return ChallengeAcceptionStatus.RequestedYouAsWell;
            }
            else
            {
                ChallengeEventData eventData = new ChallengeEventData
                {
                    TitleKey = this.gameKey,
                    otherPlayer = challenger,
                    data = data,
                    Type = ChallengeEventData.EventType.Request
                };
                // A new challenge from the same player replaces the pending one.
                State.RequestsReceived.RemoveAll(x => IsPending(x, challenger));
                State.RequestsReceived.Add(new ChallengeRequestState
                {
                    Player = challenger,
                    data = data
                });
                await state.WriteStateAsync();
                challengeEvents.OnNextAsync(eventData).Ignore();
                return ChallengeAcceptionStatus.Received;
            }
        }


        //called from outside on challenger with challengee passed as arg
        public async Task<ChallengeAcceptionStatus> SendChallengeRequest(IPlayer challengee, byte[] data, uint timeout, bool isAcceptedOffline)
        {

            var requestResult = await (GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, challengee.GetPrimaryKeyString(), null)).ChallengeRequested(player, data, isAcceptedOffline);
            if (requestResult == ChallengeAcceptionStatus.Received)
            {
                State.RequestsSent.RemoveAll(x => IsPending(x, challengee));
                State.RequestsSent.Add(new ChallengeRequestState
                {
                    Player = challengee,
                    RequestTime = DateTime.UtcNow,
                    Timeout = timeout,
                    data = data
                });
                await state.WriteStateAsync();
            }
            return requestResult;
        }

        public Task<(List<ChallengeRequestState> sent, List<ChallengeRequestState> received)> GetChallengeRequests()
        {
            return Task.FromResult((State.RequestsSent, State.RequestsReceived));
        }

    }
}
