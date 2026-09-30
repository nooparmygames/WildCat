using WildCat.Interfaces;
using Orleans;
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
        private const int MaxTrycount = 5;

        public FrontEndServer(ILogger<FrontEndServer> logger, IClusterClient clusterClient)
        {
            orleansClient = clusterClient;
            _Logger = logger;
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

                case WildCatMsgType.GetPlayerChallengesRequest:
                    await ProcessGetPlayerChallengesRequest(session, player, connection, channelId, msg, ticket);
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
                default:
                    throw new InvalidOperationException($"The legacy gateway code has no case for {msg.msgType}.");
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

    }
}
