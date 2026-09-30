using System.Linq;
using System.Threading.Tasks;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>Friends and challenges: requests from one client, pushed events on the other.</summary>
    [Collection(ServerCollection.Name)]
    public class SocialTests : ServerTestBase
    {
        public SocialTests(ServerFixture server)
            : base(server)
        {
        }

        [Fact]
        public async Task FriendInvitationsAndAnswersArriveAsEvents()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, aliceLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var (bob, bobLogin) = await LoggedInClientAsync(developerKey, gameKey);
            await Wait.For(alice.Profile.SetNameAsync("Alice"));
            var invitations = new Recorder<FriendEvent>();
            var accepted = new Recorder<FriendEvent>();
            bob.Friends.InvitationReceived += invitations.Add;
            alice.Friends.InvitationAccepted += accepted.Add;

            await Wait.For(alice.Friends.InviteByPlayerIdAsync(bobLogin.PlayerId, new byte[] { 42 }));

            FriendEvent invitation = await invitations.WaitForAnyAsync("Bob's invitation event");
            Assert.Equal(aliceLogin.PlayerId, invitation.PlayerId);
            Assert.Equal("Alice", invitation.PlayerName);
            Assert.Equal(new byte[] { 42 }, invitation.Data);

            FriendList bobsList = await Wait.For(bob.Friends.GetAsync());
            FriendInvitationInfo pending = Assert.Single(bobsList.ReceivedInvitations);
            Assert.Equal(aliceLogin.PlayerId, pending.PlayerId);

            await Wait.For(bob.Friends.AcceptAsync(aliceLogin.PlayerId));
            FriendEvent answer = await accepted.WaitForAnyAsync("Alice's accepted event");
            Assert.Equal(bobLogin.PlayerId, answer.PlayerId);

            FriendList alicesList = await Wait.For(alice.Friends.GetAsync());
            Friend friend = Assert.Single(alicesList.Friends);
            Assert.Equal(bobLogin.PlayerId, friend.PlayerId);
            Assert.Equal(OnlineStatus.Online, friend.Status);
        }

        [Fact]
        public async Task InvitingSomebodyWhoDoesNotExistFails()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, _) = await LoggedInClientAsync(developerKey, gameKey);

            var error = await Assert.ThrowsAsync<WildCatException>(() => Wait.For(alice.Friends.InviteByUserNameAsync(Unique.Id("nobody"))));
            Assert.Equal(WildCatErrorCodes.Refused, error.ErrorCode);
        }

        [Fact]
        public async Task ADeniedChallengeReachesTheChallenger()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, aliceLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var (bob, bobLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var challenges = new Recorder<ChallengeEvent>();
            var denials = new Recorder<ChallengeEvent>();
            bob.Challenges.ChallengeReceived += challenges.Add;
            alice.Challenges.ChallengeDenied += denials.Add;

            await Wait.For(alice.Challenges.SendAsync(bobLogin.PlayerId, 30, new byte[] { 5 }));
            ChallengeEvent challenge = await challenges.WaitForAnyAsync("Bob's challenge event");
            Assert.Equal(aliceLogin.PlayerId, challenge.PlayerId);
            Assert.Equal(new byte[] { 5 }, challenge.Data);

            // Received challenges are listed as received (the server listed them under sent before step 6b).
            ChallengeList bobsList = await Wait.For(bob.Challenges.GetAsync());
            Assert.Contains(bobsList.Received, c => c.PlayerId == aliceLogin.PlayerId);
            Assert.DoesNotContain(bobsList.Sent, c => c.PlayerId == aliceLogin.PlayerId);

            // The old SDK waited for the deny answer on the request type, so this never completed.
            await Wait.For(bob.Challenges.DenyAsync(aliceLogin.PlayerId));
            ChallengeEvent denial = await denials.WaitForAnyAsync("Alice's denial event");
            Assert.Equal(bobLogin.PlayerId, denial.PlayerId);

            ChallengeList list = await Wait.For(alice.Challenges.GetAsync());
            Assert.NotNull(list.Sent);
        }
    }
}
