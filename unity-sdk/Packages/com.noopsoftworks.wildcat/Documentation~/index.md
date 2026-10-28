# WildCat SDK manual

The [package README](../README.md) covers installation and your first client script. This
manual explains the connection lifecycle, threading, feature APIs and configuration.

## Layout

```
package.json, README.md, CHANGELOG.md, LICENSE.md
Runtime/
  Protocol/   asmdef WildCat.Protocol (no engine references). Generated from backend/WildCat.Protocol
              by tools/sync-protocol.sh; never edit it here.
  Core/       asmdef WildCat.Core (no engine references, references WildCat.Protocol). The client:
              WildCatClient, WildCatConnection, the transports, the feature clients.
  Unity/      asmdef WildCat.Unity. WildCatBehaviour, WildCatSettings, UnityWildCatLogger,
              coroutine helpers, device id helpers, and WebGL/ (browser WebSocket transport + .jslib).
Editor/       asmdef WildCat.Editor (Editor only). WildCat menu, About window, settings creation.
Samples~/Basic/  the Basic sample (asmdef WildCat.Samples.Basic), imported from the Package Manager.
Documentation~/  this page.
```

Everything that is not Unity-specific lives in `WildCat.Core`, which compiles as plain .NET
Standard 2.1 with C# 9. You can use the core from .NET tools without loading Unity assemblies.

## Architecture

```
game code --> client.Feature --> WildCatConnection --> ITransport --> WebSocket --> gateway /ws
    ^                                  |
    |   answers, events, relay        | receive loop, writer, heartbeat
    +--- IMainThreadDispatcher <------+ (background threads)
         (WildCatBehaviour pumps it in Update)
```

- `WildCatConnection` owns one socket at a time (a "link"). A single writer task sends queued
  frames (a WebSocket allows one send at a time), a receive loop reads frames, and a heartbeat
  loop sends a heartbeat every `HeartbeatInterval`.
- Requests get a request id from a per-connection counter. The pending request table maps ids to
  waiting Tasks; the answer frame with the same id completes the Task: the payload on success, a
  `WildCatException` for a failed `OptionalResponse` or an ErrorMsg (8), a `TimeoutException`
  when the per-request timer fires first. Answers are matched only by id, never by guessing from
  the payload.
- The send path has limits: a frame larger than `MaxMessageBytes` is refused with
  `ArgumentException` before it reaches the queue (the gateway would close the socket), the queue
  holds at most `MaxQueuedMessages` frames and `MaxQueuedBytes` bytes (then `Relay.Send`
  returns false and requests fail at once with `WildCatErrorCodes.SendQueueFull`; heartbeats and
  the resume are exempt, so a stalled socket still shows up as a missed heartbeat), and the writer
  skips requests that timed out or were cancelled while they waited. A timeout after sending
  does not establish whether the server completed the operation.
- Frames without a request id (and game messages above `HighestInternal`) are pushes: the
  registered handlers for that message type run through the dispatcher, then `MessageReceived`.
- The feature clients are thin: they build the request payload, pick the frame shape the gateway
  expects (ticket in the header, in the payload, or none; game target for LeaveGame) and turn the
  answer into SDK types (`LoginResult`, `MatchInfo`, `FriendList`, `LeaderboardPage` and so on).
- Session state: `Session.InitializeAsync` stores the keys and the ticket; a login replaces the
  ticket with the one it returns and remembers the credentials (in memory) for resuming. Both
  apply their answer on the network thread the moment it arrives, before the completion waits in
  the dispatcher, and only if the socket that carried the request is still the current one and
  the connection is not closing; otherwise the answer is ignored and the call fails with
  `WildCatDisconnectedException`. So a completion the game pumps late cannot bring back a
  session that a reconnection replaced or a password that `DisconnectAsync` forgot.
  `DisconnectAsync`, a final disconnect and `Dispose` clear the keys, the ticket, the player and
  the stored credentials.
- Argument checks (an empty id, a missing session, a value too large) fail the returned Task
  instead of throwing, so `AsCoroutine` and fire-and-forget calls see them like any other error.

### Reconnection

When a link dies without the application asking (server close, network error, missed heartbeat):

1. Every request waiting on that link fails at once with `WildCatDisconnectedException`.
2. With `AutoReconnect` (the default) the state becomes `Reconnecting` and, for attempt 1, 2, 3
   ..., the client raises `Reconnecting` (attempt, delay, reason, last error), waits
   `ReconnectMinDelay * 2^(attempt-1)` capped at `ReconnectMaxDelay`, spread by
   `ReconnectJitter` (plus or minus 20 percent, kept within the limits), and opens a new socket.
3. On the new socket it resumes: `InitializeSession` with the same keys and, if a player was
   logged in, `Authenticate` with the same credentials. Only then is the state `Connected` again
   and `Reconnected` raised. Requests made meanwhile fail with `WildCatDisconnectedException`
   (reason `Reconnecting`); `Relay.Send` returns false.
4. If the server refuses the resume with error code 1 or 2 (the game key is gone, the password
   changed), reconnection stops with `Disconnected` (reason `ResumeFailed`): trying again would
   not change the answer. Any other failure of the resume, a `WildCatException` with code -1 (an
   internal error, for example while a silo restarts) included, drops the new socket and counts
   as a failed attempt. After `MaxReconnectAttempts` failed attempts (0 means no limit) it stops
   with `ReconnectFailed`.

Matches are not rejoined automatically; after `Reconnected`, check `Matches.GetPendingAsync()`
and call `Matches.RejoinAsync(id)`, which answers with `Matches.Resynced`.

### Threading

All network work happens on thread pool threads (`ConfigureAwait(false)` throughout the core).
Everything handed to the application goes through `IMainThreadDispatcher.Post`, in order:
events, push and relay handlers, and the completion of every Task the public API returns
(`ConnectAsync` and `DisconnectAsync` included). Internal requests (heartbeats, the resume after
a reconnection) complete on the network thread, so a busy main thread cannot make a heartbeat
time out.

- `QueuedDispatcher`: a concurrent queue that `Pump()` drains on the calling thread.
  `WildCatBehaviour` pumps it in `Update` (optionally limited per frame).
- `InlineDispatcher`: runs callbacks at once on the network thread; the default outside Unity.
- Handlers that throw are logged and do not stop the connection or other handlers.

Never block the main thread on an SDK Task: the completion is queued for the next `Update`.

## API

`WildCatClient(options, dispatcher, transportFactory, logger)` creates everything; in a scene
`WildCatBehaviour.Client` does it from a `WildCatSettings` asset. Its members:

| Member | What it does |
|---|---|
| `ConnectAsync`, `DisconnectAsync`, `Dispose` | open; close with a handshake; drop without events |
| `IsConnected`, `IsLoggedIn` | state shortcuts |
| `Connected`, `Disconnected`, `Reconnecting`, `Reconnected` | connection events |
| `Connection` | `State`, `RoundTripTime`, `SmoothedRoundTripTime`, `RequestAsync<T>` (any request), `Send(frame)`, `RegisterHandler(type, handler)`, `MessageReceived` |
| `Session` | `InitializeAsync(developerKey, gameKey)`, `Ticket`, `IsInitialized` |
| `Auth` | `LoginWithDeviceAsync`, `LoginWithUserNameAsync`, `LoginWithEmailAsync`, `CreateAccountWith{Device,UserName,Email}Async`, `AddUserNameAsync`, `AddEmailAsync`, `ActivateEmailAsync`, `ResendActivationCodeAsync`, `RecoverPasswordAsync`, `ResetPasswordAsync`, `PlayerId`, `PlayerName` |
| `Profile` | `SetNameAsync`, `GetNameAsync`, `GetPlayerNameAsync` |
| `CloudData` | `SetAsync`/`GetAsync` (private), `SetPublicAsync`/`GetPublicAsync`, `GetPlayerPublicAsync`, `GetPublisherDataAsync` |
| `Friends` | `GetAsync`, `InviteByPlayerIdAsync`/`ByUserNameAsync`/`ByEmailAsync`, `AcceptAsync`, `DenyAsync`; events `InvitationReceived`, `InvitationAccepted`, `InvitationDenied`, `StatusChanged` (`InviteByDisplayNameAsync` is obsolete: the server cannot find players by display name, so it always fails) |
| `Challenges` | `SendAsync`, `AcceptAsync`, `DenyAsync`, `GetAsync`; events `ChallengeReceived`, `ChallengeAccepted`, `ChallengeDenied` |
| `Matchmaking` | `FindMatchAsync(gameMode, skill, maxWait)`: a `MatchInfo`, or null when nobody was found. The server caps `maxWait` at 20 s; cancelling stops the wait, not the server's search (check `Matches.GetPendingAsync` afterwards) |
| `Matches` | `LeaveAsync`, `SetPlayerPropertyAsync`, `SetGlobalPropertyAsync`, `RejoinAsync`, `GetPendingAsync`; events `PlayerJoined`, `PlayerLeft`, `PlayerRejoined`, `Resynced`, `PropertyChanged`, `Finished` |
| `Relay` | `Send<T>(matchId, type, message)`, `SendRaw`, `On<T>(type, handler)` (optionally with the match id), `MessageType(n)`, `IsGameMessage` |
| `Leaderboards` | `GetTopAsync`, `GetAroundMeAsync`, `GetRankAsync` (no score setting from clients) |
| `Inventory` | `GetAsync`, `RemoveAsync`, `UnlockAsync`, `GetUnlockTimeAsync` (no grants from clients) |
| `Scripts` | `ExecuteAsync(name, args)` returning a `ScriptResult`, `ExecuteAsync<T>(name, args)` |
| `ServerTime` | `GetAsync()`, then `Now`, `Offset`, `IsSynchronized` |

Script event arguments and results are int, short, long, bool, byte, byte[], float, double or
string. Relay payloads are `MessageBase` subclasses that write their fields in
`Serialize(WireWriter)` and read them in `Deserialize(WireReader)`.

The calls throw `ArgumentException`, without sending anything, for input the server would refuse
(`WireLimits` in `Runtime/Protocol`): player ids and user names over 128 characters, email
addresses over 254, display names and leaderboard, inventory, item, game mode and script event
names over 128, cloud data keys over 100 and values over 65,536 characters, more than 64 public
data keys in `Friends.GetAsync`, 64 script arguments or 256 items to remove. Inviting by user name
or email address succeeds whether or not an account has it; only an unknown player id fails.

### Options

| `WildCatClientOptions` | Default | |
|---|---|---|
| `ServerUri` | `ws://localhost:5000/ws` | ws:// or wss:// |
| `ConnectTimeout` | 10 s | |
| `RequestTimeout` | 15 s | per request; FindMatch adds its wait |
| `HeartbeatInterval` / `HeartbeatTimeout` | 10 s / 15 s | zero interval turns heartbeats off |
| `AutoReconnect`, `ResumeSession` | true, true | |
| `ReconnectMinDelay`, `ReconnectMaxDelay`, `ReconnectJitter`, `MaxReconnectAttempts` | 1 s, 30 s, 0.2, 0 (no limit) | |
| `AllowPlainTextPasswordsWithoutTls` | false | passwords only over wss:// or to localhost |
| `MaxMessageBytes` | 1 MiB | keep equal to the gateway's `Gateway:MaxMessageBytes`; a bigger message is not sent (ArgumentException), and one from the server drops the socket |
| `MaxQueuedMessages`, `MaxQueuedBytes` | 1024, 8 MiB | send queue limits; at least 1 and at least `MaxMessageBytes` |

`WildCatBehaviour` has its own inspector options: the `WildCatSettings` asset, Dont Destroy On
Load (on), Run In Background (off; sets `Application.runInBackground` in Awake so the game, and
the SDK's callbacks, keep running while the window is not focused), Connect On Start (off) and
Max Callbacks Per Frame (0, no limit). It disposes the client in `OnDestroy` and pumps once more
so awaited calls fail instead of waiting forever.

### Transports

`ITransport` is one WebSocket carrying binary messages. `ClientWebSocketTransport`
(System.Net.WebSockets.ClientWebSocket) serves the editor and desktop and mobile players;
`WebGLWebSocketTransport` plus `Runtime/Unity/WebGL/WildCatWebSocket.jslib` use the browser's
WebSocket in WebGL players, and `WildCatBehaviour` picks it there. Custom transports (a test
double, a proxy) plug in through `ITransportFactory`.

## Platform support

The SDK targets Unity 6.6. The Editor and desktop/mobile transport uses `ClientWebSocket`;
Web uses the browser WebSocket through the supplied `.jslib` bridge. `WildCatBehaviour` drives
connection deadlines, heartbeats and reconnection from `Update` through
`PumpedConnectionScheduler`, and keeps continuations on Unity's synchronization context.

Managed threads and timers are unavailable on the Web platform. When constructing a client
without `WildCatBehaviour`, pass a `PumpedConnectionScheduler` and call its `Pump()` on the
main thread every frame, before pumping the dispatcher. A paused or throttled browser tab can
delay both callbacks and timeouts; reconnect when it resumes if the server closed the socket.

The browser integration scene in the SDK project covers the connection and relay flows in
Chrome. Other browsers and mobile IL2CPP runtime support still need target-device testing.
Before shipping, check connection, message delivery, timeouts, heartbeats and reconnection in
each target browser or player.

## Known limitations

- `Friends.InviteByDisplayNameAsync` always fails: the server's map from display names to
  players is never filled.
