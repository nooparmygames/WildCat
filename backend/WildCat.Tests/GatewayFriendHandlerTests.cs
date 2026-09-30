using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;
using OnlineStatus = WildCat.Protocol.OnlineStatus;

namespace WildCat.Tests
{
    /// <summary>
    /// The friend handlers over real WebSockets, with two or three connected players: GetFriends
    /// (88), InviteFriend (90), AcceptFriend (93), DenyFriend (96), and the friend events pushed
    /// from the FriendEvents stream (92, 95, 98, 99), including that a closed or switched socket
    /// gives its subscriptions up.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayFriendHandlerTests
    {
        private const string Password = "a-good-password";
        private readonly InProcessTestCluster cluster;

        public GatewayFriendHandlerTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static readonly WildCatMsgType[] FriendRequests =
        {
            WildCatMsgType.GetFriendsRequest, WildCatMsgType.InviteFriendRequest,
            WildCatMsgType.AcceptFriendRequest, WildCatMsgType.DenyFriendRequest,
        };

        private static Task<OptionalResponse<EmptyMessage>> Invite(SocialPlayer from, InvitationType type, string value, byte[] data = null)
        {
            return from.CallAsync<EmptyMessage>(WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = type, value = value, data = data ?? new byte[0] }, WildCatMsgType.InviteFriendResponse);
        }

        private static Task<OptionalResponse<EmptyMessage>> Accept(SocialPlayer player, string inviterId, byte[] data = null)
        {
            return player.CallAsync<EmptyMessage>(WildCatMsgType.AcceptFriendRequest,
                new StringAndByteArrayMessage { stringValue = inviterId, byteArrayValue = data ?? new byte[0] }, WildCatMsgType.AcceptFriendResponse);
        }

        private static Task<OptionalResponse<EmptyMessage>> Deny(SocialPlayer player, string inviterId, byte[] data = null)
        {
            return player.CallAsync<EmptyMessage>(WildCatMsgType.DenyFriendRequest,
                new StringAndByteArrayMessage { stringValue = inviterId, byteArrayValue = data ?? new byte[0] }, WildCatMsgType.DenyFriendResponse);
        }

        private static async Task<FriendlistCollection> GetFriends(SocialPlayer player, params string[] publicKeys)
        {
            var response = await player.CallAsync<FriendlistCollection>(WildCatMsgType.GetFriendsRequest,
                new GetFriendsRequestMessage { publicKeys = publicKeys }, WildCatMsgType.GetFriendsResponse);
            Assert.True(response.isSuccesful, response.errorMsg);
            return response.msg;
        }

        private static void AssertSucceeded<T>(OptionalResponse<T> response) where T : MessageBase, new()
        {
            Assert.True(response.isSuccesful, response.errorMsg);
        }

        private static void AssertRefused<T>(OptionalResponse<T> response, string message) where T : MessageBase, new()
        {
            Assert.False(response.isSuccesful);
            Assert.Equal(GatewayErrors.Refused, response.errorCode);
            Assert.Equal(message, response.errorMsg);
        }

        /// <summary>Alice invites Bob, Bob accepts, and both have seen what they should.</summary>
        private static async Task MakeFriends(SocialPlayer alice, SocialPlayer bob)
        {
            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id));
            await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            AssertSucceeded(await Accept(bob, alice.Id));
            await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipAcceptedEvent);
        }

        [Fact]
        public async Task FriendMessagesHaveTheirOwnHandlers()
        {
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            foreach (var type in FriendRequests)
            {
                Assert.True(host.Handlers.TryGet(type, out var route, out var handler), $"{type} has no handler");
                Assert.IsNotType<LegacyAdapterHandler>(handler);
                Assert.Equal(type + 1, route.ResponseType);
                Assert.Equal(SessionRequirement.Player, route.Session);
                Assert.False(route.IsSignInAttempt);
            }
            Assert.DoesNotContain(host.Handlers.LegacyTypes, t => FriendRequests.Contains(t));
        }

        [Fact]
        public async Task FriendMessagesNeedALoggedInPlayer()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            await using var client = await game.Host.ConnectAsync();
            var session = await client.InitializeSessionAsync(game.DeveloperKey, game.GameKey);

            foreach (var type in FriendRequests)
            {
                var noTicket = await client.CallAsync<EmptyMessage>(type, new EmptyMessage(), WildCatMsgType.ErrorMsg);
                AssertRefused(noTicket, GatewayErrors.SessionRequired);
                var noLogin = await client.CallAsync<EmptyMessage>(type, new EmptyMessage(), WildCatMsgType.ErrorMsg, session.ToString());
                AssertRefused(noLogin, GatewayErrors.NotLoggedIn);
            }
        }

        [Fact]
        public async Task InvitingByAccountIdEmailAndUserNamePushesTheInvitation()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            var carol = await game.JoinAsync("Carol");
            string bobEmail = $"{TestSetup.UniqueId("bob")}@example.com";
            string bobUser = TestSetup.UniqueId("bob");
            Assert.Equal(AdditionResult.Success, await bob.Grain(game.Grains).AddPlayerEmail(bobEmail, Password));
            Assert.Equal(AdditionResult.Success, await bob.Grain(game.Grains).AddPlayerUserAndPassword(bobUser, Password));

            // By email, in another case than it was added with: addresses are compared in lower case.
            AssertSucceeded(await Invite(alice, InvitationType.Email, bobEmail.ToUpperInvariant(), new byte[] { 5 }));
            var invitation = await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            Assert.Equal(alice.Id, invitation.friend);
            Assert.Equal("Alice", invitation.friendName);
            Assert.Equal(new byte[] { 5 }, invitation.data);

            // By user name: a repeated invitation is pushed again but listed once.
            AssertSucceeded(await Invite(alice, InvitationType.UserId, bobUser, new byte[] { 6 }));
            invitation = await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            Assert.Equal(alice.Id, invitation.friend);
            Assert.Equal(new byte[] { 6 }, invitation.data);

            AssertSucceeded(await Invite(alice, InvitationType.AccountId, carol.Id, new byte[] { 7 }));
            invitation = await carol.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            Assert.Equal(alice.Id, invitation.friend);
            Assert.Equal(new byte[] { 7 }, invitation.data);

            var alices = await GetFriends(alice);
            Assert.Empty(alices.friends);
            Assert.Empty(alices.receivedInvitations.invitations);
            Assert.Equal(new[] { (bob.Id, "Bob"), (carol.Id, "Carol") },
                alices.sentInvitation.invitations.Select(i => (i.OtherPlayerId, i.otherPlayerName)).OrderBy(i => i.Item2));

            var bobs = await GetFriends(bob);
            Assert.Empty(bobs.sentInvitation.invitations);
            var received = Assert.Single(bobs.receivedInvitations.invitations);
            Assert.Equal(alice.Id, received.OtherPlayerId);
            Assert.Equal("Alice", received.otherPlayerName);
            Assert.Equal(new byte[] { 5 }, received.data);
            Assert.True(DateTime.UtcNow - received.creationTime < TimeSpan.FromMinutes(1));

            // GetSentInvitations used to return the received ones.
            var sent = await game.Grains.GetGrain<IPlayerFriendList>(game.GameKey, alice.Id, null).GetSentInvitations();
            Assert.Equal(2, sent.Count);
            Assert.All(sent, i => Assert.Equal(alice.Id, i.Sender.GetPrimaryKeyString()));

            Assert.Equal(0, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipInvitationEvent));
            Assert.Equal(2, bob.Client.Received.Count(m => m.msgType == WildCatMsgType.FriendshipInvitationEvent));
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task InviteFriendRefusesUnknownPlayersYourselfAndFriends()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertRefused(await Invite(alice, InvitationType.AccountId, TestSetup.UniqueId("nobody")), "Player not found.");
            AssertRefused(await Invite(alice, InvitationType.AccountId, ""), "Player not found.");
            AssertRefused(await Invite(alice, InvitationType.Email, $"{TestSetup.UniqueId("nobody")}@example.com"), "Player not found.");
            AssertRefused(await Invite(alice, InvitationType.UserId, TestSetup.UniqueId("nobody")), "Player not found.");
            // Nothing fills the player name map yet (docs/legacy-backend-notes.md bug 20).
            AssertRefused(await Invite(alice, InvitationType.playerName, "Bob"), "Player not found.");
            AssertRefused(await Invite(alice, InvitationType.AccountId, alice.Id), FriendListException.CannotInviteYourself);

            await MakeFriends(alice, bob);
            AssertRefused(await Invite(alice, InvitationType.AccountId, bob.Id), FriendListException.AlreadyFriends);
            AssertRefused(await Invite(bob, InvitationType.AccountId, alice.Id), FriendListException.AlreadyFriends);

            Assert.Equal(1, await bob.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipInvitationEvent));
            Assert.Empty((await GetFriends(alice)).sentInvitation.invitations);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AcceptingMakesBothFriendsWithTheirPublicDataAndTellsTheInviter()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            await bob.Grain(game.Grains).SetPlayerPublicString("level", "7");
            await bob.Grain(game.Grains).SetPlayerPublicString("title", "hidden");

            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id));
            await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            AssertSucceeded(await Accept(bob, alice.Id, new byte[] { 8 }));

            var accepted = await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipAcceptedEvent);
            Assert.Equal(bob.Id, accepted.friend);
            Assert.Equal("Bob", accepted.friendName);
            Assert.Equal(new byte[] { 8 }, accepted.data);

            // Only the keys asked for, and only the ones the friend has.
            var alices = await GetFriends(alice, "level", "missing");
            var friend = Assert.Single(alices.friends);
            Assert.Equal(bob.Id, friend.userId);
            Assert.Equal("Bob", friend.name);
            Assert.Equal(OnlineStatus.Online, friend.status);
            Assert.Equal(new Dictionary<string, string> { ["level"] = "7" }, friend.data);
            Assert.Empty(alices.sentInvitation.invitations);
            Assert.Empty(alices.receivedInvitations.invitations);

            var bobs = await GetFriends(bob);
            friend = Assert.Single(bobs.friends);
            Assert.Equal(alice.Id, friend.userId);
            Assert.Equal("Alice", friend.name);
            Assert.Equal(OnlineStatus.Online, friend.status);
            Assert.Empty(friend.data);
            Assert.Empty(bobs.receivedInvitations.invitations);

            // The invitation is used up. (Accepting a missing one used to throw from First.)
            AssertRefused(await Accept(bob, alice.Id), FriendListException.NoInvitation);
            Assert.Equal(0, await bob.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipAcceptedEvent));
            Assert.Equal(1, alice.Client.Received.Count(m => m.msgType == WildCatMsgType.FriendshipAcceptedEvent));
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AcceptingWithoutAnInvitationIsRefused()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertRefused(await Accept(bob, alice.Id), FriendListException.NoInvitation);
            AssertRefused(await Accept(bob, ""), FriendListException.NoInvitation);
            AssertRefused(await Accept(bob, TestSetup.UniqueId("nobody")), FriendListException.NoInvitation);
            // Bob's own invitation to Alice is not one Bob can accept.
            AssertSucceeded(await Invite(bob, InvitationType.AccountId, alice.Id));
            AssertRefused(await Accept(bob, alice.Id), FriendListException.NoInvitation);

            Assert.Empty((await GetFriends(alice)).friends);
            Assert.Empty((await GetFriends(bob)).friends);
            Assert.Equal(0, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipAcceptedEvent));
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task DenyingTellsTheInviterOnce()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");

            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id));
            await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            AssertSucceeded(await Deny(bob, alice.Id, new byte[] { 9 }));

            var denied = await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipDeniedEvent);
            Assert.Equal(bob.Id, denied.friend);
            Assert.Equal(new byte[] { 9 }, denied.data);

            var alices = await GetFriends(alice);
            Assert.Empty(alices.friends);
            Assert.Empty(alices.sentInvitation.invitations);
            var bobs = await GetFriends(bob);
            Assert.Empty(bobs.friends);
            Assert.Empty(bobs.receivedInvitations.invitations);

            // Nothing left to deny or accept: denying again succeeds and tells nobody.
            AssertSucceeded(await Deny(bob, alice.Id));
            AssertRefused(await Accept(bob, alice.Id), FriendListException.NoInvitation);
            AssertRefused(await Deny(bob, ""), FriendListException.NoInvitation);
            Assert.Equal(1, await alice.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipDeniedEvent));
            Assert.Equal(0, bob.Client.Received.Count(m => m.msgType == WildCatMsgType.FriendshipDeniedEvent));

            // Alice may invite again after a denial.
            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id));
            await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task FriendsSeeEachOtherLogOutAndLogIn()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            await MakeFriends(alice, bob);

            await game.DisconnectAsync(bob);
            var offline = await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendStatusChangedEvent);
            Assert.Equal(bob.Id, offline.friend);
            Assert.Equal(OnlineStatus.Offline, offline.status);
            Assert.Equal(OnlineStatus.Offline, Assert.Single((await GetFriends(alice)).friends).status);

            await game.ConnectAsync(bob);
            var online = await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendStatusChangedEvent);
            Assert.Equal(bob.Id, online.friend);
            Assert.Equal(OnlineStatus.Online, online.status);
            Assert.Equal(OnlineStatus.Online, Assert.Single((await GetFriends(alice)).friends).status);

            // And the other way round, on Bob's new socket.
            await game.DisconnectAsync(alice);
            offline = await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendStatusChangedEvent);
            Assert.Equal(alice.Id, offline.friend);
            Assert.Equal(OnlineStatus.Offline, offline.status);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task AClosedSocketGivesUpItsFriendEvents()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            Assert.Equal(1, await game.FriendSubscriptionsAsync(bob.Id));
            Assert.Equal(1, await game.ChallengeSubscriptionsAsync(bob.Id));

            await game.DisconnectAsync(bob);
            Assert.Equal(0, await game.FriendSubscriptionsAsync(bob.Id));
            Assert.Equal(0, await game.ChallengeSubscriptionsAsync(bob.Id));

            // Nothing on this node listens for Bob any more, nothing fails, and the invitation waits for him.
            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id, new byte[] { 1 }));
            await Task.Delay(SocialPlayer.QuietPeriod);

            await game.ConnectAsync(bob);
            var received = Assert.Single((await GetFriends(bob)).receivedInvitations.invitations);
            Assert.Equal(alice.Id, received.OtherPlayerId);
            Assert.Equal(new byte[] { 1 }, received.data);
            AssertSucceeded(await Accept(bob, alice.Id));
            await alice.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipAcceptedEvent);
            game.AssertNothingWentWrong();
        }

        [Fact]
        public async Task LoggingInAsAnotherPlayerOnTheSocketMovesItsFriendEvents()
        {
            await using var game = await GatewaySocialGame.StartAsync(cluster.Client);
            var alice = await game.JoinAsync("Alice");
            var bob = await game.JoinAsync("Bob");
            var carol = await game.CreateAsync("Carol");

            // Carol logs in on Bob's socket and session.
            var carolLogin = await bob.Client.LoginWithDeviceAsync(Guid.Parse(bob.Ticket), carol.Device);
            Assert.Equal(carol.Id, carolLogin.userId);
            await TestSetup.WaitUntilAsync(async () => !await bob.Grain(game.Grains).IsLoggedIn(), "Bob to be logged out");
            Assert.Equal(0, await game.FriendSubscriptionsAsync(bob.Id));
            Assert.Equal(0, await game.ChallengeSubscriptionsAsync(bob.Id));
            Assert.Equal(1, await game.FriendSubscriptionsAsync(carol.Id));
            Assert.Equal(1, await game.ChallengeSubscriptionsAsync(carol.Id));

            AssertSucceeded(await Invite(alice, InvitationType.AccountId, bob.Id));
            AssertSucceeded(await Invite(alice, InvitationType.AccountId, carol.Id));
            var invitation = await bob.WaitForEventAsync<FriendlistMessage>(WildCatMsgType.FriendshipInvitationEvent);
            Assert.Equal(alice.Id, invitation.friend);

            // Only Carol's invitation reaches the socket: Bob's is stored for his next login.
            Assert.Equal(1, await bob.CountAfterQuietPeriodAsync(WildCatMsgType.FriendshipInvitationEvent));
            var carolsList = await game.Grains.GetGrain<IPlayerFriendList>(game.GameKey, carol.Id, null).GetReceivedInvitations();
            Assert.Single(carolsList);
            var bobsList = await game.Grains.GetGrain<IPlayerFriendList>(game.GameKey, bob.Id, null).GetReceivedInvitations();
            Assert.Single(bobsList);
            game.AssertNothingWentWrong();
        }
    }
}
