using Interfaces;
using Newtonsoft.Json;
using Orleans;
using Orleans.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RealtimeServer;

namespace FrontEnd
{
    public class FrontEndServer : IFrontEndServer
    {
        private readonly ILogger _Logger;

        public Dictionary<int, PlayerInfo> clientConnections;
        public Dictionary<IPlayer, int> playerConnectionMapper;
        private readonly IClusterClient orleansClient;
        private IStreamProvider streamProvider;
        private const int MaxTrycount = 5;

#if !MASTER
        private static bool isMakingEnvironmentReady;
#endif

        public FrontEndServer(ILogger<FrontEndServer> logger, IClusterClient clusterClient)
        {
            clientConnections = new Dictionary<int, PlayerInfo>();
            playerConnectionMapper = new Dictionary<IPlayer, int>();
            orleansClient = clusterClient;
            streamProvider = orleansClient.GetStreamProvider(StreamNames.PROVIDER_NAME);
            this._Logger = logger;
        }

        private string GetConnectionGrainId(int connectionId)
        {
            return $"{Dns.GetHostName()}|{connectionId}";
        }

        public async Task PlayerConnected(ConnectionProcessor connection, int id)
        {
            connection.OnDataReceived += DataReceived;
            clientConnections[id] = new PlayerInfo(connection);
        }

        private async Task RegisterConnectionStream(IAsyncStream<UNetConnectionDataTransferMessage> stream)
        {
            for (int i = 0; i < 5; ++i)
            {
                try
                {
                    await stream.SubscribeAsync((data, token) => OnServerEvent(data));
                    break;
                }
                catch
                {
                }
            }
        }

        private async Task UnRegisterConnectionStream(IAsyncStream<UNetConnectionDataTransferMessage> stream, int id)
        {
            for (int i = 0; i < 5; ++i)
            {
                try
                {
                    var handles = await stream.GetAllSubscriptionHandles();
                    foreach (var handle in handles)
                    {
                        await handle.UnsubscribeAsync();
                    }

                    break;
                }
                catch
                {
                }
            }
        }

        private async Task OnServerEvent(UNetConnectionDataTransferMessage eventData)
        {
            switch (eventData.eventType)
            {
                case UNetConnectionDataTransferMessage.EventType.DataTransfer:
                    await Send(eventData.player, eventData.data);
                    break;
            }
        }

        public async Task PlayerDisconnected(ConnectionProcessor connection, int id)
        {
            if (clientConnections.ContainsKey(id))
            {
            }

            clientConnections.Remove(id);
            await ClientDisconnected(id);
        }
        //------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private PlayerInfo GetPlayerInfo(int id)
        {
            return clientConnections.ContainsKey(id) ? clientConnections[id] : null;
        }

        private async Task DataReceived(byte[] message, int id)
        {
            var playerInfo = GetPlayerInfo(id);
            if(playerInfo == null)
                return;
            // await GetClientConnectionGrain(id).DataReceived(id, 0, message, true);
            var connectionId = id;
            var isReliable = true;
            var channelId = 0;
            var connectionStream =
                streamProvider.GetStream<UNetConnectionDataTransferMessage>(
                    StreamNames.PlayerConnection(GetConnectionGrainId(connectionId)));

            ApadanaMessageBase msg = new ApadanaMessageBase(message);
            try
            {
                AssertSessionIfRequired(msg);
            }
            catch (SessionRequiredException ex)
            {
                _Logger.LogError((int) msg.msgType, "request without required session ticket");
                var returning = ApadanaMessageBase.Create
                (new StringMessage {value = ex.Message},
                    ApadanaMsgType.ErrorMsg,
                    ApadanaMsgHeader.normal);
                if (msg.hasRequestID)
                    returning.SetRequestID(msg.requestID);
                await connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
                {
                    player = null, connectionId = connectionId, channelId = channelId, data = returning.ToByteArray(),
                    eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
                });
                return;
            }

            IPlayer player = null;
            string userId;
            SessionGameInfo session = null;
            Guid ticket = Guid.Empty;
            if (msg.hasAuthData)
            {
                ticket = Guid.Parse(msg.ticket);

                if (msg.msgType > ApadanaMsgType.HighestInternal)
                {
                    session = (await orleansClient.GetGrain<ISession>(ticket).AddUserMessagesCheckSessionAndGetInfo(2));
                    userId = session.UserID;
                }
                else //for normal messages add calls in accounting
                {
                    session = (await orleansClient.GetGrain<ISession>(ticket).AddUserCallCheckSessionAndGetInfo());
                    userId = session.UserID;
                }

                player = orleansClient.GetGrain<IPlayer>(userId);
                if ((DateTime.Now - playerInfo.lastSentHeartBeat).TotalSeconds > 5)
                    player.HeartBeat().Ignore();
                playerInfo.lastSentHeartBeat = DateTime.Now;
            }

            _Logger.LogTrace((int) msg.msgType, "Received request with size: {0}", msg.RawData.Length);
            switch (msg.msgType)
            {
#if !MASTER
                //Developer dashboard requests
                case ApadanaMsgType.CreateEnvironmentRequest:
                    while (isMakingEnvironmentReady)
                    {
                        await Task.Delay(100);
                    }

                    isMakingEnvironmentReady = true;
                    await ProcessCreateEnvironmentMessage(session, player, connectionId, channelId, msg);
                    isMakingEnvironmentReady = false;
                    break;
#endif
                //Normal messages which are always available
                case ApadanaMsgType.SetPublisherDataRequest_S:
                    await ProcessSetPublisherDataByDeveloperMessage(session, player, connectionId, channelId, msg);
                    break;
                case ApadanaMsgType.GetInventoryRequest:
                    await ProcessGetInventoryRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetInventoryUnlockTimeResquest:
                    await ProcessGetInventoryUnlockTimeRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.UnlockInventoryRequest:
                    await ProcessUnlockInventoryRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.AddItemToInventoryRequest:
                    await ProcessAddInventoryRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetItemToInventoryRequest:
                    await ProcessSetInventoryRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.RemoveInventoryItemRequest:
                    await ProcessRemoveInventoryRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPublisherDataRequest_S:
                    await ProcessGetPublisherDataByDeveloperMessage(session, player, connectionId, channelId, msg);
                    break;

                //user requests
                case ApadanaMsgType.InitializeSessionRequest:
                    await ProcessInitializeSessionMessage(session, player, connectionId, channelId, msg);
                    break;
                case ApadanaMsgType.FindMatchRequest:
                    await ProcessFindMatchRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetMatchPropertyRequest:
                    await ProcessSetMatchPropertyRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.RejoinMatchRequest:
                    await ProcessRejoinMatchRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPlayerPendingMatchesRequest:
                    await ProcessGetPlayerPendingMatchesRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPlayerChallengesRequest:
                    await ProcessGetPlayerChallengesRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetNameRequest:
                    await ProcessSetNameRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.HeartBeatRequest:
                    await ProcessHeartBeatRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.InviteFriendRequest:
                    await ProcessInviteFriendsRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetFriendsRequest:
                    await ProcessGetFriendsRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.AcceptFriendRequest:
                    await ProcessAcceptFriendsRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.DenyFriendRequest:
                    await ProcessDenyFriendsRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetNameRequest:
                    await ProcessGetNameRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPlayerNameRequest:
                    await ProcessGetPlayerNameRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetServerTimeRequest:
                    await ProcessGetServerTimeRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.VerifyStoreReceiptRequest:
                    await ProcessVerifyCafeReceiptRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetCloudDataRequest:
                    await ProcessSetCloudDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetPublicCloudDataRequest:
                    await ProcessSetPublicCloudDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetCloudDataRequest:
                    await ProcessGetCloudDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPublicCloudDataRequest:
                    await ProcessGetPublicCloudDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPlayerPublicCloudDataRequest:
                    await ProcessGetPlayerPublicCloudDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetPublisherDataRequest:
                    await ProcessGetPublisherDataRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.GetLeaderboardRequest:
                    await ProcessGetLeaderBoardRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.SetLeaderboardRequest:
                    await ProcessSetLeaderBoardRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                case ApadanaMsgType.CreateNewAccountRequest:
                    await ProcessCreateNewAccountRequest(session, player, connectionId, channelId, msg);
                    break;
                case ApadanaMsgType.EmailActivationRequest:
                    await ProcessEmailActivationRequest(session, player, connectionId, channelId, msg);
                    break;
                case ApadanaMsgType.AddAuthMechanismToAccountRequest:
                    await ProcessAddAuthMechanismToAccountRequest(session, player, connectionId, channelId, msg,
                        ticket);
                    break;
                case ApadanaMsgType.AuthenticateRequest:
                    await ProcessAuthenticationRequest(session, player, connectionId, channelId, msg);
                    break;
                case ApadanaMsgType.LeaveGameRequest:
                    await ProcessLeaveGameMessage(session, player, connectionId, channelId, msg, ticket, isReliable, 0);
                    break;
                case ApadanaMsgType.StartChallengeRequest:
                    await ProcessStartChallengeMessage(session, player, connectionId, channelId, msg, ticket,
                        isReliable, 0);
                    break;
                case ApadanaMsgType.AcceptChallengeRequest:
                    await ProcessAcceptChallengeMessage(session, player, connectionId, channelId, msg, ticket,
                        isReliable, 0);
                    break;
                case ApadanaMsgType.DenyChallengeRequest:
                    await ProcessDenyChallengeMessage(session, player, connectionId, channelId, msg, ticket, isReliable,
                        0);
                    break;
                case ApadanaMsgType.ExecuteScriptEventRequest:
                    await ProcessExecuteScriptEventRequest(session, player, connectionId, channelId, msg, ticket);
                    break;
                default: //user messages
                    await ProcessUserMessage(session, player, channelId, msg, message, ticket, isReliable, 0);
                    break;
            }
        }

        #region Proccessors

        private async Task ProcessSetPublisherDataByDeveloperMessage(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<SetPublisherDataRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var developerStorage = orleansClient.GetGrain<IDeveloperStorage>(request.developerKey);
                await developerStorage.SetString(request.key, request.value, request.developerSecret);
                response.Succeed(new EmptyMessage());
            }
            catch (InvalidOperationException x)
            {
                _Logger.LogError((int) msg.msgType, "SetPublisherData InvalidSecret Error:", x);
                response.Fail($"Secret is wrong", 1);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "SetPublisherData Error:", x);
                response.Fail(x.Message, -1);
            }

            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetPublisherDataResponse_S,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetInventoryRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var name = msg.GetData<StringMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        name.value));
                (string, int)[] stuff = await GetInventoryItems(inv);
                GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                {
                    items = stuff
                };
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetInventory Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetInventoryResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetInventoryUnlockTimeRequest(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var name = msg.GetData<StringMessage>();
            OptionalResponse<GetInventoryUnlockTimeResponseMessage> response =
                new OptionalResponse<GetInventoryUnlockTimeResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        name.value));
                var (status, time) = await inv.GetUnlocktime();
                response.Succeed(new GetInventoryUnlockTimeResponseMessage
                {
                    time = time
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetInventoryUnlockTime Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetInventoryUnlockTimeResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessUnlockInventoryRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var name = msg.GetData<StringMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        name.value));
                (string, int)[] stuff = await UnlockInventoryItems(inv);
                GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                {
                    items = stuff
                };
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"UnlockInventory Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.UnlockInventoryResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessAddInventoryRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var req = msg.GetData<BuyItemInventoryRequestMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        req.inventoryName));
                var couldAdd = await inv.AddItem(
                    req.items.Select(x =>
                        orleansClient.GetGrain<IInventoryItem>(ResourcesUtility.GetItemKey(playerInfo.GameKey.ToString(), x.Item1),
                            ResourcesUtility.GetGameItemsClassName())).ToArray(),
                    req.items.Select(x => x.Item2).ToArray());
                if (couldAdd)
                {
                    (string, int)[] stuff = await GetInventoryItems(inv);
                    GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                    {
                        items = stuff
                    };
                    response.Succeed(responseData);
                }
                else //could not add cause inventory was full
                {
                    response.Fail("inventory was full", 1);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"AddInventoryRequest Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.AddItemToInventoryResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessSetInventoryRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var req = msg.GetData<BuyItemInventoryRequestMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        req.inventoryName));
                await inv.SetItem(
                    req.items.Select(x =>
                        orleansClient.GetGrain<IInventoryItem>(
                            ResourcesUtility.GetItemKey(playerInfo.GameKey.ToString(), x.Item1),
                            ResourcesUtility.GetGameItemsClassName())).ToArray(),
                    req.items.Select(x => x.Item2).ToArray());
                (string, int)[] stuff = await GetInventoryItems(inv);
                GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                {
                    items = stuff
                };
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetInventoryRequest Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetItemToInventoryResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }


        private async Task ProcessRemoveInventoryRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var req = msg.GetData<RemoveItemInventoryRequestMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(playerInfo.GameKey.ToString(), player.GetPrimaryKeyString(),
                        req.inventoryName));
                await inv.RemoveItem(
                    req.items.Select(x =>
                        orleansClient.GetGrain<IInventoryItem>(
                            ResourcesUtility.GetItemKey(playerInfo.GameKey.ToString(), x.Item1))).ToArray(),
                    req.items.Select(x => x.Item2).ToArray());
                (string, int)[] stuff = await GetInventoryItems(inv);
                GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                {
                    items = stuff
                };
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"RemoveInventoryRequest Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.RemoveInventoryItemResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task<(string, int)[]> GetInventoryItems(IPlayerInventory inv)
        {
            var readItems = await inv.GetInventory();
            if (readItems != null && readItems.items != null)
            {
                var tasks = readItems.items.Select(async x => ((await x.Item.GetName()), x.Count)).ToArray();
                (string, int)[] stuff = await Task.WhenAll(tasks);
                return stuff;
            }
            else
            {
                return new (string, int)[0];
            }
        }

        private async Task<(string, int)[]> UnlockInventoryItems(IPlayerInventory inv)
        {
            var readItems = await inv.Unlock();
            var tasks = readItems.Select(async x => ((await x.Item.GetName()), x.Count)).ToArray();
            (string, int)[] stuff = await Task.WhenAll(tasks);
            return stuff;
        }


        private async Task ProcessGetPublisherDataByDeveloperMessage(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<GetPublisherDataRequest>();
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var developerStorage = orleansClient.GetGrain<IDeveloperStorage>(request.developerKey);
                var readValue = await developerStorage.GetString(request.key);
                response.Succeed(new StringMessage {value = readValue});
            }
            catch (InvalidOperationException x)
            {
                _Logger.LogError((int) msg.msgType, "GetPublisherData InvalidSecret Error:", x);
                response.Fail($"Secret is wrong", 1);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "GetPublisherData Error:", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPublisherDataResponse_S,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }


        private async Task ProcessCreateEnvironmentMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<CreateEnvironmentRequest>();
            OptionalResponse<CreateEnvironmentResponse> response = new OptionalResponse<CreateEnvironmentResponse>();
            try
            {
                //Developer creation
                var developer = await orleansClient.GetGrain<IDeveloperManager>("main").GetDeveloper(request.Email);
                if (developer == null)
                {
                    var developerKey = await orleansClient.GetGrain<IDeveloperManager>("main").AddDeveloper(
                        new DeveloperState
                        {
                            FirstName = request.FirstName,
                            LastName = request.LastName,
                            PasswordHash = request.PasswordHash,
                            Email = request.Email,
                            PhoneNumber = request.PhoneNumber,
                            Address = request.Address,
                            Description = request.Description
                        });
                    developer = orleansClient.GetGrain<IDeveloper>(developerKey);
                }

                //Game creation
                var games = await developer.GetGames();
                var game = games.FirstOrDefault(x => x.Name == "game1");
                Guid gameKey;
                if (game == null)
                {
                    gameKey = await developer.AddGame(new GameInfo {Name = "game1"});
                }
                else
                {
                    gameKey = game.Key;
                }

                //Leaderboard creation
                var leaderboards = await orleansClient
                    .GetGrain<IResourceManager>(ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()))
                    .GetResources(ResourcesUtility.GetLeaderBoardClassName());
                List<string> boardNames = new List<string>();
                foreach (var b in leaderboards)
                    boardNames.Add(await b.GetName());
                var leaderBoard =
                    orleansClient.GetGrain<ILeaderBoard>(
                        ResourcesUtility.GetLeaderBoardKey(gameKey.ToString(), "global"));
                if (!boardNames.Contains("global"))
                {
                    await leaderBoard.Initialize("global", DateTime.Now, DateTime.Now, true);
                    bool success = await orleansClient
                        .GetGrain<IResourceManager>(
                            ResourcesUtility.GetLeaderboardResourceManagerName(gameKey.ToString()))
                        .AddResource(leaderBoard);
                }

                //game mode creation
                var gameModes = await orleansClient.GetGrain<IGameModeManager>(gameKey).GetAllGameModes();
                var mode = gameModes.GameModes.FirstOrDefault(x => x.Name == "multi");
                if (mode == null)
                {
                    await orleansClient.GetGrain<IGameModeManager>(gameKey).AddOrUpdateGameMode(new GameMode
                    {
                        Name = "multi",
                        MinPlayers = 4,
                        MaxPlayers = 4,
                        Criterias = new List<GameMode.MatchMakingCriteria>
                        {
                            new GameMode.MatchMakingCriteria
                                {AcceptableThreshold = 1000, StartTimeInSeconds = 0, IsMinPlayersEnough = false}
                        }
                    });
                }

                //script creation
                var scripts = await orleansClient
                    .GetGrain<IResourceManager>(ResourcesUtility.GetEventScriptResourceManagerName(gameKey.ToString()))
                    .GetResources(ResourcesUtility.GetEventScriptClassName());
                List<string> scriptNames = new List<string>();
                foreach (var s in scripts)
                    scriptNames.Add(await s.GetName());
                var script =
                    orleansClient.GetGrain<IScriptEventCompiler>(
                        ResourcesUtility.GetEventScriptKey(gameKey.ToString(), "Test"));
                if (!scriptNames.Contains("Test"))
                {
                    await script.Initialize("Test", new List<EventParameter>
                    {
                        new EventParameter {Type = EventParameter.ParamType.IntParam, Name = ""},
                        new EventParameter {Type = EventParameter.ParamType.IntParam, Name = ""}
                    }, "public async Task<int> Test(int a,int b){return a+b;}");
                    bool success = await orleansClient
                        .GetGrain<IResourceManager>(
                            ResourcesUtility.GetEventScriptResourceManagerName(gameKey.ToString())).AddResource(script);
                }

                response.Succeed(new CreateEnvironmentResponse
                    {GameKey = gameKey, DeveloperKey = developer.GetPrimaryKey()});
            }
            catch (ArgumentException x)
            {
                _Logger.LogError((int) msg.msgType, "CreateDeveloper invalid/existing email Error:", x);
                response.Fail("either email already registered or is not valid email address", 1);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "CreateDeveloper Error:", x);
                response.Fail(x.Message, 1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.CreateEnvironmentResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessInitializeSessionMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<InitializeSessionRequest>();
            OptionalResponse<GuidMessage> response = new OptionalResponse<GuidMessage>();
            try
            {
                var result = await orleansClient.GetGrain<IDeveloper>(request.DeveloperKey)
                    .InitializeSession(request.GameKey);
                //do not have authentication data so we should call accounting ourselves
                orleansClient.GetGrain<ISession>(result).AddUserCall().Ignore();
                response.Succeed(new GuidMessage {value = result});
            }
            catch (InvalidOperationException x)
            {
                _Logger.LogError((int) msg.msgType, $"InitializeSession {request.GameKey} not valid Error:", x);
                response.Fail($"gamekey {request.GameKey} is not valid.", 1);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "InitializeSession Error:", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.InitializeSessionResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        //Only game messages are supported for now
        private async Task ProcessUserMessage(SessionGameInfo session, IPlayer player, int channelId,
            ApadanaMessageBase msg, byte[] data, Guid ticket, bool reliable, int count)
        {
            if (msg.HasGameTarget)
            {
                try
                {
                    var game = orleansClient.GetGrain<IRelayServerGame>(Guid.Parse(msg.game),
                        session.GameKey.ToString(), null);
                    await game.SendToAllBut(player, channelId, data);
                }
                catch (GameNotRunningException x)
                {
                    _Logger.LogError((int) msg.msgType, "UserMessage NotRunningGame Error:", x);
                    return;
                }
                catch (Exception x)
                {
                    _Logger.LogError((int) msg.msgType, "UserMessage Error:", x);
                    if (count < MaxTrycount)
                    {
                        _Logger.LogTrace((int) msg.msgType, "Retrying user message {0}", count);
                        await Task.Delay(MaxTrycount * 10);
                        await ProcessUserMessage(session, player, channelId, msg, data, ticket, reliable, count + 1);
                        return;
                    }
                    else
                    {
                        _Logger.LogError((int) msg.msgType, $"User message failed after max retries {MaxTrycount}");
                        return;
                    }
                }
            }
        }

        private async Task ProcessLeaveGameMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket, bool reliable, int count)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            if (msg.HasGameTarget)
            {
                OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
                try
                {
                    var game = orleansClient.GetGrain<IRelayServerGame>(Guid.Parse(msg.game),
                        session.GameKey.ToString(), null);
                    await game.PlayerFinished(player);
                    response.Succeed(new EmptyMessage());
                }
                catch (GameNotRunningException x)
                {
                    _Logger.LogError((int) msg.msgType, "LeaveGame NotRunningGame Error:", x);
                    response.Fail("Game not running", 1);
                }
                catch (Exception x)
                {
                    _Logger.LogError((int) msg.msgType, "LeaveGame Error:", x);
                    if (count < MaxTrycount)
                    {
                        _Logger.LogTrace((int) msg.msgType, "Retrying LeaveGame message {0}", count);
                        await Task.Delay(MaxTrycount * 10);
                        await ProcessLeaveGameMessage(session, player, connectionId, channelId, msg, ticket, reliable,
                            count + 1);
                        return;
                    }
                    else
                    {
                        _Logger.LogError((int) msg.msgType, $"LeaveGame Failed after {MaxTrycount}");
                        response.Fail(x.Message, -1);
                    }
                }

                ApadanaMessageBase returning = ApadanaMessageBase.CreateWithGameTarget(response,
                    ApadanaMsgType.LeaveGameResponse, ApadanaMsgHeader.normal, msg.game, "");
                returning.SetRequestID(msg.requestID);
                await playerInfo.connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
                {
                    player = player, connectionId = connectionId, channelId = channelId, data = returning.ToByteArray(),
                    eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
                });
            }
        }

        private async Task ProcessStartChallengeMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetData<ChallengeMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var challenge =
                    orleansClient.GetGrain<IPlayerChallengeHandler>(session.GameKey, player.GetPrimaryKeyString(),
                        null);
                var result = await challenge.SendChallengeRequest(orleansClient.GetGrain<IPlayer>(challengeData.Player),
                    challengeData.data, challengeData.TimeOut, true);
                if (result == ChallengeAcceptionStatus.Received)
                    response.Succeed(new EmptyMessage());
                else if (result == ChallengeAcceptionStatus.Disconnected)
                    response.Fail(result.ToString(), 1);
                else if (result == ChallengeAcceptionStatus.RequestedYouAsWell)
                    response.Fail(result.ToString(), 2);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "StartChallenge Error:", x);
                if (count < MaxTrycount)
                {
                    _Logger.LogTrace((int) msg.msgType, "Retrying StartChallenge message {0}", count);
                    await Task.Delay(MaxTrycount * 10);
                    await ProcessStartChallengeMessage(session, player, connectionId, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"StartChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.StartChallengeResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessAcceptChallengeMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetData<ChallengeMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var challenge =
                    orleansClient.GetGrain<IPlayerChallengeHandler>(session.GameKey, challengeData.Player, null);
                await challenge.AcceptChallenge(player, challengeData.data);
                response.Succeed(new EmptyMessage());
            }
            catch (ChallengeTimeoutException x)
            {
                _Logger.LogError((int) msg.msgType, "AcceptChallenge AcceptAfterTimeout Error:", x);
                response.Fail(x.Message, 1);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "AcceptChallenge Error:", x);
                if (count < MaxTrycount)
                {
                    _Logger.LogTrace((int) msg.msgType, "Retrying AcceptChallenge message {0}", count);
                    await Task.Delay(MaxTrycount * 10);
                    await ProcessAcceptChallengeMessage(session, player, connectionId, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"AcceptChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.AcceptChallengeResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessDenyChallengeMessage(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetData<ChallengeMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var challenge =
                    orleansClient.GetGrain<IPlayerChallengeHandler>(session.GameKey, challengeData.Player, null);
                await challenge.DenyChallenge(player, challengeData.data);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, "DenyChallenge Error:", x);
                if (count < MaxTrycount)
                {
                    _Logger.LogTrace((int) msg.msgType, "Retrying DenyChallenge message {0}", count);
                    await Task.Delay(MaxTrycount * 10);
                    await ProcessDenyChallengeMessage(session, player, connectionId, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"DenyChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.DenyChallengeResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessCreateNewAccountRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<CreateAccountRequest>();
            Guid sessionKey = Guid.Parse(request.ticket);
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                AccountCreationResult accountRes = new AccountCreationResult {result = AdditionResult.Failure};
                if (request.LoginType == LoginType.Device)
                {
                    accountRes = await orleansClient.GetGrain<IPlayerCreator>(0).CreateAccountWithDevice(request.Id);
                }
                else if (request.LoginType == LoginType.UserWithPassowrd)
                {
                    accountRes = await orleansClient.GetGrain<IPlayerCreator>(0)
                        .CreateAccountWithUserAndPassword(request.Id, request.PasswordHash);
                }
                else if (request.LoginType == LoginType.Email)
                {
                    accountRes = await orleansClient.GetGrain<IPlayerCreator>(0)
                        .CreateAccountWithEmail(request.Id, request.PasswordHash);
                }
                else
                {
                    //_Logger.LogError("");
                }

                if (accountRes.result == AdditionResult.Success)
                {
                    response.Succeed(new StringMessage {value = accountRes.Account});
                }
                else
                {
                    sbyte code = (accountRes.result == AdditionResult.AlreadyExisting) ? (sbyte) 2 : (sbyte) -1;
                    string message = code == 2 ? "Already exists" : "Failed";
                    response.Fail(message, code);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"Login Error: {x}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.CreateNewAccountResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessEmailActivationRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg)
        {
            var request = msg.GetData<EmailActivationRequest>();
            Guid sessionKey = Guid.Parse(request.ticket);
            await orleansClient.GetGrain<ISession>(sessionKey).AddUserCall(1);
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var playerId = await orleansClient.GetGrain<IEmailToAccountIdMap>(request.email).GetAccountID();
                if (!string.IsNullOrEmpty(playerId))
                {
                    await orleansClient.GetGrain<IPlayer>(playerId).Activate(request.code);
                    response.Succeed(new EmptyMessage());
                }
                else
                {
                    response.Fail("No such email exist to activate", 1);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"Login Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.EmailActivationResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessSetMatchPropertyRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<SetMatchPropertyData>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var game = orleansClient.GetGrain<IRelayServerGame>(request.Game, session.GameKey.ToString(), null);
                if (request.isPlayerProperty)
                    await game.SetPlayerProperty(player, request.key, request.value);
                else
                    await game.SetGameProperty(request.key, request.value);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetMatchProperty Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetMatchPropertyResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessRejoinMatchRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<GuidMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                var game = orleansClient.GetGrain<IRelayServerGame>(request.value, session.GameKey.ToString(), null);

                await player.RejoinMatch(game);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"RejoinMatch Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.RejoinMatchResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPlayerPendingMatchesRequest(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<EmptyMessage>();
            OptionalResponse<PendingMatchesCollectionData> response = new OptionalResponse<PendingMatchesCollectionData>();
            try
            {
                var matches = await player.GetPendingMatches();
                PendingMatchesCollectionData data = new PendingMatchesCollectionData();
                data.matches = new PendingMatchesCollectionData.PendingMatchInfo[matches.Count];
                for (int i = 0; i < matches.Count; ++i)
                    data.matches[i] = new PendingMatchesCollectionData.PendingMatchInfo
                    {
                        startTime = matches[i].StartTime,
                        matchId = matches[i].MatchId,
                        players = matches[i].players.Select(x => x.GetPrimaryKeyString()).ToArray()
                    };
                response.Succeed(data);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetPendingMatches Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPlayerMatchesResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPlayerChallengesRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<EmptyMessage>();
            OptionalResponse<ChallengeRequestsMessage> response = new OptionalResponse<ChallengeRequestsMessage>();
            try
            {
                (List<ChallengeRequestState> sent, List<ChallengeRequestState> received) matches =
                    await orleansClient.GetGrain<IPlayerChallengeHandler>(session.GameKey, player.GetPrimaryKeyString())
                        .GetChallengeRequests();
                ChallengeRequestsMessage data = new ChallengeRequestsMessage();

                for (int i = 0; i < matches.sent.Count; ++i)
                    data.sent.Add(new ChallengeRequestsMessage.Request
                    {
                        Player = matches.sent[i].Player.GetPrimaryKeyString(),
                        data = matches.sent[i].data,
                        gameId = matches.sent[i].Game,
                        status = (ChallengeRequestsMessage.Request.Status) matches.sent[i].State,
                        PlayerName = await matches.sent[i].Player.GetPlayerName()
                    });
                for (int i = 0; i < matches.received.Count; ++i)
                    data.sent.Add(new ChallengeRequestsMessage.Request
                    {
                        Player = matches.received[i].Player.GetPrimaryKeyString(),
                        data = matches.received[i].data,
                        gameId = matches.received[i].Game,
                        status = (ChallengeRequestsMessage.Request.Status) matches.received[i].State,
                        PlayerName = await matches.received[i].Player.GetPlayerName()
                    });
                response.Succeed(data);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetChalenges Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPlayerChallengesResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessFindMatchRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var request = msg.GetData<FindMatchRequest>();

            var mm = orleansClient.GetGrain<ICustomMatchMaker>(session.GameKey, request.matchType, null);
            string gk = "";
            OptionalResponse<FindMatchResponse> response = new OptionalResponse<FindMatchResponse>();
            try
            {
                var foundMatchResponse = await mm.FindMatch(player, request.skill, request.waitTime);
                if (foundMatchResponse == null)
                {
                    response.Fail("Match not found", 1);
                }
                else
                {
                    FindMatchResponse responseToSend = new FindMatchResponse();
                    responseToSend.gameId = foundMatchResponse.game.GetPrimaryKey(out gk);
                    responseToSend.players = (await foundMatchResponse.game.GetPlayers())
                        .Select(x => x.GetPrimaryKeyString())
                        .ToArray();
                    responseToSend.data = "";
                    response.Succeed(responseToSend);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"FindMatch Error: ", x);
                response.Fail(x.Message, -1);
            }

            var returningMsg =
                ApadanaMessageBase.CreateWithRequestID(response, ApadanaMsgType.FindMatchResponse, msg.header,
                    msg.requestID);
            if (response.isSuccesful)
            {
                //TODO remove this and make it none automatic
                await orleansClient.GetGrain<IRelayServerGame>(response.msg.gameId, gk, null)
                    .PlayerStarted(player);
            }

            await playerInfo.connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
            {
                player = player, connectionId = connectionId, channelId = channelId, data = returningMsg.ToByteArray(),
                eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
            });
        }

        private async Task ProcessSetNameRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await player.SetPlayerName(request.value);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetName Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetNameResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessHeartBeatRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<EmptyMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await player.HeartBeat();
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetName Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.HeartBeatResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetFriendsRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<GetFriendsRequestMessage>();
            OptionalResponse<FriendlistCollection> response = new OptionalResponse<FriendlistCollection>();
            try
            {
                var playerFriends = await orleansClient
                    .GetGrain<IPlayerFriendList>(session.GameKey, player.GetPrimaryKeyString(), null)
                    .GetFriends(request.publicKeys);
                var col = new FriendlistCollection();
                for (int i = 0; i < playerFriends.Friends.Count; ++i)
                {
                    col.friends.Add(new FriendlistEntry
                    {
                        userId = playerFriends.Friends[i].Player.GetPrimaryKeyString(),
                        name = playerFriends.Friends[i].Name,
                        status = (RealtimeServer.OnlineStatus) playerFriends.Friends[i].OnlineStatus,
                        data =
                            (playerFriends.publicData.ContainsKey(playerFriends.Friends[i].Player.GetPrimaryKeyString())
                            )
                                ? playerFriends.publicData[playerFriends.Friends[i].Player.GetPrimaryKeyString()]
                                : new Dictionary<string, string>()
                    });
                }

                for (int i = 0; i < playerFriends.ReceivedInvitations.Count; ++i)
                {
                    col.receivedInvitations.invitations.Add(new FriendInvitation
                    {
                        creationTime = playerFriends.ReceivedInvitations[i].CreationDate,
                        data = playerFriends.ReceivedInvitations[i].data,
                        OtherPlayerId = playerFriends.ReceivedInvitations[i].Sender.GetPrimaryKeyString(),
                        otherPlayerName = playerFriends.ReceivedInvitations[i].SenderName
                    });
                }

                for (int i = 0; i < playerFriends.SentInvitations.Count; ++i)
                {
                    col.sentInvitation.invitations.Add(new FriendInvitation
                    {
                        creationTime = playerFriends.SentInvitations[i].CreationDate,
                        data = playerFriends.SentInvitations[i].data,
                        OtherPlayerId = playerFriends.SentInvitations[i].Receiver.GetPrimaryKeyString(),
                        otherPlayerName = playerFriends.SentInvitations[i].ReceiverName
                    });
                }

                response.Succeed(col);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetFriends Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetFriendsResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessInviteFriendsRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            var request = msg.GetData<InviteFriendRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                IPlayer foundPlayer = null;
                switch (request.type)
                {
                    case InvitationType.AccountId:
                        foundPlayer = orleansClient.GetGrain<IPlayer>(request.value);
                        break;
                    case InvitationType.playerName:
                        string nameBasedId = await orleansClient.GetGrain<INameToAccountIdMap>(request.value).GetId();
                        if (!string.IsNullOrEmpty(nameBasedId))
                        {
                            foundPlayer = orleansClient.GetGrain<IPlayer>(nameBasedId);
                        }

                        break;
                    case InvitationType.Email:
                        string emailBasedId =
                            await orleansClient.GetGrain<IEmailToAccountIdMap>(request.value).GetAccountID();
                        if (!string.IsNullOrEmpty(emailBasedId))
                        {
                            foundPlayer = orleansClient.GetGrain<IPlayer>(emailBasedId);
                        }

                        break;
                    case InvitationType.UserId:
                        string userBasedId = await orleansClient.GetGrain<IUserIdToAccountIdMap>(request.value)
                            .GetAccountID();
                        if (!string.IsNullOrEmpty(userBasedId))
                        {
                            foundPlayer = orleansClient.GetGrain<IPlayer>(userBasedId);
                        }

                        break;
                    case InvitationType.PlayfabId:
                        foundPlayer =
                            orleansClient.GetGrain<IPlayer>(
                                ResourcesUtility.GetPlayfabPlayerKey(playerInfo.titleId, request.value));
                        break;
                }

                if (foundPlayer != null)
                {
                    await orleansClient.GetGrain<IPlayerFriendList>(session.GameKey, player.GetPrimaryKeyString(), null)
                        .Invite(foundPlayer, await player.GetPlayerName(), request.data);

                    response.Succeed(new EmptyMessage());
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"Invite friend Error: player not found");
                    response.Fail("player not found", 1);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"Invite friend Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.InviteFriendResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessAcceptFriendsRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringAndByteArrayMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await orleansClient.GetGrain<IPlayerFriendList>(session.GameKey, player.GetPrimaryKeyString(), null)
                    .AcceptInvitation(orleansClient.GetGrain<IPlayer>(request.stringValue), request.byteArrayValue);

                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"Accept friendship Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.AcceptFriendResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessDenyFriendsRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringAndByteArrayMessage>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await orleansClient.GetGrain<IPlayerFriendList>(session.GameKey, player.GetPrimaryKeyString(), null)
                    .DenyInvitation(orleansClient.GetGrain<IPlayer>(request.stringValue), request.byteArrayValue);

                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"Deny friendship Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.DenyFriendResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessAddAuthMechanismToAccountRequest(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<AddAuthenticationToAccountRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                AdditionResult addition = AdditionResult.Failure;
                switch (request.LoginType)
                {
                    case LoginType.Device:
                        addition = await player.AddPlayerDevice(request.Id);
                        break;
                    case LoginType.Email:
                        addition = await player.AddPlayerEmail(request.Id, request.PasswordHash);
                        break;
                    case LoginType.UserWithPassowrd:
                        addition = await player.AddPlayerUserAndPassword(request.Id, request.PasswordHash);
                        break;
                }

                if (addition == AdditionResult.Success)
                    response.Succeed(new EmptyMessage());
                else if (addition == AdditionResult.AlreadyExisting)
                    response.Fail("alreayd exists", 1);
                else
                    throw new Exception("Returned something other than exists or success");
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetName Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.AddAuthMechanismToAccountResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetCloudDataRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringMessage>();
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var readValue = (await player.GetPlayerString(request.value)).Item2;
                response.Succeed(new StringMessage
                {
                    value = readValue
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetCloudDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPublicCloudDataRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringMessage>();
            OptionalResponse<GetPublicCloudDataResponse> response = new OptionalResponse<GetPublicCloudDataResponse>();
            try
            {
                var readValue = (await player.GetPlayerPublicString(request.value));
                GetPublicCloudDataResponse responseData = new GetPublicCloudDataResponse();
                responseData.hasValue = readValue.Item1;
                if (readValue.Item1)
                    responseData.value = readValue.Item2;
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetPublicCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPublicCloudDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPlayerPublicCloudDataRequest(SessionGameInfo session, IPlayer player,
            int connectionId, int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<GetPlayerPublicCloudDataRequest>();
            OptionalResponse<GetPlayerPublicCloudDataResponse> response =
                new OptionalResponse<GetPlayerPublicCloudDataResponse>();
            try
            {
                var readValue =
                    (await orleansClient.GetGrain<IPlayer>(request.player).GetPlayerPublicString(request.key));
                GetPlayerPublicCloudDataResponse responseData = new GetPlayerPublicCloudDataResponse();
                responseData.hasValue = readValue.Item1;
                if (responseData.hasValue) responseData.value = readValue.Item2;
                response.Succeed(responseData);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetPlayerPublicCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPlayerPublicCloudDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPublisherDataRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringMessage>();
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var readValue = await orleansClient.GetGrain<IDeveloperStorage>(session.DeveloperKey)
                    .GetString(request.value);
                response.Succeed(new StringMessage
                {
                    value = readValue
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPublisherDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessSetLeaderBoardRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<SetLeaderBoardRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await orleansClient
                    .GetGrain<ILeaderBoard>(
                        ResourcesUtility.GetLeaderBoardKey(session.GameKey.ToString(), request.leaderBoardName))
                    .AddPlayerScore(player, request.score);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetLeaderBoardRequest Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetLeaderboardResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetLeaderBoardRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<GetLeaderBoardRequest>();
            OptionalResponse<GetLeaderBoardResponse> response = new OptionalResponse<GetLeaderBoardResponse>();
            try
            {
                GetLeaderBoardResponse responseToSend = new GetLeaderBoardResponse();
                var leaderBoard = orleansClient.GetGrain<ILeaderBoard>(
                    ResourcesUtility.GetLeaderBoardKey(session.GameKey.ToString(), request.leaderBoardName));
                switch (request.type)
                {
                    case GetLeaderBoardRequest.RequestType.Me:
                        int rank = await leaderBoard.GetPlayerRank(player);
                        responseToSend.playerRank = rank;
                        break;
                    case GetLeaderBoardRequest.RequestType.Around:
                        var aroundEntries = await leaderBoard.GetAroundMe(player, request.count);
                        responseToSend.playerRank = aroundEntries.Item1;
                        FillReturningLeaderBoardEntries(responseToSend, aroundEntries.Item2);
                        break;
                    case GetLeaderBoardRequest.RequestType.Top:
                        var topEntries = await leaderBoard.GetTopEntries(request.count, request.offset);
                        FillReturningLeaderBoardEntries(responseToSend, topEntries.Item2);
                        responseToSend.count = topEntries.Item1;
                        break;
                }

                response.Succeed(responseToSend);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetLeaderBoardRequest Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetLeaderboardResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private static void FillReturningLeaderBoardEntries(GetLeaderBoardResponse responseToSend,
            List<Interfaces.LeaderBoardEntry> aroundEntries)
        {
            foreach (var item in aroundEntries)
            {
                responseToSend.entries.Add(new RealtimeServer.LeaderBoardEntry
                {
                    score = item.Score,
                    userName = item.Name,
                    playerId = item.PlayerID,
                    entryTime = item.EntryTime
                });
            }
        }

        private async Task ProcessSetCloudDataRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<SetCloudDataRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await player.SetPlayerString(request.key, request.value);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetCloudDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessSetPublicCloudDataRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<SetCloudDataRequest>();
            OptionalResponse<EmptyMessage> response = new OptionalResponse<EmptyMessage>();
            try
            {
                await player.SetPlayerPublicString(request.key, request.value);
                response.Succeed(new EmptyMessage());
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetPublicCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.SetPublicCloudDataResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessExecuteScriptEventRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<ScriptEventExecutionRequestMessage>();
            OptionalResponse<ScriptEventExecutionResponseMessage> response =
                new OptionalResponse<ScriptEventExecutionResponseMessage>();
            try
            {
                ISession sessionGrain = orleansClient.GetGrain<ISession>(ticket);

                object returnValue =
                    await orleansClient.GetGrain<IScriptEventRunner>(session.GameKey, session.UserID, null)
                        .Run(sessionGrain, request.eventName, request.parameters);
                ScriptEventExecutionResponseMessage responseMessage = new ScriptEventExecutionResponseMessage
                    {hasReturnValue = (returnValue != null)};
                if (responseMessage.hasReturnValue)
                {
                    responseMessage.returnValue = returnValue;
                    responseMessage.returnType =
                        (RealtimeServer.ParamType) GetReturnType(returnValue);
                }

                response.Succeed(responseMessage);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.ExecuteScriptEventResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetServerTimeRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var time = await orleansClient.GetGrain<ITimeService>(Guid.Empty).GetServerTime();
                response.Succeed(new StringMessage
                {
                    value = time
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetServerTimeRequest Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetServerTimeResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetNameRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var name = await player.GetPlayerName();
                response.Succeed(new StringMessage
                {
                    value = name
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetNameRequest Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetNameResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessGetPlayerNameRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<StringMessage>();
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var name = await orleansClient.GetGrain<IPlayer>(request.value).GetPlayerName();
                response.Succeed(new StringMessage
                {
                    value = name
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"GetPlayerNameRequest Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.GetPlayerNameResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ProcessVerifyCafeReceiptRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg, Guid ticket)
        {
            var request = msg.GetData<VerifyCafeReceiptRequest>();
            OptionalResponse<VerifyCafeReceiptResponse> response = new OptionalResponse<VerifyCafeReceiptResponse>();
            try
            {
                var result = await orleansClient
                    .GetGrain<IIAPVerification>(session.GameKey, player.GetPrimaryKeyString(), null)
                    .VerifyCafeBazaar(request.Product, request.Receipt);
                response.Succeed(new VerifyCafeReceiptResponse
                {
                    Status = ((RealtimeServer.VerificationStatus) result.Item1),
                    Data = result.Item2
                });
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"VerifyCafeReceipt Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.VerifyStoreReceiptResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        #endregion


        private void AssertSessionIfRequired(ApadanaMessageBase msg)
        {
            if (MessageTypeRequiresSession(msg))
            {
                if (!msg.hasAuthData)
                    throw new SessionRequiredException();
            }
        }

        public async Task ClientDisconnected(int connectionId)
        {
            _Logger.LogInformation($"connection {connectionId} disconnected");
            await ChangeConnectionStateToLoggedOut(connectionId);
        }
        
        
        
        private static bool MessageTypeRequiresSession(ApadanaMessageBase msg)
        {
            return msg.msgType == ApadanaMsgType.FindMatchRequest
                   || msg.msgType == ApadanaMsgType.SetNameRequest
                   || msg.msgType == ApadanaMsgType.GetNameRequest
                   || msg.msgType > ApadanaMsgType.HighestInternal;
        }

        [Serializable]
        public class SessionRequiredException : ArgumentException
        {
            public SessionRequiredException()
            {
            }

            public SessionRequiredException(System.Runtime.Serialization.SerializationInfo info,
                System.Runtime.Serialization.StreamingContext ctx)
            {
            }
        }

        private async Task ProcessAuthenticationRequest(SessionGameInfo session, IPlayer player, int connectionId,
            int channelId, ApadanaMessageBase msg)
        {
            //try to get the account to login
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            bool tryLoggingIn = false;
            bool isNewlyCreated = false;
            var request = msg.GetData<AuthenticationRequest>();
            Guid sessionKey = Guid.Parse(request.ticket);
            await orleansClient.GetGrain<ISession>(sessionKey).AddUserCall(1);
            string account = null;
            OptionalResponse<AuthenticationResponse> response = new OptionalResponse<AuthenticationResponse>();
            try
            {
                switch (request.LoginType)
                {
                    case LoginType.Device:
                        account = await orleansClient
                            .GetGrain<ICustomIDToAccountIDMap>(UserMappingUtility.GetDeviceIdMappingId(request.Id))
                            .GetAccountID();
                        break;
                    case LoginType.UserWithPassowrd:
                        account = await orleansClient.GetGrain<IUserIdToAccountIdMap>(request.Id).GetAccountID();
                        break;
                    case LoginType.Email:
                        account = await orleansClient.GetGrain<IEmailToAccountIdMap>(request.Id).GetAccountID();
                        break;
                    case LoginType.Custom:
                        account = ResourcesUtility.GetPlayfabPlayerKey(request.data["titleId"],
                            request.data["playfabId"]);
                        playerInfo.titleId = request.data["titleId"];
                        break;
                }

                //If account didn't exist
                if (string.IsNullOrEmpty(account))
                {
                    //If this is device or user login and autocreate is true
                    AccountCreationResult accountRes = new AccountCreationResult {result = AdditionResult.Failure};
                    if (request.AutoCreate && request.LoginType == LoginType.Device)
                    {
                        accountRes = await orleansClient.GetGrain<IPlayerCreator>(0)
                            .CreateAccountWithDevice(request.Id);
                        isNewlyCreated = true;
                    }
                    else if (request.AutoCreate && request.LoginType == LoginType.UserWithPassowrd)
                    {
                        accountRes = await orleansClient.GetGrain<IPlayerCreator>(0)
                            .CreateAccountWithUserAndPassword(request.Id, request.PasswordHash);
                        isNewlyCreated = true;
                    }

                    //if created try to login
                    tryLoggingIn = accountRes.result == AdditionResult.Success;
                    if (tryLoggingIn)
                        account = accountRes.Account;
                }
                else //if the account exists then login
                {
                    tryLoggingIn = true;
                }

                playerInfo.loginTimeRandomKey = Guid.NewGuid();
                //if we can login because account exists or we created it
                if (tryLoggingIn)
                {
                    var p = orleansClient.GetGrain<IPlayer>(account);
                    LoginResult loginResponse;
                    switch (request.LoginType)
                    {
                        case RealtimeServer.LoginType.Device:
                            loginResponse = await p.LoginWithDeviceID(request.Id, sessionKey, playerInfo.loginTimeRandomKey);
                            break;
                        case LoginType.UserWithPassowrd:
                            loginResponse = await p.LoginWithUserNameAndPassword(request.Id, request.PasswordHash,
                                sessionKey, playerInfo.loginTimeRandomKey);
                            break;
                        case LoginType.Email:
                            loginResponse = await p.LoginWithEmail(request.Id, request.PasswordHash, sessionKey, playerInfo.loginTimeRandomKey);
                            break;
                        case LoginType.Custom:
                            loginResponse = await p.LoginWithProvider(request.Id, request.data, sessionKey, playerInfo.loginTimeRandomKey);
                            break;
                        default:
                            throw new ArgumentException("Unsupported login type");
                    }

                    if (loginResponse == null)
                    {
                        response.Fail("didn't login", 1);
                    }
                    else
                    {
                        var gk = (await orleansClient.GetGrain<ISession>(sessionKey).GetDeveloperKey()).GameKey;
                        await ChangeConnectionStateToLoggedIn(p, connectionId, gk, "UNET");
                        response.Succeed(new AuthenticationResponse
                        {
                            name = loginResponse.name, userId = loginResponse.userID, ticket = loginResponse.ticket,
                            isNewlyCreated = isNewlyCreated
                        });
                    }
                }
                else
                {
                    response.Fail("didn't login", 1);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"*Login Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = ApadanaMessageBase.CreateWithRequestID
            (response,
                ApadanaMsgType.AuthenticateResponse,
                msg.header,
                msg.requestID);
            await Send(connectionId, returning.ToByteArray());
        }

        private async Task ChangeConnectionStateToLoggedIn(IPlayer p, int connectionId, Guid gameKey, string host)
        {
            _Logger.LogInformation(
                $"Connection state changed to logged in connectionId: {connectionId} gameKey: {gameKey} host:{host}");
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            playerInfo.Player = p;
            playerConnectionMapper[p] = connectionId;
            playerInfo.IsLoggedIn = true;
            playerInfo.Host = host;
            playerInfo.ConnectionId = connectionId;
            playerInfo.GameKey = gameKey;
            playerInfo.connectionStream =
                streamProvider.GetStream<UNetConnectionDataTransferMessage>(
                    StreamNames.PlayerConnection(p.GetPrimaryKeyString()));
            await UnRegisterConnectionStream(playerInfo.connectionStream, playerInfo.ConnectionId);
            await RegisterConnectionStream(playerInfo.connectionStream);
            await SubscribeForStreams(playerInfo);
        }

        private async Task ChangeConnectionStateToLoggedOut(int connectionId)
        {
            _Logger.LogInformation($"connection logged out {connectionId}");
            var playerInfo = GetPlayerInfo(connectionId);
            if(playerInfo == null)
                return;
            if (playerInfo.challengesHandle != null)
                await playerInfo.challengesHandle.UnsubscribeAsync();
            if (playerInfo.friendlistHandle != null)
                await playerInfo.friendlistHandle.UnsubscribeAsync();
            
            playerInfo.Player = null;
            playerInfo.Host = "";
            playerInfo.IsLoggedIn = false;
            playerInfo.GameKey = Guid.Empty;
            playerInfo.ConnectionId = -1;
            await UnRegisterConnectionStream(playerInfo.connectionStream, connectionId);
            //await ClearStateAsync();##
        }

        private async Task SubscribeForStreams(PlayerInfo playerInfo)
        {
            playerInfo.ChallengesStream = streamProvider.GetStream<ChallengeEventData>(
                StreamNames.ChallengeEvents(playerInfo.GameKey, playerInfo.Player.GetPrimaryKeyString()));
            playerInfo.friendListStream = streamProvider.GetStream<FriendListEventData>(
                StreamNames.FriendEvents(playerInfo.GameKey, playerInfo.Player.GetPrimaryKeyString()));
            try
            {
                var handles = await playerInfo.ChallengesStream.GetAllSubscriptionHandles();
                if (handles.Count > 0)
                {
                    playerInfo.challengesHandle =
                        await handles[0].ResumeAsync<ChallengeEventData>(playerInfo.OnNextChallengeMessage);
                    if (handles.Count > 1)
                    {
                        for (int i = 1; i < handles.Count; ++i)
                        {
                            handles[i].UnsubscribeAsync().Ignore();
                        }
                    }
                }
                else
                {
                    playerInfo.challengesHandle =
                        await playerInfo.ChallengesStream.SubscribeAsync<ChallengeEventData>(playerInfo
                            .OnNextChallengeMessage);
                }
            }
            catch
            {
                try
                {
                    playerInfo.challengesHandle =
                        await playerInfo.ChallengesStream.SubscribeAsync<ChallengeEventData>(playerInfo
                            .OnNextChallengeMessage);
                }
                catch (Exception ex)
                {
                    _Logger.LogCritical(-1, ex, ex.Message);
                }
            }

            try
            {
                var handles2 = await playerInfo.friendListStream.GetAllSubscriptionHandles();
                if (handles2.Count > 0)
                {
                    playerInfo.friendlistHandle =
                        await handles2[0].ResumeAsync<FriendListEventData>(playerInfo.OnNextFriendMessage);
                    if (handles2.Count > 1)
                    {
                        for (int i = 1; i < handles2.Count; ++i)
                        {
                            handles2[i].UnsubscribeAsync().Ignore();
                        }
                    }
                }
                else
                {
                    playerInfo.friendlistHandle =
                        await playerInfo.friendListStream.SubscribeAsync<FriendListEventData>(playerInfo
                            .OnNextFriendMessage);
                }
            }
            catch
            {
                try
                {
                    playerInfo.friendlistHandle =
                        await playerInfo.friendListStream.SubscribeAsync<FriendListEventData>(playerInfo
                            .OnNextFriendMessage);
                }
                catch (Exception ex)
                {
                    _Logger.LogCritical(-1, ex, ex.Message);
                }
            }
        }

        //private async Task SendResponseToClient<T>(IPlayer player, int connectionId, int channelId,
        //    ApadanaMessageBase msg, T response, ApadanaMsgType msgType) where T : MessageBase, new()
        //{
        //    var playerInfo = GetPlayerInfo(connectionId);
        //    var returning = ApadanaMessageBase.CreateWithRequestID
        //    (response,
        //        msgType,
        //        msg.header,
        //        msg.requestID);
        //    try
        //    {
        //        await playerInfo.connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
        //        {
        //            player = player, connectionId = connectionId, channelId = channelId, data = returning.ToByteArray(),
        //            eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _Logger.LogError(-1, $"sending data to client failed: {ex.Message}");
        //    }
        //}
        
        public static EventParameter.ParamType GetReturnType(object returnValue)
        {
            Type t = returnValue.GetType();
            if (t == typeof(int))
                return EventParameter.ParamType.IntParam;
            if (t == typeof(short))
                return EventParameter.ParamType.ShortParam;
            if (t == typeof(long))
                return EventParameter.ParamType.LongParam;
            if (t == typeof(bool))
                return EventParameter.ParamType.BoolParam;
            if (t == typeof(byte[]))
                return EventParameter.ParamType.ByteArrayParam;
            if (t == typeof(byte))
                return EventParameter.ParamType.ByteParam;
            if (t == typeof(float))
                return EventParameter.ParamType.FloatParam;
            if (t == typeof(double))
                return EventParameter.ParamType.DoubleParam;
            if (t == typeof(string))
                return EventParameter.ParamType.StringParam;
            throw new ArgumentException("Invalid argument type");
        }

        //--------------------------------------------------------------------------------------------------------------------------------
        public async Task<bool> Send(int id, byte[] message)
        {
            if(!clientConnections.ContainsKey(id))
                return false;
            PlayerInfo player = clientConnections[id];
            return await Send(player, message);
        }

        public async Task<bool> Send(IPlayer player, byte[] message)
        {
            PlayerInfo playerInfo = GetPlayerInfo(playerConnectionMapper[player]);
            if(playerInfo == null)
                return false;
            return await Send(playerInfo, message);
        }

        public async Task<bool> Send(ConnectionInfo connection, byte[] message)
        {
            return await connection.processor.Send(message);
        }
    }

    public class ConnectionInfo
    {
        public ConnectionProcessor processor { get; protected set; }
    }

    public class PlayerInfo : ConnectionInfo
    {
        public string userId { get; private set; }
        public IPlayer Player { get; set; }
        public bool IsLoggedIn { get; set; }
        public Guid GameKey { get; set; }
        public Guid loginTimeRandomKey;
        public string Host { get; set; }
        public int ConnectionId { get; set; }
        public DateTime lastSentHeartBeat;

        public string titleId;

        public IAsyncStream<UNetConnectionDataTransferMessage> connectionStream;
        public IAsyncStream<ChallengeEventData> ChallengesStream;
        public IAsyncStream<FriendListEventData> friendListStream;
        public StreamSubscriptionHandle<ChallengeEventData> challengesHandle;
        public StreamSubscriptionHandle<FriendListEventData> friendlistHandle;

        public void SetPlayerInfo(string uid)
        {
            userId = uid;
        }

        public PlayerInfo(ConnectionProcessor processor)
        {
            this.processor = processor;
        }

        public async Task OnNextChallengeMessage(ChallengeEventData msg, StreamSequenceToken token)
        {
            if (msg.TitleKey != GameKey)
                return;
            ChallengeMessage dataToSend = new ChallengeMessage
            {
                Player = msg.otherPlayer.GetPrimaryKeyString(),
                PlayerName = await msg.otherPlayer.GetPlayerName(),
                GameKey = Guid.Empty,
                data = msg.data
            };
            OptionalResponse<ChallengeMessage> response = new OptionalResponse<ChallengeMessage>();
            ApadanaMsgType msgType;
            try
            {
                switch (msg.Type)
                {
                    case ChallengeEventData.EventType.Started:
                        dataToSend.GameKey = msg.gameID;
                        msgType = ApadanaMsgType.ChallengeAcceptedEvent;
                        response.Succeed(dataToSend);
                        break;
                    case ChallengeEventData.EventType.Deny:
                        msgType = ApadanaMsgType.ChallengeDeniedEvent;
                        response.Succeed(dataToSend);
                        break;
                    case ChallengeEventData.EventType.Request:
                        msgType = ApadanaMsgType.StartChallengeRequestedEvent;
                        response.Succeed(dataToSend);
                        break;
                    default:
                        throw new ArgumentException("invalid message type in challenge stream");
                }
            }
            catch (Exception x)
            {
                if (msg.Type == ChallengeEventData.EventType.Started)
                    msgType = ApadanaMsgType.ChallengeAcceptedEvent;
                else if (msg.Type == ChallengeEventData.EventType.Deny)
                    msgType = ApadanaMsgType.ChallengeDeniedEvent;
                else
                    msgType = ApadanaMsgType.StartChallengeRequestedEvent;
                response.Fail(x.Message, -1);
            }

            ApadanaMessageBase returning = ApadanaMessageBase.Create(response, msgType, ApadanaMsgHeader.normal);
            await connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
            {
                player = Player, connectionId = ConnectionId, channelId = 0,
                data = returning.ToByteArray(), eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
            });
        }

        public Task OnNextFriendMessage(FriendListEventData msg, StreamSequenceToken token)
        {
            if (GameKey != msg.GameKey)
                return Task.CompletedTask;
            FriendlistMessage dataToSend = new FriendlistMessage
            {
                friend = msg.Friend.GetPrimaryKeyString(),
                friendName = msg.FriendName,

                data = msg.Data,
                status = (RealtimeServer.OnlineStatus) msg.OnlineStatus
            };
            OptionalResponse<FriendlistMessage> response = new OptionalResponse<FriendlistMessage>();
            ApadanaMsgType msgType;
            switch (msg.Type)
            {
                case FriendListEventData.EventType.Accept:
                    msgType = ApadanaMsgType.FriendshipAcceptedEvent;
                    response.Succeed(dataToSend);
                    break;
                case FriendListEventData.EventType.Deny:
                    msgType = ApadanaMsgType.FriendshipDeniedEvent;
                    response.Succeed(dataToSend);
                    break;
                case FriendListEventData.EventType.Invite:
                    msgType = ApadanaMsgType.FriendshipInvitationEvent;
                    response.Succeed(dataToSend);
                    break;
                case FriendListEventData.EventType.StateChange:
                    msgType = ApadanaMsgType.FriendStatusChangedEvent;
                    response.Succeed(dataToSend);
                    break;
                default:
                    throw new InvalidOperationException("The friendship event type is not valid");
            }

            ApadanaMessageBase returning = ApadanaMessageBase.Create(response, msgType, ApadanaMsgHeader.normal);
            connectionStream.OnNextAsync(new UNetConnectionDataTransferMessage
            {
                player = Player, connectionId = ConnectionId, channelId = 0, data = returning.ToByteArray(),
                eventType = UNetConnectionDataTransferMessage.EventType.DataTransfer
            });
            return Task.CompletedTask;
        }
    }
}