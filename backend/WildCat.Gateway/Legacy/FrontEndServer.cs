using WildCat.Interfaces;
using Orleans;
using Orleans.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildCat.Protocol;

namespace WildCat.Gateway
{
    /// <summary>
    /// The old gateway code for the message types that have no handler of their own yet, called
    /// through <see cref="LegacyAdapterHandler"/>. The dispatcher has already decoded the frame,
    /// applied the sign-in limit and checked the session. Roadmap step 6b moves each group to
    /// handlers under Handlers/ and deletes its code here; after that this file and the adapter go.
    /// </summary>
    public class FrontEndServer
    {
        private readonly ILogger _Logger;
        private readonly IClusterClient orleansClient;
        private IStreamProvider streamProvider;
        private const int MaxTrycount = 5;

        public FrontEndServer(ILogger<FrontEndServer> logger, IClusterClient clusterClient)
        {
            orleansClient = clusterClient;
            _Logger = logger;
        }

        /// <summary>
        /// The player's PlayerConnection stream. FindMatch and LeaveGame answer through it (as they
        /// always did), so the answer reaches the client after the match events published before it.
        /// </summary>
        private IAsyncStream<PlayerConnectionMessage> ConnectionStream(IPlayer player)
        {
            streamProvider ??= orleansClient.GetStreamProvider(StreamNames.PROVIDER_NAME);
            return streamProvider.GetStream<PlayerConnectionMessage>(StreamNames.PlayerConnection(player.GetPrimaryKeyString()));
        }

        /// <summary>Handles one request of a type listed in <see cref="LegacyAdapterHandler"/>.</summary>
        public async Task HandleAsync(MessageContext context)
        {
            SessionGameInfo session = context.Session;
            IPlayer player = context.Player;
            GatewayConnection connection = context.Connection;
            WildCatMessageBase msg = context.Request;
            Guid ticket = context.Ticket;
            const int channelId = 0;
            const bool isReliable = true;

            _Logger.LogTrace("Legacy handling of {MessageType} with a {Size} byte payload", msg.msgType, msg.RawData.Length);
            switch (msg.msgType)
            {
                case WildCatMsgType.SetPublisherDataRequest_S:
                    await ProcessSetPublisherDataByDeveloperMessage(session, player, connection, channelId, msg);
                    break;
                case WildCatMsgType.GetInventoryRequest:
                    await ProcessGetInventoryRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetInventoryUnlockTimeRequest:
                    await ProcessGetInventoryUnlockTimeRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.UnlockInventoryRequest:
                    await ProcessUnlockInventoryRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.AddItemToInventoryRequest:
                    await RejectClientGrant(connection, msg, WildCatMsgType.AddItemToInventoryResponse);
                    break;
                case WildCatMsgType.SetItemToInventoryRequest:
                    await RejectClientGrant(connection, msg, WildCatMsgType.SetItemToInventoryResponse);
                    break;
                case WildCatMsgType.RemoveInventoryItemRequest:
                    await ProcessRemoveInventoryRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPublisherDataRequest_S:
                    await ProcessGetPublisherDataByDeveloperMessage(session, player, connection, channelId, msg);
                    break;

                case WildCatMsgType.FindMatchRequest:
                    await ProcessFindMatchRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.SetMatchPropertyRequest:
                    await ProcessSetMatchPropertyRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.RejoinMatchRequest:
                    await ProcessRejoinMatchRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPlayerPendingMatchesRequest:
                    await ProcessGetPlayerPendingMatchesRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPlayerChallengesRequest:
                    await ProcessGetPlayerChallengesRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.SetNameRequest:
                    await ProcessSetNameRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.InviteFriendRequest:
                    await ProcessInviteFriendsRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetFriendsRequest:
                    await ProcessGetFriendsRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.AcceptFriendRequest:
                    await ProcessAcceptFriendsRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.DenyFriendRequest:
                    await ProcessDenyFriendsRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetNameRequest:
                    await ProcessGetNameRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPlayerNameRequest:
                    await ProcessGetPlayerNameRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.SetCloudDataRequest:
                    await ProcessSetCloudDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.SetPublicCloudDataRequest:
                    await ProcessSetPublicCloudDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetCloudDataRequest:
                    await ProcessGetCloudDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPublicCloudDataRequest:
                    await ProcessGetPublicCloudDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPlayerPublicCloudDataRequest:
                    await ProcessGetPlayerPublicCloudDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetPublisherDataRequest:
                    await ProcessGetPublisherDataRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.GetLeaderboardRequest:
                    await ProcessGetLeaderBoardRequest(session, player, connection, channelId, msg, ticket);
                    break;
                case WildCatMsgType.SetLeaderboardRequest:
                    await RejectClientGrant(connection, msg, WildCatMsgType.SetLeaderboardResponse);
                    break;
                case WildCatMsgType.LeaveGameRequest:
                    await ProcessLeaveGameMessage(session, player, connection, channelId, msg, ticket, isReliable, 0);
                    break;
                case WildCatMsgType.StartChallengeRequest:
                    await ProcessStartChallengeMessage(session, player, connection, channelId, msg, ticket,
                        isReliable, 0);
                    break;
                case WildCatMsgType.AcceptChallengeRequest:
                    await ProcessAcceptChallengeMessage(session, player, connection, channelId, msg, ticket,
                        isReliable, 0);
                    break;
                case WildCatMsgType.DenyChallengeRequest:
                    await ProcessDenyChallengeMessage(session, player, connection, channelId, msg, ticket, isReliable,
                        0);
                    break;
                case WildCatMsgType.ExecuteScriptEventRequest:
                    await ProcessExecuteScriptEventRequest(session, player, connection, channelId, msg, ticket);
                    break;
                default:
                    throw new InvalidOperationException($"The legacy gateway code has no case for {msg.msgType}.");
            }
        }

        private async Task ProcessSetPublisherDataByDeveloperMessage(SessionGameInfo session, IPlayer player,
            GatewayConnection connection, int channelId, WildCatMessageBase msg)
        {
            var request = msg.GetPayload<SetPublisherDataRequest>();
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

            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.SetPublisherDataResponse_S, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPublisherDataByDeveloperMessage(SessionGameInfo session, IPlayer player,
            GatewayConnection connection, int channelId, WildCatMessageBase msg)
        {
            var request = msg.GetPayload<GetPublisherDataRequest>();
            OptionalResponse<StringMessage> response = new OptionalResponse<StringMessage>();
            try
            {
                var developerStorage = orleansClient.GetGrain<IDeveloperStorage>(request.developerKey);
                var readValue = await developerStorage.GetStringForDeveloper(request.key, request.developerSecret);
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPublisherDataResponse_S, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetInventoryRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var name = msg.GetPayload<StringMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(connection.GameKey.ToString(), player.GetPrimaryKeyString(),
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetInventoryResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetInventoryUnlockTimeRequest(SessionGameInfo session, IPlayer player,
            GatewayConnection connection, int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var name = msg.GetPayload<StringMessage>();
            OptionalResponse<GetInventoryUnlockTimeResponseMessage> response =
                new OptionalResponse<GetInventoryUnlockTimeResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(connection.GameKey.ToString(), player.GetPrimaryKeyString(),
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetInventoryUnlockTimeResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessUnlockInventoryRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var name = msg.GetPayload<StringMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(connection.GameKey.ToString(), player.GetPrimaryKeyString(),
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.UnlockInventoryResponse, msg);
            connection.Send(returning);
        }

        /// <summary>
        /// Answers 28 (SetLeaderboard), 111 (AddItemToInventory) and 113 (SetItemToInventory) with
        /// an error. Clients used to grant themselves items and pick their own scores with these;
        /// grants and scores now come from server script events (WildCatScriptApi). Removing items
        /// (115) stays allowed because players may spend their own items.
        /// </summary>
        private Task RejectClientGrant(GatewayConnection connection, WildCatMessageBase msg, WildCatMsgType responseType)
        {
            _Logger.LogInformation("Rejected {MessageType} from connection {ConnectionId}: clients may not grant items or set scores",
                msg.msgType, connection.Id);
            // A failed response carries no payload, so the payload type does not matter on the wire.
            var response = new OptionalResponse<EmptyMessage>();
            response.Fail(GatewayErrors.ClientGrantRejected, 1);
            var returning = WildCatMessageBase.CreateResponse(response, responseType, msg);
            connection.Send(returning);
            return Task.CompletedTask;
        }

        private async Task ProcessRemoveInventoryRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var req = msg.GetPayload<RemoveItemInventoryRequestMessage>();
            OptionalResponse<GetInventoryResponseMessage> response = new OptionalResponse<GetInventoryResponseMessage>();
            try
            {
                var inv = orleansClient.GetGrain<IPlayerInventory>(
                    ResourcesUtility.GetPlayerInventoryKey(connection.GameKey.ToString(), player.GetPrimaryKeyString(),
                        req.inventoryName));
                // IInventoryItem has two grain classes (items and packages), so the class has to be
                // named; without it the lookup threw and no removal ever worked.
                bool removed = await inv.RemoveItem(
                    req.items.Select(x =>
                        orleansClient.GetGrain<IInventoryItem>(
                            ResourcesUtility.GetItemKey(connection.GameKey.ToString(), x.Item1),
                            ResourcesUtility.GetGameItemsClassName())).ToArray(),
                    req.items.Select(x => x.Item2).ToArray());
                if (removed)
                {
                    (string, int)[] stuff = await GetInventoryItems(inv);
                    GetInventoryResponseMessage responseData = new GetInventoryResponseMessage
                    {
                        items = stuff
                    };
                    response.Succeed(responseData);
                }
                else
                {
                    // RemoveItem changes nothing when a count is not positive or more than the
                    // player holds.
                    response.Fail("The inventory does not hold these items in these amounts.", 1);
                }
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"RemoveInventoryRequest Error: {x.ToString()}", x);
                response.Fail(x.Message, -1);
            }
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.RemoveInventoryItemResponse, msg);
            connection.Send(returning);
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

        private async Task ProcessLeaveGameMessage(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket, bool reliable, int count)
        {
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
                        await ProcessLeaveGameMessage(session, player, connection, channelId, msg, ticket, reliable,
                            count + 1);
                        return;
                    }
                    else
                    {
                        _Logger.LogError((int) msg.msgType, $"LeaveGame Failed after {MaxTrycount}");
                        response.Fail(x.Message, -1);
                    }
                }

                WildCatMessageBase returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.LeaveGameResponse, msg);
                await ConnectionStream(player).OnNextAsync(new PlayerConnectionMessage
                {
                    player = player, connectionId = connection.Id, channelId = channelId, data = WireCodec.Encode(returning),
                    eventType = PlayerConnectionMessage.EventType.DataTransfer
                });
            }
        }

        private async Task ProcessStartChallengeMessage(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetPayload<ChallengeMessage>();
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
                    await ProcessStartChallengeMessage(session, player, connection, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"StartChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.StartChallengeResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessAcceptChallengeMessage(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetPayload<ChallengeMessage>();
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
                    await ProcessAcceptChallengeMessage(session, player, connection, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"AcceptChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.AcceptChallengeResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessDenyChallengeMessage(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket, bool reliable, int count)
        {
            ChallengeMessage challengeData = msg.GetPayload<ChallengeMessage>();
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
                    await ProcessDenyChallengeMessage(session, player, connection, channelId, msg, ticket, reliable,
                        count + 1);
                    return;
                }
                else
                {
                    _Logger.LogError((int) msg.msgType, $"DenyChallenge Failed after {MaxTrycount}");
                    response.Fail(x.Message, -1);
                }
            }
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.DenyChallengeResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessSetMatchPropertyRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<SetMatchPropertyData>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.SetMatchPropertyResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessRejoinMatchRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<GuidMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.RejoinMatchResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPlayerPendingMatchesRequest(SessionGameInfo session, IPlayer player,
            GatewayConnection connection, int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<EmptyMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPlayerMatchesResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPlayerChallengesRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<EmptyMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPlayerChallengesResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessFindMatchRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var request = msg.GetPayload<FindMatchRequest>();

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
                WildCatMessageBase.CreateResponse(response, WildCatMsgType.FindMatchResponse, msg);
            if (response.isSuccesful)
            {
                //TODO remove this and make it none automatic
                await orleansClient.GetGrain<IRelayServerGame>(response.msg.gameId, gk, null)
                    .PlayerStarted(player);
            }

            await ConnectionStream(player).OnNextAsync(new PlayerConnectionMessage
            {
                player = player, connectionId = connection.Id, channelId = channelId, data = WireCodec.Encode(returningMsg),
                eventType = PlayerConnectionMessage.EventType.DataTransfer
            });
        }

        private async Task ProcessSetNameRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.SetNameResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetFriendsRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<GetFriendsRequestMessage>();
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
                        status = (WildCat.Protocol.OnlineStatus) playerFriends.Friends[i].OnlineStatus,
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetFriendsResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessInviteFriendsRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
                        var request = msg.GetPayload<InviteFriendRequest>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.InviteFriendResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessAcceptFriendsRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringAndByteArrayMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.AcceptFriendResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessDenyFriendsRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringAndByteArrayMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.DenyFriendResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetCloudDataRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetCloudDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPublicCloudDataRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPublicCloudDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPlayerPublicCloudDataRequest(SessionGameInfo session, IPlayer player,
            GatewayConnection connection, int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<GetPlayerPublicCloudDataRequest>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPlayerPublicCloudDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPublisherDataRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPublisherDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetLeaderBoardRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<GetLeaderBoardRequest>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetLeaderboardResponse, msg);
            connection.Send(returning);
        }

        private static void FillReturningLeaderBoardEntries(GetLeaderBoardResponse responseToSend,
            List<WildCat.Interfaces.LeaderBoardEntry> aroundEntries)
        {
            foreach (var item in aroundEntries)
            {
                responseToSend.entries.Add(new WildCat.Protocol.LeaderBoardEntry
                {
                    score = item.Score,
                    userName = item.Name,
                    playerId = item.PlayerID,
                    entryTime = item.EntryTime
                });
            }
        }

        private async Task ProcessSetCloudDataRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<SetCloudDataRequest>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.SetCloudDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessSetPublicCloudDataRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<SetCloudDataRequest>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.SetPublicCloudDataResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessExecuteScriptEventRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<ScriptEventExecutionRequestMessage>();
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
                        (WildCat.Protocol.ParamType) GetReturnType(returnValue);
                }

                response.Succeed(responseMessage);
            }
            catch (Exception x)
            {
                _Logger.LogError((int) msg.msgType, $"SetCloudData Error: ", x);
                response.Fail(x.Message, -1);
            }
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.ExecuteScriptEventResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetNameRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetNameResponse, msg);
            connection.Send(returning);
        }

        private async Task ProcessGetPlayerNameRequest(SessionGameInfo session, IPlayer player, GatewayConnection connection,
            int channelId, WildCatMessageBase msg, Guid ticket)
        {
            var request = msg.GetPayload<StringMessage>();
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
            var returning = WildCatMessageBase.CreateResponse(response, WildCatMsgType.GetPlayerNameResponse, msg);
            connection.Send(returning);
        }

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
    }
}
