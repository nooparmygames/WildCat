# Unity SDK notes

`unity-sdk/` is the `Assets/Backend` folder of the original Unity 2020.3.18 project plus its
ProjectSettings and a trimmed package manifest. The vendored PlayFab SDK, the basketball art,
the DOTS packages and the broken prefabs were not copied. Surveyed 2026-09-30.

Paths and names follow the WildCat rename from roadmap step 3: the SDK code uses the namespace
`WildCat` (was `Backend`) and its protocol copy `WildCat.Protocol`, while the `Assets/Backend`
folder layout and the `BackendPlatform` class name stay until the step 8 rewrite.

Edits made during the copy: the three `PlayfabErrorCode` properties in `Result.cs`, the
`AuthenticateWithPlayfab` method and the two `Debug.Log` lines that printed the developer secret
were removed from `BackendPlatform.cs`; the sample's public server IP became `localhost`; the
developer and game key GUIDs in `Sample/test.unity` were zeroed; the hardcoded game id in
`Client.cs` became an empty string. Nothing else changed at copy time, so every bug below is
still present.

Removed in step 2: `VerifyCafeReceipt` with `CafeVerificationStatusResults`; the card-game calls
`BuyUBCItem`, `SetDeck`, `ClaimQuests`, `RegisterTournament`, `SetAchievementProgress`,
`SetQuestProgress` and `SetMatchResult` with their result tables, handlers and `Client` senders
(plus the unused login, quest, achievement and main data senders); the developer-only
`DevGetPublisherData` and `DevSetPublisherData`, which sent the developer secret from the
client; and the `ENABLE_PLAYFABSERVER_API` scripting define. The SDK's `Messages.cs` lost
the same message types and enum members as the server copy and pins the same explicit ids
(see docs/wire-protocol.md).

Changed in step 4: passwords go to the server in plain text (the server stores a salted PBKDF2
hash), so production builds must connect over wss://; `AuthenticateExtensions.cs` with `ToMd5`
and `ToSha1` is gone. `AddInventoryItems` and `SetScoreInLeaderBoard` are `[Obsolete]` because
the server now rejects messages 111 and 28 from clients with error code 1; grant items and set
scores from a server script event run through `ExecuteScriptEvent`. The SDK never had a call
for 113 (SetItemToInventory). The sample's "Send score" button still calls
`SetScoreInLeaderBoard`, so it gets that error and an obsolete warning.

Changed in step 6: the protocol is no longer a hand-kept copy. `LowLevelAPI/Messages.cs` is gone;
`Plugins/Protocol/` is generated from `backend/WildCat.Protocol` by `tools/sync-protocol.sh` and
must not be edited (see docs/wire-protocol.md). Encoding goes through `WireCodec`: frames are sent
as `WireCodec.Encode(msg)` (was `msg.ToByteArray()`) and read with `WireCodec.Decode(bytes)`
(was `WildCatMessageBase.CreateMessageFromByteArray`), and payloads are read with
`msg.GetPayload<T>()` (was `GetData<T>()`). Custom payloads, like the sample's `Vector3Message`,
override `Serialize(WireWriter)` and `Deserialize(WireReader)` (they used `BinaryWriter`,
`BinaryReader` and the `WriteBytesFull` extension). The wire format changed with it (UTC tick
DateTimes, int32 counts, null-safe strings), so an SDK and a server from before and after step 6
cannot talk to each other. `GetServerTime` reads a UTC `DateTime` from a `DateTimeMessage`
instead of parsing a culture dependent string, and `SetPlayerMatchProperty(Guid, string,
MessageBase)` encodes its value through the codec instead of calling itself.

## Layout

```
Assets/Backend/Editor/Scripts/      WildCatWindow (about box), WildCatMenus (docs links, "Create Clone"
                                    symlink helper for running two editors), OneTimeRunner (dead)
Assets/Backend/Plugins/BackendPlatform.cs   the public API, about 2,000 lines, singleton MonoBehaviour
Assets/Backend/Plugins/Result.cs            Result, Result<T>, Result<T,Q>: poll or `yield return r.Wait()`
Assets/Backend/Plugins/LowLevelAPI/         Client (request/response plumbing), WebSocketNetworkTransmitter
                                            (websocket-sharp), NetworkTransmitter (dead UNet LLAPI),
                                            RequestIdGenerator
Assets/Backend/Plugins/Protocol/            generated copy of backend/WildCat.Protocol (do not edit)
Assets/Backend/Plugins/Sample/              Sample.cs IMGUI demo, TimestampedBuffer (interpolation), test.unity
Assets/Backend/Plugins/Prefabs/WildCat.prefab   references a missing script GUID (broken)
```

There is no asmdef. `UnityEngine.Networking` types (`QosType`, `NetworkError`) from the
deprecated UNet package are still referenced. websocket-sharp does not work on WebGL.

## Public API (BackendPlatform)

Results: every call returns a `Result` object with `isComplete`, `isSuccessful`, `errorCode`,
`errorMessage`, `value`. `Wait()` returns a coroutine. No Tasks, no timeouts, callback
parameters on some overloads are ignored. Pending results complete on the main thread from
`Client.Update`; on disconnect all pending results fail with "Disconnected". Server pushes arrive
as C# events or public delegate fields.

- Connection: `Connect(url, port)` (port ignored, url is a full ws:// URL), `ConnectLocally()`
  (ws://localhost:5000/ws), `Disconnect()`, `InitializeSession(developerKey, gameKey)`, events
  `OnConnected`, `OnConnectionFailed`, field `OnDisconnected`.
- Auth: `AuthenticateWithTestDevice`, `AuthenticateWithDevice` (uses
  `SystemInfo.deviceUniqueIdentifier`), `AuthenticateWithUserAndPassword`,
  `AuthenticateWithEmail`, `CreateAccountWith*`, `AddUserNameAndPasswordToAccount`,
  `AddEmailAndPasswordToAccount`, `ActivateEmail`. Passwords are sent in plain text (MD5-hashed
  client side before step 4), so use wss:// in production.
- Profile: `SetName`, `GetName`, `GetPlayerName(playerId)`.
- Friends: `GetFriends(publicDataKeys)`, `InviteFriendByUserName/Email/PlayerId`,
  `AcceptFriendInvitation`, `DenyFriendInvitation`, events `OnFriendListReceived`,
  `OnFriendshipInvitation`, `OnFriendshipAccepted`, `OnFriendshipDenied`,
  `OnFriendStatusChanged`.
- Inventory: `GetInventory`, `RemoveInventoryItems`, and the obsolete `AddInventoryItems`
  (rejected by the server since step 4).
- Leaderboards: the obsolete `SetScoreInLeaderBoard` (rejected since step 4),
  `GetRankInLeaderBoard`, `GetAroundPlayersInLeaderBoard`, `GetTopPlayersInLeaderBoard`.
- Matchmaking and matches: `FindMatch(gameMode, skill)` (15 s timeout, error 1),
  `FinishGame(game)`, `SetPlayerMatchProperty`, `SetGlobalMatchProperty` (10 KB values),
  `RejoinMatch`, `GetPendingMatches`, events `OnGameFinished`, `OnGameFinishedDueToDisconnect`,
  `OnMatchPropertyChanged`, `OnPlayerInMatchPropertyChanged`, fields `OnMatchPlayerRemoved`,
  `OnMatchPlayerRejoined`, `OnMatchRejoinSyncDataReceived`.
- Relay: `UpdateGame<T>(game, msg, type, qos)` fire-and-forget with custom message types above
  1023; `RegisterHandler(type, handler)` / `UnregisterHandler` for raw messages, payload via
  `msg.GetPayload<T>()`.
- Challenges: `SendChallengeRequest`, `AcceptChallengeRequest`, `DenyChallengeRequest`,
  `GetChallenges` (161/162), events
  `OnchallengeRequestReceived`, `OnchallengeStarted`, `OnchallengeDenied`.
- Cloud data: `SaveCloudData`, `SavePublicCloudData`, `GetCloudData`, `GetPublicCloudData`,
  `GetPlayerPublicCloudData`, `GetPublisherData`.
- Scripts: `ExecuteScriptEvent(name, params object[] args)` for int, short, long, bool, byte,
  byte[], float, double, string.
- Time: `GetServerTime()` (UTC; sends the ticket, which the server does not check).

## Transport

`Client : WebSocketNetworkTransmitter` wraps websocket-sharp. Events arrive on worker threads;
`DataEvent` parses on the worker thread, looks up the handler dictionary without a lock and
pushes to a plain `Queue` without a lock (thread-safety bug); `Update` drains it on the main
thread. QoS channels are accepted and ignored (everything is reliable, ordered TCP).
`OnError` is a no-op, so websocket failures surface as `OnDisconnected` only. Heartbeat: a
`HeartBeatRequest` after 10 s without sends; no RTT. No reconnection. Request ids come from a
static counter with a small race outside its lock.

## Bugs to fix before anyone uses it

- `Client.Disconnect()` clears the handler table including the internal response handlers, so
  after an explicit disconnect and reconnect no response is ever processed. `OnDisconnected`
  fires twice.
- `DenyChallengeRequest` registers its response handler on the request type, so its Result
  never completes. (`SetDeck` had the same bug until step 2 removed it.)
- `AddInventoryItems` and `RemoveInventoryItems` never set `inventoryName` and throw on the
  null write.
- `CreateAccountWithUserAndPassword` sends the `userName` property instead of the argument.
- Leaderboard responses are identified by guessing from flags; a Top query on an empty board
  never completes.
- Fixed in step 6: `GetFriendsRequestMessage.Deserialize` never allocated its array, the
  player-target count was int32 vs int16, and `SetPlayerMatchProperty(Guid, string,
  MessageBase)` called itself (see docs/wire-protocol.md). The deserializers with null lists
  belonged to card-game messages removed in step 2.
- There is no handler for ErrorMsg (8), so a request the gateway refuses before its handler
  (no session, not logged in, unknown id) leaves its Result pending, and no calls exist yet for
  RecoverPassword (84), ResendActivation (86) and ResetPasswordWithCode (163).
- Sample: leaderboard never displays (`entriesToShow` unassigned), `int.Parse` on empty input,
  null reference in `OnFriendStatusChanged` before the list loads, callbacks re-registered on
  every Start, `TimestampedBuffer.GetValue` divides by zero on equal timestamps, sends at 20 Hz
  while the comment says 10.
- `WildCat.prefab` and `test.unity` reference a script GUID that no longer exists.

## Cleanup plan

1. Add an asmdef, remove the UNet types (own `Qos` enum), delete `NetworkTransmitter.cs`,
   `Singleton.cs`, `OneTimeRunner.cs`. (The UBC/quest/league/deck/"Old" messages, the dev
   publisher-data calls and the CafeBazaar code went in step 2.)
2. Replace the polling `Result` with `Task`-based or `UniTask`-style calls with timeouts, keep a
   coroutine-friendly wrapper.
3. Make the transport thread-safe (concurrent queue, immutable handler map), add reconnection
   with session resume, and a real heartbeat with RTT.
4. (Done in step 6) Share the protocol definition with the server: `Plugins/Protocol` is
   generated from `backend/WildCat.Protocol`; move it into the UPM package's Runtime folder and
   point `tools/sync-protocol.sh` there.
5. Rebuild the sample scene and prefab and package as a UPM package (`com.<org>.wildcat`).
   (Step 3 renamed the brand from Apadana to WildCat and pointed the editor menu links at the
   GitHub repository.)
6. Delete the obsolete `AddInventoryItems` and `SetScoreInLeaderBoard` (the server stopped
   trusting client grants and scores in step 4) and show script-granted items and scores in
   the sample instead.
