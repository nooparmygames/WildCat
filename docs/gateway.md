# The WebSocket gateway

`backend/WildCat.Gateway` is the WebSocket front door of the host: clients connect to `/ws`, send
one binary frame per message (see `docs/wire-protocol.md`) and get answers and pushed events
back. It runs inside the silo process and calls grains through the silo's `IClusterClient`.
Roadmap step 6 split the old one-class gateway (`FrontEndServer`) into a transport, a dispatcher
and one handler per message. This page explains the parts and how to add a handler.

## Layout

```
WildCat.Gateway/
  GatewayExtensions.cs     AddGameGateway (services, options, handler discovery), MapGameGateway (/ws)
  GameGatewayOptions.cs    the "Gateway" configuration section
  TokenBucket.cs           the per-connection sign-in allowance
  RelayFrame.cs            the copy of a game message that is relayed (no ticket)
  Transport/
    WebSocketConnection.cs receive loop, bounded outbound queue, single writer, closing
  Connections/
    GatewayConnection.cs   per-socket state: transport, login, allowance, queues
    ConnectionRegistry.cs  connection id -> connection, player id -> connection (this node)
    LoginManager.cs        binds a login to a connection, releases it on disconnect
  Push/
    PushSubscriptions.cs   the player's PlayerConnection, ChallengeEvents and FriendEvents streams
    ChallengeEventFrames.cs, FriendEventFrames.cs   stream event -> frame
  Dispatch/
    MessageDispatcher.cs   per frame: decode, route, sign-in limit, session check, run, answer errors
    HandlerRegistry.cs     request type -> handler, built at startup
    IMessageHandler.cs, RequestHandler.cs, MessageRoute.cs, SessionRequirement.cs
    MessageContext.cs      what a handler gets
    RequestFailedException.cs, GatewayErrors.cs
  Handlers/
    Session/               InitializeSession (6), HeartBeat (100), GetServerTime (42)
    Auth/                  Authenticate (76), CreateNewAccount (78), EmailActivation (80),
                           AddAuthMechanismToAccount (82), RecoverPassword (84),
                           ResendActivation (86), ResetPasswordWithCode (163), SignIn.cs (shared)
    Matches/               FindMatch (15), LeaveGame (13), SetMatchProperty (129),
                           GetPlayerPendingMatches (131), RejoinMatch (133), Matches.cs (shared)
    Leaderboards/          GetLeaderboard (26)
    Inventory/             GetInventory (109), RemoveInventoryItem (115), UnlockInventory (119),
                           GetInventoryUnlockTime (121), Inventories.cs (shared)
    Refused/               SetLeaderboard (28), AddItemToInventory (111), SetItemToInventory (113):
                           always error 1, grants and scores come from server scripts
    Friends/               GetFriends (88), InviteFriend (90), AcceptFriend (93), DenyFriend (96),
                           Friends.cs (shared, also finds accounts for the challenge handlers)
    Challenges/            StartChallenge (17), AcceptChallenge (20), DenyChallenge (23),
                           GetPlayerChallenges (161), Challenges.cs (shared)
```

## What happens to a frame

1. `WebSocketConnection` reads a whole binary message (fragments are joined up to
   `Gateway:MaxMessageBytes`; a bigger one closes the socket with MessageTooBig) and awaits the
   dispatcher's `OnFrameAsync` for it. Frames are handed on one at a time, in arrival order.
2. The dispatcher decodes it with `WireCodec.Decode`. A frame that does not decode gets an
   ErrorMsg (8) with code 1, "The message could not be read."
3. Game messages (ids above `HighestInternal`, 1023) go to the connection's relay queue. One relay
   worker per connection checks the session, and passes each one on to the other players of the
   match in its game target, in order, without the ticket.
4. For any other id the dispatcher looks up the route. No route: ErrorMsg code 1, "Unsupported
   message type". A route that is a sign-in attempt takes a token from the connection's allowance
   first, still in arrival order; without one the request is answered on its own response type
   with code 1, "Too many attempts, try again later".
5. The request then takes one of the connection's request slots (`Gateway:MaxConcurrentRequests`,
   8 by default; the receive loop waits for a free one) and runs on its own: the dispatcher checks
   the session the route requires, builds a `MessageContext` and awaits the handler inside a
   try/catch. So a slow request (FindMatch) does not hold up a heartbeat, but two requests of one
   connection can finish in either order.
6. Everything sent to the client, answers, pushes and relayed frames alike, is encoded and put in
   the connection's bounded outbound queue (`Gateway:OutboundQueueCapacity`, 1024 frames), which
   one writer task drains. Nothing waits for the socket. When a client stops reading and the queue
   fills up, the connection is closed with PolicyViolation (after 5 seconds without a close
   handshake it is aborted) and a warning is logged.
7. When the socket closes, however it closes, `LoginManager.ReleaseAsync` removes the connection,
   drops its stream subscriptions (if it still owns them) and calls `IPlayer.Disconnected` with the
   ticket of the login it held.

## Session requirements

Each route says what the dispatcher checks before the handler runs (`SessionRequirement`):

| Requirement | Checks | Context |
|---|---|---|
| `None` | nothing; a ticket in the header is ignored | `Session`, `Player` null |
| `Session` | header ticket is a Guid of a live session (touched, which keeps it alive) | `Session` set; `Player` set if someone is logged in |
| `Player` (default) | as `Session`, and a player is logged in on it | `Session` and `Player` set |

A refusal is an ErrorMsg (8) with the request id and code 1: "This message needs a session
ticket.", "The session ticket is not valid.", the session exceptions' texts (unknown, ended or
expired session), or "Log in before sending this message.". Code -1, "The session could not be
checked. Try again.", means the session grain failed. The dispatcher also pings the player
grain's heartbeat at most every 5 seconds per connection when a request passes the check.

## Errors a handler produces

- Return normally: the dispatcher's `RequestHandler` base answers with a successful
  `OptionalResponse<TResponse>` on the response type.
- `throw new RequestFailedException(code, message)`: answered with that code and message. Use it
  for every expected failure (wrong password, already exists, not allowed). Codes: 1 refused or
  failed (`GatewayErrors.Refused`), 2 already exists (`GatewayErrors.AlreadyExists`), others only
  when the message's documentation says so.
- The game's own domain exceptions from `WildCat.Interfaces` (session not initialized or expired,
  invalid game, game not running, challenge timeout) are answered with code 1 and their message.
- A payload that does not decode (`WireFormatException`) is answered with code 1, "The message
  could not be read."
- Anything else is a bug: logged at Error with the stack trace and answered with code -1,
  "Internal error". Do not put exception messages from grains in answers unless you know they
  are written for clients; translate the ones you expect into `RequestFailedException` (see
  `SignIn.WithInputErrorsAsync`, which does that for the ArgumentExceptions the Player grain
  throws for bad input).

## Adding a handler

One class per request type, in `Handlers/<Group>/<Name>Handler.cs`, deriving from
`RequestHandler<TRequest, TResponse>`:

```csharp
namespace WildCat.Gateway
{
    /// <summary>GetName (36 -> 37): the logged in player's name.</summary>
    internal sealed class GetNameHandler : RequestHandler<EmptyMessage, StringMessage>
    {
        public override WildCatMsgType RequestType => WildCatMsgType.GetNameRequest;
        public override WildCatMsgType ResponseType => WildCatMsgType.GetNameResponse;
        // public override SessionRequirement Session => SessionRequirement.Player;   (the default)
        // public override bool IsSignInAttempt => false;                            (the default)

        protected override async Task<StringMessage> HandleAsync(MessageContext context, EmptyMessage request)
        {
            return new StringMessage { value = await context.Player.GetPlayerName() };
        }
    }
}
```

- Registration is automatic: `AddGameGateway` registers every non-abstract `IMessageHandler` in
  the assembly as a singleton, so there is no list to edit. Two handlers for one type, or a
  handler for a game message id, fail at startup. Handlers are shared by all connections: keep
  no per-request state in fields.
- Dependencies come through the constructor: `LoginManager`, `ConnectionRegistry`,
  `PushSubscriptions`, `TimeProvider`, `ILogger<T>`, `IOptions<GameGatewayOptions>` or anything
  else the host registers. Grain calls go through `context.Grains`.
- `context.Player`, `context.Session` (developer key, game key, user id) and `context.Ticket`
  are what the dispatcher checked; scope every grain key by `context.Session.GameKey` (or
  `context.Connection.GameKey`, the game of the login) the way the old code did.
- A handler for several types, or one that answers with something other than a single
  OptionalResponse, implements `IMessageHandler` directly: list its `MessageRoute`s and use
  `context.ReadPayload<T>()`, `context.Reply(payload)`, `context.ReplyFailure(code, text)` and
  `context.Send(frame)`.
- Never block (`.Result`, `.Wait()`) and never wait on a socket; `context.Send` and
  `GatewayConnection.TrySend` only queue.
- Every request type the old `FrontEndServer` handled has its own handler since roadmap step 6b;
  the temporary adapter that served the rest in between is gone. A request type without a
  handler is answered with "Unsupported message type".

## Pushing to clients

- To the connection of the request: `context.Send(WildCatMessageBase.Create(payload, type,
  WildCatMsgHeader.normal))`.
- To a player logged in on this node: `ConnectionRegistry.SendToPlayer(playerId, WireCodec.Encode(frame))`.
- From a grain (any node): `IPlayer.SendDataToConnection(player, channel, encodedFrame)` publishes
  on the player's PlayerConnection stream (awaited, so frames one caller sends in turn stay in
  order); `PushSubscriptions` queues it on the player's socket. RelayServerGame sends its match
  events (9, 10, 125-128) and relayed game messages this way. Answers never take this path (the
  legacy FindMatch and LeaveGame answers did), so an answer can arrive before the match events
  its own request caused.
- Friend and challenge events arrive on the FriendEvents and ChallengeEvents streams and become
  frames in `Push/FriendEventFrames.cs` and `Push/ChallengeEventFrames.cs`.

## Testing a handler

End-to-end tests go through the real gateway on Kestrel with the shared test cluster
(`WildCat.Tests/GatewayTestHost.cs`):

```csharp
[Collection(ClusterCollection.Name)]
public class GatewayProfileHandlerTests
{
    private readonly InProcessTestCluster cluster;
    public GatewayProfileHandlerTests(ClusterFixture fixture) { cluster = fixture.Cluster; }

    [Fact]
    public async Task GetNameAnswersTheName()
    {
        var (developerKey, gameKey) = await TestSetup.CreateDeveloperAndGame(cluster.Client);
        await using var host = await GatewayTestHost.StartAsync(cluster.Client);
        await using var client = await host.ConnectAsync();
        var session = await client.InitializeSessionAsync(developerKey, gameKey);
        var login = await client.LoginWithDeviceAsync(session, TestSetup.UniqueId("device"));
        await cluster.Client.GetGrain<IPlayer>(login.userId).SetPlayerName("Ada");

        var name = await client.CallAsync<StringMessage>(WildCatMsgType.GetNameRequest, new EmptyMessage(),
            WildCatMsgType.GetNameResponse, session.ToString());

        Assert.True(name.isSuccesful, name.errorMsg);
        Assert.Equal("Ada", name.msg.value);
    }
}
```

- `CallAsync<T>(type, payload, responseType, ticket)` sends a request with a new request id (and
  the ticket in the header when given), waits up to 15 seconds for the frame echoing the id,
  checks its type and that the header holds only the request id, and decodes the
  OptionalResponse. `RequestAsync` returns the raw frame instead.
- `WaitForTypeAsync(type)` waits for a pushed frame; `Received` lists every frame so far.
- `GatewayTestHost.StartAsync(client, configure, timeProvider, configureServices)` changes
  options (for example a small `AuthAttemptBurst`), swaps the clock (`ManualTimeProvider`) or
  adds test handlers; `host.Connections` is the registry and `host.Logs` every log entry.
- Use unique keys (`TestSetup.UniqueId`, a new developer and game per test) and bounded waits
  (`TestSetup.WaitUntilAsync`); never sleep without a limit.
- Test the success path, each error code the handler can answer with, the session requirement
  (an ErrorMsg without a ticket or login), and the sign-in limit for sign-in attempts.
