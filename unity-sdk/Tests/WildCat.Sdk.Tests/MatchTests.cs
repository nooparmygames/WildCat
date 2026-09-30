using System;
using System.Linq;
using System.Threading.Tasks;
using WildCat.Protocol;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>Matchmaking, relayed game messages and match properties with two real clients.</summary>
    [Collection(ServerCollection.Name)]
    public class MatchTests : ServerTestBase
    {
        private static readonly WildCatMsgType Position = RelayClient.MessageType(1);
        private static readonly WildCatMsgType Chat = RelayClient.MessageType(2);

        public MatchTests(ServerFixture server)
            : base(server)
        {
        }

        /// <summary>A game payload like the sample's position message.</summary>
        public sealed class PositionMessage : MessageBase
        {
            public int Sequence;
            public float X;
            public float Y;

            public override void Serialize(WireWriter writer)
            {
                writer.WriteInt32(Sequence);
                writer.WriteSingle(X);
                writer.WriteSingle(Y);
            }

            public override void Deserialize(WireReader reader)
            {
                Sequence = reader.ReadInt32();
                X = reader.ReadSingle();
                Y = reader.ReadSingle();
            }
        }

        private async Task<(WildCatClient Alice, WildCatClient Bob, LoginResult AliceLogin, LoginResult BobLogin, MatchInfo AliceMatch, MatchInfo BobMatch)> StartMatchAsync()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (alice, aliceLogin) = await LoggedInClientAsync(developerKey, gameKey);
            var (bob, bobLogin) = await LoggedInClientAsync(developerKey, gameKey);
            string mode = Unique.Id("duel");

            Task<MatchInfo> aliceSearch = alice.Matchmaking.FindMatchAsync(mode, 100, TimeSpan.FromSeconds(10));
            Task<MatchInfo> bobSearch = bob.Matchmaking.FindMatchAsync(mode, 100, TimeSpan.FromSeconds(10));
            MatchInfo aliceMatch = await Wait.For(aliceSearch);
            MatchInfo bobMatch = await Wait.For(bobSearch);
            Assert.NotNull(aliceMatch);
            Assert.NotNull(bobMatch);
            return (alice, bob, aliceLogin, bobLogin, aliceMatch, bobMatch);
        }

        [Fact]
        public async Task FindMatchPutsTwoClientsInOneMatchAndRelaysTheirMessagesInOrder()
        {
            var (alice, bob, aliceLogin, bobLogin, aliceMatch, bobMatch) = await StartMatchAsync();
            Assert.Equal(aliceMatch.Id, bobMatch.Id);
            Assert.Contains(aliceLogin.PlayerId, aliceMatch.PlayerIds);
            Assert.Contains(bobLogin.PlayerId, aliceMatch.PlayerIds);

            var received = new Recorder<(PositionMessage Message, Guid Match)>();
            using IDisposable subscription = bob.Relay.On<PositionMessage>(Position, (message, match) => received.Add((message, match)));

            for (int i = 0; i < 100; ++i)
                Assert.True(alice.Relay.Send(aliceMatch.Id, Position, new PositionMessage { Sequence = i, X = i * 0.5f, Y = -i }));

            await Wait.UntilAsync(() => received.Count == 100, "100 relayed positions");
            var items = received.Items;
            for (int i = 0; i < 100; ++i)
            {
                Assert.Equal(i, items[i].Message.Sequence);
                Assert.Equal(i * 0.5f, items[i].Message.X);
                Assert.Equal(aliceMatch.Id, items[i].Match);
            }

            // The other way, raw bytes, and a handler that is removed gets nothing more.
            var chat = new Recorder<byte[]>();
            IDisposable chatHandler = alice.Relay.On<RawMessage>(Chat, message => chat.Add(message.value));
            Assert.True(bob.Relay.SendRaw(bobMatch.Id, Chat, new byte[] { 1, 2, 3 }));
            byte[] bytes = await chat.WaitForAnyAsync("a relayed chat message");
            Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
            chatHandler.Dispose();
            subscription.Dispose();
            Assert.True(bob.Relay.SendRaw(bobMatch.Id, Chat, new byte[] { 4 }));
            Assert.True(alice.Relay.Send(aliceMatch.Id, Position, new PositionMessage { Sequence = 100 }));
            await Task.Delay(300);
            Assert.Equal(1, chat.Count);
            Assert.Equal(100, received.Count);
        }

        [Fact]
        public async Task MatchPropertiesAndLeavingRaiseTheMatchEvents()
        {
            var (alice, bob, _, _, aliceMatch, _) = await StartMatchAsync();
            var bobChanges = new Recorder<MatchPropertyChange>();
            var aliceChanges = new Recorder<MatchPropertyChange>();
            var finished = new Recorder<MatchFinished>();
            bob.Matches.PropertyChanged += bobChanges.Add;
            alice.Matches.PropertyChanged += aliceChanges.Add;
            bob.Matches.Finished += finished.Add;

            await Wait.For(alice.Matches.SetGlobalPropertyAsync(aliceMatch.Id, "round", new IntegerMessage { value = 3 }));
            var global = await bobChanges.WaitForAsync(c => c.Key == "round", "the global property on Bob's side");
            Assert.True(global.IsGlobal);
            Assert.Equal(3, global.GetValue<IntegerMessage>().value);
            await aliceChanges.WaitForAsync(c => c.Key == "round", "the global property on Alice's side too");

            // Player properties are not tested here: the server's RelayServerGameState.SetPlayerProperties
            // replaces the whole property table instead of adding the player's entry and then fails
            // with a KeyNotFoundException, so SetPlayerProperty (129) always answers code -1 for now.

            await Assert.ThrowsAsync<ArgumentException>(() => alice.Matches.SetGlobalPropertyAsync(aliceMatch.Id, "big", new byte[MatchesClient.MaxPropertyBytes + 1]));

            await Wait.For(alice.Matches.LeaveAsync(aliceMatch.Id));
            await Wait.For(bob.Matches.LeaveAsync(aliceMatch.Id));
            MatchFinished end = await finished.WaitForAnyAsync("the match to finish");
            Assert.Equal(aliceMatch.Id, end.MatchId);
            Assert.False(end.ByDisconnect);
        }

        [Fact]
        public async Task FindMatchReturnsNullWhenNobodyElseIsLooking()
        {
            var (developerKey, gameKey) = await Server.CreateGameAsync();
            var (client, _) = await LoggedInClientAsync(developerKey, gameKey);

            MatchInfo match = await Wait.For(client.Matchmaking.FindMatchAsync(Unique.Id("lonely"), 0, TimeSpan.FromSeconds(1)));

            Assert.Null(match);
            Assert.Empty(await Wait.For(client.Matches.GetPendingAsync()));
        }

        [Fact]
        public void RelayOnlyTakesGameMessageTypes()
        {
            Assert.Equal((WildCatMsgType)1024, RelayClient.MessageType(1));
            Assert.True(RelayClient.IsGameMessage(RelayClient.MessageType(1)));
            Assert.False(RelayClient.IsGameMessage(WildCatMsgType.HighestInternal));
            Assert.Throws<ArgumentOutOfRangeException>(() => RelayClient.MessageType(0));

            WildCatClient client = NewClient();
            Assert.Throws<ArgumentOutOfRangeException>(() => client.Relay.Send(Guid.NewGuid(), WildCatMsgType.FindMatchRequest, new EmptyMessage()));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.Relay.On<EmptyMessage>(WildCatMsgType.GetNameResponse, _ => { }));
            // Not connected: nothing is queued.
            Assert.False(client.Relay.Send(Guid.NewGuid(), Position, new PositionMessage()));
        }
    }
}
