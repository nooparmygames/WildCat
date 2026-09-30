using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using RealtimeServer;

namespace Collections
{
    class RelayServerGame : Grain<RelayServerGameState>, IRelayServerGame
    {
        private const int EVENT_CHANNEL = 0;
        private GameMode gameMode;

        public Task<RelayServerGameStatus> GetGameState()
        {
            return Task.FromResult(State.State);
        }

        public async Task<List<string>> GetPlayerNames()
        {
            var nameTasks = State.Players.Select(x => x.player.GetPlayerName()).ToList();
            await Task.WhenAll(nameTasks);
            return nameTasks.Select(x => x.Result).ToList();
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
            State.GetPlayerData(player).state = RelayServerPlayerGameStatus.Disconnected;
            if (gameMode.FinishGameWithOneDisconnect)
            {
                State.State = RelayServerGameStatus.FinishedWithDisconnect;
                State.Players.Where(x => x.player.GetPrimaryKeyString() != player.GetPrimaryKeyString()).Select(x =>
                {
                    x.state = RelayServerPlayerGameStatus.Finished;
                    return x;
                });
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = ApadanaMessageBase.Create(
                    new GameFinishedEvent { Game = primary, FinishedWithDisconnect = true }, ApadanaMsgType.GameFinishedEvent, ApadanaMsgHeader.normal);
                await SendFinishDataToAllPlayers(EVENT_CHANNEL, msg.ToByteArray());
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
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = ApadanaMessageBase.Create(
                    new MatchPlayerEventData { Game = primary, Player = player.GetPrimaryKeyString() }, ApadanaMsgType.GamePlayerRemovedEvent, ApadanaMsgHeader.normal);
                await SendDataToAllPlayersExceptOne(player, EVENT_CHANNEL, msg.ToByteArray());
                return false;
            }
        }

        //calls player
        public async Task PlayerFinished(IPlayer player)
        {
            State.State = RelayServerGameStatus.SomeFinished;
            State.GetPlayerData(player).state = RelayServerPlayerGameStatus.Finished;
            if (State.Players.All(x => x.state == RelayServerPlayerGameStatus.Finished || (gameMode.FinishGameWithOneDisconnect && x.state == RelayServerPlayerGameStatus.Disconnected)))
            {
                State.State = RelayServerGameStatus.AllFinished;
                string extended;
                var primary = this.GetPrimaryKey(out extended);
                var msg = ApadanaMessageBase.Create(
                    new GameFinishedEvent { Game = primary, FinishedWithDisconnect = false }, ApadanaMsgType.GameFinishedEvent, ApadanaMsgHeader.normal);
                await SendFinishDataToAllPlayers(EVENT_CHANNEL, msg.ToByteArray());
            }
            await player.GameFinished(this.AsReference<IRelayServerGame>());
        }

        public async Task<Dictionary<IPlayer, Tuple<Guid, string>>> StartGame(IPlayer[] players, GameMode gameMode)
        {
            if (gameMode == null)
            {
                gameMode = GameMode.CreateDefault();
            }
            this.gameMode = gameMode;
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
            await WriteStateAsync();
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
            playerState.state = RelayServerPlayerGameStatus.InGame;
            playerState.lastMessageTime = DateTime.Now;
            State.State = RelayServerGameStatus.SomeJoined;
            if (State.Players.All(x => x.state == RelayServerPlayerGameStatus.InGame))
                State.State = RelayServerGameStatus.AllJoined;
            await WriteStateAsync();
            string extended;
            var primary = this.GetPrimaryKey(out extended);
            var playerName = await player.GetPlayerName();
            var msg = ApadanaMessageBase.Create(
                new PlayerAddedToGameEvent { Game = primary, Player = playerName },
                ApadanaMsgType.GamePlayerAddedEvent, ApadanaMsgHeader.normal);
            await SendPlayerAddedDataToAllPlayers(EVENT_CHANNEL, msg.ToByteArray());
        }

        //Called by player
        public async Task PlayerRejoined(IPlayer player)
        {
            //await player.GameStarted(this.AsReference<IRelayServerGame>());
            var playerState = State.GetPlayerData(player);
            playerState.state = RelayServerPlayerGameStatus.InGame;
            playerState.lastMessageTime = DateTime.Now;
            if (State.State != RelayServerGameStatus.SomeFinished)
            {
                State.State = RelayServerGameStatus.SomeJoined;
                if (State.Players.All(x => x.state == RelayServerPlayerGameStatus.InGame))
                    State.State = RelayServerGameStatus.AllJoined;
            }
            await WriteStateAsync();
            string extended;
            var primary = this.GetPrimaryKey(out extended);
            var msg = ApadanaMessageBase.Create(
                new MatchPlayerEventData
                {
                    Game = primary,
                    Player = player.GetPrimaryKeyString()
                },
                ApadanaMsgType.GamePlayerRejoinedEvent, ApadanaMsgHeader.normal);
            MatchSyncEventData syncData = new MatchSyncEventData
            {
                Game = primary,
                gameProperties = State.GameProperties,
            };
            foreach (var kvp in State.PlayerProperties)
            {
                syncData.playerProperties.Add(kvp.Key.GetPrimaryKeyString(), kvp.Value);
            }
            var syncMsg = ApadanaMessageBase.Create(
                syncData,
                ApadanaMsgType.MatchRejoinSyncEvent, ApadanaMsgHeader.normal);
            await SendPlayerAddedDataToAllPlayers(EVENT_CHANNEL, msg.ToByteArray());
            await SendDataToPlayers(EVENT_CHANNEL, syncMsg.ToByteArray(), new RelayServerGamePlayerState[] { playerState }, false);
        }

        public Task SendToAllBut(IPlayer player, int channelId, byte[] data)
        {
            if (!IsItValidToSendData())
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

        private static async Task SendDataToPlayers(int channelId, byte[] data, IEnumerable<RelayServerGamePlayerState> players, bool sendToFinished)
        {
            List<Task> tasks = new List<Task>();
            foreach (var player in players)
            {
                if ((player.state == RelayServerPlayerGameStatus.InGame || (player.state == RelayServerPlayerGameStatus.Finished && sendToFinished)))
                    tasks.Add(player.player.SendDataToConnection(player.player, channelId, data));
            }
            await Task.WhenAll(tasks);
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

        public Task ReceivePlayerData(IPlayer player, int channelId, byte[] data)
        {
            return SendToAllBut(player, channelId, data);
        }

        public Task<bool> IsValidGame()
        {
            return Task.FromResult(State.State != RelayServerGameStatus.AllFinished && State.State != RelayServerGameStatus.FinishedWithDisconnect);
        }

        public async Task SetGameProperty(string key, byte[] value)
        {
            string keyExtension;
            var primaryKey = this.GetPrimaryKey(out keyExtension);
            if (State.SetGameProperty(key, value))
            {
                var propertyChangeMsg = ApadanaMessageBase.Create(
                new MatchPropertyChangeData
                {
                    Game = primaryKey,
                    Player = "",
                    key = key,
                    value = value
                },
                ApadanaMsgType.MatchPropertyChangedEvent, ApadanaMsgHeader.normal);
                await SendDataToPlayers(EVENT_CHANNEL, propertyChangeMsg.ToByteArray(), State.Players, false);
            }
        }

        public async Task SetPlayerProperty(IPlayer player, string key, byte[] value)
        {
            string ext;
            var primary = this.GetPrimaryKey(out ext);
            if (State.SetPlayerProperties(player, key, value))
            {
                var msg = ApadanaMessageBase.Create(
                new MatchPropertyChangeData
                {
                    Game = primary,
                    Player = player.GetPrimaryKeyString(),
                    key = key,
                    value = value
                },
                ApadanaMsgType.MatchPropertyChangedEvent, ApadanaMsgHeader.normal);
                await SendDataToAllPlayersExceptOne(player, EVENT_CHANNEL, msg.ToByteArray());
            }
        }

        public Task<RelayServerGameState> GetState()
        {
            return Task.FromResult(State);
        }
    }
}
