using Orleans;
using Orleans.TestingHost;
using WildCat.Gateway;
using WildCat.Interfaces;
using WildCat.Protocol;
using Xunit;
using static WildCat.Tests.GatewayProfileHandlerTests;

namespace WildCat.Tests
{
    /// <summary>
    /// Strings clients send that become grain keys or stored values have length limits
    /// (<see cref="InputLimits"/>): a longer one is answered with error code 1 on the request's
    /// response type. Before, a player could key grains or store cloud data by strings of up to a
    /// megabyte.
    /// </summary>
    [Collection(ClusterCollection.Name)]
    public class GatewayInputLimitTests
    {
        private static readonly string LongId = new string('p', InputLimits.MaxIdLength + 1);
        private static readonly string LongName = new string('n', InputLimits.MaxNameLength + 1);
        private static readonly string LongKey = new string('k', InputLimits.MaxKeyLength + 1);
        private static readonly string LongValue = new string('v', InputLimits.MaxValueLength + 1);
        private static readonly string LongEmail = new string('e', InputLimits.MaxEmailLength - 11) + "@example.com";

        private readonly InProcessTestCluster cluster;
        private IGrainFactory GrainFactory => cluster.Client;

        public GatewayInputLimitTests(ClusterFixture fixture)
        {
            cluster = fixture.Cluster;
        }

        private static async Task AssertTooLong(LoggedIn player, WildCatMsgType type, MessageBase payload, string what, int max,
            bool withTicket = true)
        {
            var response = await player.Client.CallAsync<EmptyMessage>(type, payload, type + 1, withTicket ? player.Ticket : null);
            AssertFailed(response, GatewayErrors.Refused, InputLimits.TooLong(what, max));
        }

        [Fact]
        public async Task TooLongStringsAreRefusedWithCodeOne()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client, o => o.AuthAttemptBurst = 50);
            var player = await LogIn(host, developerKey, gameKey);
            await using var _ = player.Client;
            const int Id = InputLimits.MaxIdLength, Name = InputLimits.MaxNameLength, Key = InputLimits.MaxKeyLength;

            // Cloud data and profile.
            await AssertTooLong(player, WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = LongKey, value = "v" }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = "k", value = LongValue }, "value",
                InputLimits.MaxValueLength);
            await AssertTooLong(player, WildCatMsgType.GetCloudDataRequest, new StringMessage { value = LongKey }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.SetPublicCloudDataRequest, new SetCloudDataRequest { key = LongKey, value = "v" }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.SetPublicCloudDataRequest, new SetCloudDataRequest { key = "k", value = LongValue }, "value",
                InputLimits.MaxValueLength);
            await AssertTooLong(player, WildCatMsgType.GetPublicCloudDataRequest, new StringMessage { value = LongKey }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.GetPlayerPublicCloudDataRequest,
                new GetPlayerPublicCloudDataRequest { player = LongId, key = "k" }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.GetPlayerPublicCloudDataRequest,
                new GetPlayerPublicCloudDataRequest { player = player.PlayerId, key = LongKey }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.GetPlayerNameRequest, new StringMessage { value = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.SetNameRequest, new StringMessage { value = LongName }, "name", Name);
            await AssertTooLong(player, WildCatMsgType.GetPublisherDataRequest, new StringMessage { value = LongKey }, "key", Key);

            // Friends and challenges.
            await AssertTooLong(player, WildCatMsgType.GetFriendsRequest, new GetFriendsRequestMessage { publicKeys = new[] { "k", LongKey } }, "key", Key);
            await AssertTooLong(player, WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = InvitationType.AccountId, value = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = InvitationType.UserId, value = LongId }, "user name", Id);
            await AssertTooLong(player, WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = InvitationType.Email, value = LongEmail }, "email address", InputLimits.MaxEmailLength);
            await AssertTooLong(player, WildCatMsgType.InviteFriendRequest,
                new InviteFriendRequest { type = InvitationType.playerName, value = LongName }, "player name", Name);
            await AssertTooLong(player, WildCatMsgType.AcceptFriendRequest, new StringAndByteArrayMessage { stringValue = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.DenyFriendRequest, new StringAndByteArrayMessage { stringValue = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.StartChallengeRequest, new ChallengeMessage { Player = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.AcceptChallengeRequest, new ChallengeMessage { Player = LongId }, "player id", Id);
            await AssertTooLong(player, WildCatMsgType.DenyChallengeRequest, new ChallengeMessage { Player = LongId }, "player id", Id);

            // Leaderboards, inventory, matchmaking.
            await AssertTooLong(player, WildCatMsgType.GetLeaderboardRequest,
                new GetLeaderBoardRequest { leaderBoardName = LongName, type = GetLeaderBoardRequest.RequestType.Top, count = 5 }, "leaderboard name", Name);
            await AssertTooLong(player, WildCatMsgType.GetInventoryRequest, new StringMessage { value = LongName }, "inventory name", Name);
            await AssertTooLong(player, WildCatMsgType.UnlockInventoryRequest, new StringMessage { value = LongName }, "inventory name", Name);
            await AssertTooLong(player, WildCatMsgType.GetInventoryUnlockTimeRequest, new StringMessage { value = LongName }, "inventory name", Name);
            await AssertTooLong(player, WildCatMsgType.RemoveInventoryItemRequest,
                new RemoveItemInventoryRequestMessage { inventoryName = LongName, items = new[] { ("coin", 1) } }, "inventory name", Name);
            await AssertTooLong(player, WildCatMsgType.RemoveInventoryItemRequest,
                new RemoveItemInventoryRequestMessage { items = new[] { (LongName, 1) } }, "item name", Name);
            await AssertTooLong(player, WildCatMsgType.FindMatchRequest, new FindMatchRequest(LongName, 0, 1), "game mode name", Name);

            // Sign-in: device ids and user names, email addresses.
            string ticket = player.Ticket;
            await AssertTooLong(player, WildCatMsgType.AuthenticateRequest,
                new AuthenticationRequest { ticket = ticket, LoginType = LoginType.Device, Id = LongId, AutoCreate = true }, "device id", Id, false);
            await AssertTooLong(player, WildCatMsgType.AuthenticateRequest,
                new AuthenticationRequest { ticket = ticket, LoginType = LoginType.UserWithPassword, Id = LongId, PasswordHash = "p" }, "user name", Id, false);
            await AssertTooLong(player, WildCatMsgType.AuthenticateRequest,
                new AuthenticationRequest { ticket = ticket, LoginType = LoginType.Email, Id = LongEmail, PasswordHash = "p" }, "email address",
                InputLimits.MaxEmailLength, false);
            await AssertTooLong(player, WildCatMsgType.CreateNewAccountRequest,
                new CreateAccountRequest { ticket = ticket, LoginType = LoginType.UserWithPassword, Id = LongId, PasswordHash = "p" }, "user name", Id, false);
            await AssertTooLong(player, WildCatMsgType.AddAuthMechanismToAccountRequest,
                new AddAuthenticationToAccountRequest { LoginType = LoginType.Device, Id = LongId }, "device id", Id);
            await AssertTooLong(player, WildCatMsgType.RecoverPasswordRequest, new RecoverPasswordRequest { email = LongEmail }, "email address",
                InputLimits.MaxEmailLength);
            await AssertTooLong(player, WildCatMsgType.ResendActivationRequest, new ResendActivationRequest { email = LongEmail }, "email address",
                InputLimits.MaxEmailLength);
            await AssertTooLong(player, WildCatMsgType.EmailActivationRequest,
                new EmailActivationRequest { ticket = ticket, email = LongEmail, code = "ABC123" }, "email address", InputLimits.MaxEmailLength, false);
        }

        [Fact]
        public async Task StringsAtTheLimitAreAccepted()
        {
            var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(GrainFactory);
            await using var host = await GatewayTestHost.StartAsync(cluster.Client);
            var player = await LogIn(host, developerKey, gameKey);
            await using var _ = player.Client;
            string key = new string('k', InputLimits.MaxKeyLength);
            string value = new string('v', InputLimits.MaxValueLength);
            string name = new string('n', InputLimits.MaxNameLength);

            var set = await player.Client.CallAsync<EmptyMessage>(WildCatMsgType.SetCloudDataRequest, new SetCloudDataRequest { key = key, value = value },
                WildCatMsgType.SetCloudDataResponse, player.Ticket);
            Assert.True(set.isSuccesful, set.errorMsg);
            var get = await player.Client.CallAsync<StringMessage>(WildCatMsgType.GetCloudDataRequest, new StringMessage { value = key },
                WildCatMsgType.GetCloudDataResponse, player.Ticket);
            Assert.Equal(value, get.msg.value);

            var rename = await player.Client.CallAsync<EmptyMessage>(WildCatMsgType.SetNameRequest, new StringMessage { value = name },
                WildCatMsgType.SetNameResponse, player.Ticket);
            Assert.True(rename.isSuccesful, rename.errorMsg);
            Assert.Equal(name, await GrainFactory.GetGrain<IPlayer>(player.PlayerId).GetPlayerName());

            var board = await player.Client.CallAsync<GetLeaderBoardResponse>(WildCatMsgType.GetLeaderboardRequest,
                new GetLeaderBoardRequest { leaderBoardName = name, type = GetLeaderBoardRequest.RequestType.Top, count = 5 },
                WildCatMsgType.GetLeaderboardResponse, player.Ticket);
            Assert.True(board.isSuccesful, board.errorMsg);
        }
    }
}
