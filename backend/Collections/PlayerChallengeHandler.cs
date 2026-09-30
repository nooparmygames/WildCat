using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Interfaces;
using Orleans.Streams;

namespace Collections
{
    public class PlayerChallengeHandler : Grain<PlayerChallengesState>, IPlayerChallengeHandler
    {
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
        public async Task AcceptChallenge(IPlayer accepter, byte[] data)
        {
            var req = State.RequestsSent.First(x => x.Player.GetPrimaryKeyString() == accepter.GetPrimaryKeyString());
            State.RequestsSent.RemoveAll(x => x.Player.GetPrimaryKeyString() == accepter.GetPrimaryKeyString());

            if ((DateTime.Now - req.RequestTime).TotalSeconds < req.Timeout)
            {
                var createdGame = await CreateGame(new IPlayer[] { player, accepter }, $"challenge-{DateTime.Now}");
                string unUsed;
                Guid gameId = createdGame.GetPrimaryKey(out unUsed);
                req.Game = gameId;
                req.State = ChallengeRequestState.Status.Started;
                State.RequestsSent.Add(req);
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
            else
            {
                throw new ChallengeTimeoutException();
            }

        }

        public async Task RemovedFromReceivedList(IPlayer other)
        {
            State.RequestsReceived.RemoveAll(x => x.Player.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            await WriteStateAsync();
        }

        public Task UpdateInReceiveList(IPlayer other, Guid gameKey)
        {
            var req = State.RequestsReceived.First(x => x.Player.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            State.RequestsReceived.RemoveAll(x => x.Player.GetPrimaryKeyString() == other.GetPrimaryKeyString());
            req.Game = gameKey;
            req.State = ChallengeRequestState.Status.Started;
            State.RequestsReceived.Add(req);
            return WriteStateAsync();
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

        public Task DenyChallenge(IPlayer denyer, byte[] data)
        {
            State.RequestsSent.RemoveAll(x => x.Player.GetPrimaryKeyString() == denyer.GetPrimaryKeyString());
            ChallengeEventData myStreamEventData = new ChallengeEventData
            {
                TitleKey = this.gameKey,
                otherPlayer = denyer,
                data = data,
                Type = ChallengeEventData.EventType.Deny
            };
            challengeEvents.OnNextAsync(myStreamEventData).Ignore();
            GrainFactory.GetGrain<IPlayerChallengeHandler>(gameKey, denyer.GetPrimaryKeyString(), null).RemovedFromReceivedList(player).Ignore();
            return WriteStateAsync();
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
            //if we sent challenge request to the other successfully as well.
            ChallengeRequestState playerWithRequestSent = State.RequestsSent.FirstOrDefault(x => x.Player.GetPrimaryKeyString() == challenger.GetPrimaryKeyString());
            if (playerWithRequestSent.RequestTime != default(DateTime))
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
                State.RequestsReceived.Add(new ChallengeRequestState
                {
                    Player = challenger,
                    data = data
                });
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
                State.RequestsSent.Add(new ChallengeRequestState
                {
                    Player = challengee,
                    RequestTime = DateTime.Now,
                    Timeout = timeout
                });
            }
            return requestResult;
        }

        public Task<(List<ChallengeRequestState> sent, List<ChallengeRequestState> received)> GetChallengeRequests()
        {
            return Task.FromResult((State.RequestsSent, State.RequestsReceived));
        }

    }
}