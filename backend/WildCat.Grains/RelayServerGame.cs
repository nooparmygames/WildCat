using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;
using WildCat.Protocol;

namespace WildCat.Grains
{
    class RelayServerGame : Grain, IRelayServerGame
    {
        private readonly IPersistentState<RelayServerGameState> state;
        private readonly ILogger<RelayServerGame> logger;

        public RelayServerGame(
            [PersistentState(GrainStateNames.RelayServerGame, GrainStorageNames.Default)] IPersistentState<RelayServerGameState> state,
            ILogger<RelayServerGame> logger)
        {
            this.state = state;
            this.logger = logger;
        }

        private RelayServerGameState State => state.State;

        private const int EVENT_CHANNEL = 0;

        /// <summary>
        /// The mode the match was started with. It is kept in state, so a match that was
        /// deactivated still knows it (the legacy code kept it in a field and threw a
        /// NullReferenceException on the next disconnect or finish).
        /// </summary>
        private GameMode Mode => State.Mode ?? GameMode.CreateDefault();

        public Task<RelayServerGameStatus> GetGameState()
        {
            return Task.FromResult(State.State);
        }

        public async Task<List<string>> GetPlayerNames()
        {
            var nameTasks = State.Players.Select(x => x.player.GetPlayerName()).ToList();
            var names = await Task.WhenAll(nameTasks);
            return names.ToList();
        }

        public Task<RelayServerPlayerGameStatus> GetPlayerStatus(IPlayer player)
        {
            return Task.FromResult(State.GetPlayerData(player).state);
        }

        public Task<List<IPlayer>> GetPlayers()
        {
            return Task.FromResult(State.Players.Select(x => x.player).ToList());
        }

        //called by player
        public async Task<bool> PlayerDisconnected(IPlayer player)
        {
            var playerState = State.GetPlayerData(player);
            // A finished match, or one the player has left, has nothing left to do with the
            // player. (Both players of a match that ends on a disconnect used to end it in turn,
            // pushing GameFinishedEvent twice.)
            if (!IsRunning() || playerState.state == RelayServerPlayerGameStatus.Finished)
                return true;
            playerState.state = RelayServerPlayerGameStatus.Disconnected;
            if (Mode.FinishGameWithOneDisconnect)
            {
                State.State = RelayServerGameStatus.FinishedWithDisconnect;
                // The match is over for everyone else too. (This was a Select with side effects
                // that nothing enumerated, so the others stayed InGame.)
                foreach (var other in State.Players)
                {
                    if (other != playerState && other.state != RelayServerPlayerGameStatus.Disconnected)
                        other.state = RelayServerPlayerGameStatus.Finished;
                }
                await state.WriteStateAsync();
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = WildCatMessageBase.Create(
                    new GameFinishedEvent { Game = primary, FinishedWithDisconnect = true }, WildCatMsgType.GameFinishedEvent, WildCatMsgHeader.normal);
                await SendFinishDataToAllPlayers(EVENT_CHANNEL, WireCodec.Encode(msg));
                return true;
            }
            else
            {
                if (State.Players.Any(x => x.state == RelayServerPlayerGameStatus.Finished))
                    State.State = RelayServerGameStatus.SomeFinished;
                else if (State.Players.Any(x => x.state == RelayServerPlayerGameStatus.InGame))
                    State.State = RelayServerGameStatus.SomeJoined;
                else
                    State.State = RelayServerGameStatus.Started;
                await state.WriteStateAsync();
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = WildCatMessageBase.Create(
                    new MatchPlayerEventData { Game = primary, Player = player.GetPrimaryKeyString() }, WildCatMsgType.GamePlayerRemovedEvent, WildCatMsgHeader.normal);
                await SendDataToAllPlayersExceptOne(player, EVENT_CHANNEL, WireCodec.Encode(msg));
                return false;
            }
        }

        //calls player
        public async Task PlayerFinished(IPlayer player)
        {
            // Look the player up first: someone who is not in the match must not change its state.
            var playerState = State.GetPlayerData(player);
            var match = this.AsReference<IRelayServerGame>();
            // Leaving a finished match changes nothing in it (it used to turn FinishedWithDisconnect
            // into AllFinished and push GameFinishedEvent again).
            if (!IsRunning())
            {
                await player.GameFinished(match);
                return;
            }
            State.State = RelayServerGameStatus.SomeFinished;
            playerState.state = RelayServerPlayerGameStatus.Finished;
            bool allFinished = State.Players.All(x => x.state == RelayServerPlayerGameStatus.Finished
                || (Mode.FinishGameWithOneDisconnect && x.state == RelayServerPlayerGameStatus.Disconnected));
            if (allFinished)
                State.State = RelayServerGameStatus.AllFinished;
            await state.WriteStateAsync();
            if (allFinished)
            {
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = WildCatMessageBase.Create(
                    new GameFinishedEvent { Game = primary, FinishedWithDisconnect = false }, WildCatMsgType.GameFinishedEvent, WildCatMsgHeader.normal);
                await SendFinishDataToAllPlayers(EVENT_CHANNEL, WireCodec.Encode(msg));
            }
            // GameFinished is one way, so this only sends it: the player may be waiting on this
            // match right now (its socket closed and Disconnected called PlayerDisconnected), and
            // waiting for the player's answer here deadlocked both.
            await player.GameFinished(match);
        }

        public async Task<Dictionary<IPlayer, Tuple<Guid, string>>> StartGame(IPlayer[] players, GameMode gameMode)
        {
            if (gameMode == null)
            {
                gameMode = GameMode.CreateDefault();
            }
            State.Mode = gameMode;
            State.MaxPlayers = gameMode.MaxPlayers;
            if (State.MaxPlayers < players.Length)
                throw new ArgumentException("argument count should be 2 but is " + players.Length);

            State.Players = new List<RelayServerGamePlayerState>();
            foreach (var p in players)
            {
                RelayServerGamePlayerState data = new RelayServerGamePlayerState();
                data.state = RelayServerPlayerGameStatus.NotStarted;
                data.player = p;
                data.lastMessageTime = DateTime.Now;
                State.Players.Add(data);
            }

            State.StartTime = DateTime.Now;
            State.State = RelayServerGameStatus.Started;
            await state.WriteStateAsync();
            foreach (var p in players)
            {
                await p.GameStarted(this.AsReference<IRelayServerGame>());
            }
            string[] strings = new string[State.Players.Count];
            Dictionary<IPlayer, Tuple<Guid, string>> returning = new Dictionary<IPlayer, Tuple<Guid, string>>();
            for (int i = 0; i < State.Players.Count; ++i)
            {
                string s;
                var guid = this.GetPrimaryKey(out s);
                returning.Add(State.Players[i].player, Tuple.Create(guid, s));
            }
            return returning;
        }

        //called by match maker or external client
        public async Task PlayerStarted(IPlayer player)
        {
            var playerState = State.GetPlayerData(player);
            // Joining used to bring a finished match back to SomeJoined, and a player who had
            // left back InGame although the player's own state no longer listed the match.
            if (!IsRunning())
                throw new GameNotRunningException(RelayServerGameState.MatchFinished);
            if (playerState.state == RelayServerPlayerGameStatus.Finished)
                throw new GameNotRunningException(RelayServerGameState.LeftMatch);
            playerState.state = RelayServerPlayerGameStatus.InGame;
            playerState.lastMessageTime = DateTime.Now;
            State.State = RelayServerGameStatus.SomeJoined;
            if (State.Players.All(x => x.state == RelayServerPlayerGameStatus.InGame))
                State.State = RelayServerGameStatus.AllJoined;
            await state.WriteStateAsync();
            string extended;
            var primary = this.GetPrimaryKey(out extended);
            // The account id, like every other match event. (It was the display name, which also
            // took a call into the player that could close a cycle with the player's Disconnected.)
            var msg = WildCatMessageBase.Create(
                new PlayerAddedToGameEvent { Game = primary, Player = player.GetPrimaryKeyString() },
                WildCatMsgType.GamePlayerAddedEvent, WildCatMsgHeader.normal);
            await SendPlayerAddedDataToAllPlayers(EVENT_CHANNEL, WireCodec.Encode(msg));
        }

        //Called by player
        public async Task PlayerRejoined(IPlayer player)
        {
            var playerState = State.GetPlayerData(player);
            // A finished match stays finished (rejoining one used to set it back to SomeJoined).
            if (!IsRunning())
                throw new GameNotRunningException(RelayServerGameState.MatchFinished);
            // Only a player who dropped out comes back. A player who left used to be set back
            // InGame although the player's own state no longer listed the match.
            if (playerState.state != RelayServerPlayerGameStatus.Disconnected)
                throw new GameNotRunningException(RelayServerGameState.NotDroppedOut);
            playerState.state = RelayServerPlayerGameStatus.InGame;
            playerState.lastMessageTime = DateTime.Now;
            if (State.State != RelayServerGameStatus.SomeFinished)
            {
                State.State = RelayServerGameStatus.SomeJoined;
                if (State.Players.All(x => x.state == RelayServerPlayerGameStatus.InGame))
                    State.State = RelayServerGameStatus.AllJoined;
            }
            await state.WriteStateAsync();
            string extended;
            var primary = this.GetPrimaryKey(out extended);
            var msg = WildCatMessageBase.Create(
                new MatchPlayerEventData
                {
                    Game = primary,
                    Player = player.GetPrimaryKeyString()
                },
                WildCatMsgType.GamePlayerRejoinedEvent, WildCatMsgHeader.normal);
            MatchSyncEventData syncData = new MatchSyncEventData
            {
                Game = primary,
                gameProperties = State.GameProperties,
            };
            foreach (var kvp in State.PlayerProperties)
            {
                syncData.playerProperties.Add(kvp.Key, kvp.Value);
            }
            var syncMsg = WildCatMessageBase.Create(
                syncData,
                WildCatMsgType.MatchRejoinSyncEvent, WildCatMsgHeader.normal);
            await SendPlayerAddedDataToAllPlayers(EVENT_CHANNEL, WireCodec.Encode(msg));
            await SendDataToPlayers(EVENT_CHANNEL, WireCodec.Encode(syncMsg), new RelayServerGamePlayerState[] { playerState }, false);
        }

        public Task SendToAllBut(IPlayer player, int channelId, byte[] data)
        {
            if (!IsItValidToSendData())
                return Task.CompletedTask;
            // Only a player in the match relays into it: not an outsider who knows the match id,
            // nor a player who left, dropped out or never joined.
            if (State.FindPlayerData(player)?.state != RelayServerPlayerGameStatus.InGame)
                return Task.CompletedTask;
            return SendDataToAllPlayersExceptOne(player, channelId, data);
        }

        private Task SendDataToAllPlayersExceptOne(IPlayer player, int channelId, byte[] data)
        {
            string s;
            var gk = this.GetPrimaryKey(out s);
            var players = State.Players.
                            Where(x => x.player.GetPrimaryKeyString() != player.GetPrimaryKeyString()
                            && x.state != RelayServerPlayerGameStatus.Disconnected);
            return SendDataToPlayers(channelId, data, players, false);

        }

        /// <summary>
        /// Pushes <paramref name="data"/> to the players who are in the match (and to those who
        /// finished it when <paramref name="sendToFinished"/>). Waits for every push, so pushes of
        /// one match to one player keep their order. A push that fails is logged and reaches only
        /// that player: the change that caused it still completes. (A failed publish, such as a
        /// full stream queue, used to fail the whole call halfway.)
        /// </summary>
        private async Task SendDataToPlayers(int channelId, byte[] data, IEnumerable<RelayServerGamePlayerState> players, bool sendToFinished)
        {
            var sends = new List<(IPlayer Player, Task Send)>();
            foreach (var player in players)
            {
                if ((player.state == RelayServerPlayerGameStatus.InGame || (player.state == RelayServerPlayerGameStatus.Finished && sendToFinished)))
                    sends.Add((player.player, player.player.SendDataToConnection(player.player, channelId, data)));
            }
            foreach (var (player, send) in sends)
            {
                try
                {
                    await send;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not push to player {PlayerId} in match {MatchId}",
                        player.GetPrimaryKeyString(), this.GetPrimaryKey(out string _));
                }
            }
        }

        private Task SendFinishDataToAllPlayers(int channelId, byte[] data)
        {
            var players = State.Players.
                            Where(x => x.state != RelayServerPlayerGameStatus.Disconnected);
            return SendDataToPlayers(channelId, data, players, true);
        }

        private Task SendPlayerAddedDataToAllPlayers(int channelId, byte[] data)
        {
            var players = State.Players.
                            Where(x => x.state != RelayServerPlayerGameStatus.Disconnected);
            return SendDataToPlayers(channelId, data, players, false);
        }

        private bool IsItValidToSendData()
        {
            return State.State == RelayServerGameStatus.SomeJoined
                || State.State == RelayServerGameStatus.AllJoined;
        }

        public Task<bool> IsValidGame()
        {
            return Task.FromResult(IsRunning());
        }

        private bool IsRunning()
        {
            return State.State != RelayServerGameStatus.AllFinished && State.State != RelayServerGameStatus.FinishedWithDisconnect;
        }

        public async Task<bool> SetGameProperty(IPlayer player, string key, byte[] value)
        {
            // Only a player of the match may change it.
            State.GetPlayerData(player);
            string keyExtension;
            var primaryKey = this.GetPrimaryKey(out keyExtension);
            if (!State.SetGameProperty(key, value))
                return false;
            await state.WriteStateAsync();
            var propertyChangeMsg = WildCatMessageBase.Create(
                new MatchPropertyChangeData
                {
                    Game = primaryKey,
                    Player = "",
                    key = key,
                    value = value
                },
                WildCatMsgType.MatchPropertyChangedEvent, WildCatMsgHeader.normal);
            await SendDataToPlayers(EVENT_CHANNEL, WireCodec.Encode(propertyChangeMsg), State.Players, false);
            return true;
        }

        public async Task<bool> SetPlayerProperty(IPlayer player, string key, byte[] value)
        {
            string ext;
            var primary = this.GetPrimaryKey(out ext);
            if (!State.SetPlayerProperties(player, key, value))
                return false;
            await state.WriteStateAsync();
            var msg = WildCatMessageBase.Create(
                new MatchPropertyChangeData
                {
                    Game = primary,
                    Player = player.GetPrimaryKeyString(),
                    key = key,
                    value = value
                },
                WildCatMsgType.MatchPropertyChangedEvent, WildCatMsgHeader.normal);
            await SendDataToAllPlayersExceptOne(player, EVENT_CHANNEL, WireCodec.Encode(msg));
            return true;
        }

        public Task<RelayServerGameState> GetState()
        {
            return Task.FromResult(State);
        }
    }
}
