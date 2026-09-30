# Unity SDK notes

`unity-sdk/` is the `Assets/Backend` folder of the original Unity 2020.3.18 project plus its
ProjectSettings and a trimmed package manifest. The vendored PlayFab SDK, the basketball art,
the DOTS packages and the broken prefabs were not copied. Surveyed 2026-09-30.

Edits made during the copy: the three `PlayfabErrorCode` properties in `Result.cs`, the
`AuthenticateWithPlayfab` method and the two `Debug.Log` lines that printed the developer secret
were removed from `BackendPlatform.cs`; the sample's public server IP became `localhost`; the
developer and game key GUIDs in `Sample/test.unity` were zeroed; the hardcoded game id in
`Client.cs` became an empty string. Nothing else changed, so every bug below is still present.

## Layout

```
Assets/Backend/Editor/Scripts/      ApadanaWindow (about box), ApadanaMenus (docs links, "Create Clone"
                                    symlink helper for running two editors), OneTimeRunner (dead)
Assets/Backend/Plugins/BackendPlatform.cs   the public API, 2,300 lines, singleton MonoBehaviour
Assets/Backend/Plugins/Result.cs            Result, Result<T>, Result<T,Q>: poll or `yield return r.Wait()`
Assets/Backend/Plugins/LowLevelAPI/         Client (request/response plumbing), WebSocketNetworkTransmitter
                                            (websocket-sharp), NetworkTransmitter (dead UNet LLAPI),
                                            RequestIdGenerator, uNetMessages.cs (protocol copy)
Assets/Backend/Plugins/Sample/              Sample.cs IMGUI demo, TimestampedBuffer (interpolation), test.unity
Assets/Backend/Plugins/Prefabs/Apadana.prefab   references a missing script GUID (broken)
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
  `AddEmailAndPasswordToAccount`, `ActivateEmail`. Passwords are MD5-hashed client side.
- Profile: `SetName`, `GetName`, `GetPlayerName(playerId)`.
- Friends: `GetFriends(publicDataKeys)`, `InviteFriendByUserName/Email/PlayerId`,
  `AcceptFriendInvitation`, `DenyFriendInvitation`, events `OnFriendListReceived`,
  `OnFriendshipInvitation`, `OnFriendshipAccepted`, `OnFriendshipDenied`,
  `OnFriendStatusChanged`.
- Inventory: `GetInventory`, `AddInventoryItems`, `RemoveInventoryItems`, plus card-game
  leftovers (`BuyUBCItem`, `SetDeck`, `ClaimQuests`, `RegisterTournament`,
  `SetAchievementProgress`, `SetQuestProgress`, `SetMatchResult`) whose server handlers do not
  exist.
- Leaderboards: `SetScoreInLeaderBoard`, `GetRankInLeaderBoard`,
  `GetAroundPlayersInLeaderBoard`, `GetTopPlayersInLeaderBoard`.
- Matchmaking and matches: `FindMatch(gameMode, skill)` (15 s timeout, error 1),
  `FinishGame(game)`, `SetPlayerMatchProperty`, `SetGlobalMatchProperty` (10 KB values),
  `RejoinMatch`, `GetPendingMatches`, events `OnGameFinished`, `OnGameFinishedDueToDisconnect`,
  `OnMatchPropertyChanged`, `OnPlayerInMatchPropertyChanged`, fields `OnMatchPlayerRemoved`,
  `OnMatchPlayerRejoined`, `OnMatchRejoinSyncDataReceived`.
- Relay: `UpdateGame<T>(game, msg, type, qos)` fire-and-forget with custom message types above
  1023; `RegisterHandler(type, handler)` / `UnregisterHandler` for raw messages, payload via
  `msg.GetData<T>()`.
- Challenges: `SendChallengeRequest`, `AcceptChallengeRequest`, `DenyChallengeRequest`, events
  `OnchallengeRequestReceived`, `OnchallengeStarted`, `OnchallengeDenied`.
- Cloud data: `SaveCloudData`, `SavePublicCloudData`, `GetCloudData`, `GetPublicCloudData`,
  `GetPlayerPublicCloudData`, `GetPublisherData`, developer-only `DevGetPublisherData` and
  `DevSetPublisherData` (send the developer secret from the client; remove).
- Scripts: `ExecuteScriptEvent(name, params object[] args)` for int, short, long, bool, byte,
  byte[], float, double, string.
- Time: `GetServerTime()` (needs a ticket despite only checking the connection; culture
  dependent parse).
- IAP: `VerifyCafeReceipt`. Dropped; delete with `CafeVerificationStatusResults` and the
  `VerifyStoreReceipt*` messages.

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
- `SetDeck` and `DenyChallengeRequest` register their response handlers on the request type, so
  their Results never complete.
- `SetPlayerMatchProperty(Guid, string, MessageBase)` calls itself (stack overflow).
- `AddInventoryItems` and `RemoveInventoryItems` never set `inventoryName` and throw on the
  null write.
- `CreateAccountWithUserAndPassword` sends the `userName` property instead of the argument.
- Leaderboard responses are identified by guessing from flags; a Top query on an empty board
  never completes.
- `GetFriendsRequestMessage.Deserialize` never allocates its array; other deserializers use
  null lists; player-target count is int32 vs int16 (see docs/wire-protocol.md).
- Sample: leaderboard never displays (`entriesToShow` unassigned), `int.Parse` on empty input,
  null reference in `OnFriendStatusChanged` before the list loads, callbacks re-registered on
  every Start, `TimestampedBuffer.GetValue` divides by zero on equal timestamps, sends at 20 Hz
  while the comment says 10.
- `Apadana.prefab` and `test.unity` reference a script GUID that no longer exists.

## Cleanup plan

1. Add an asmdef, remove the UNet types (own `Qos` enum), delete `NetworkTransmitter.cs`,
   `Singleton.cs`, `OneTimeRunner.cs`, the UBC/quest/league/deck/"Old" messages, the dev
   publisher-data calls, and the CafeBazaar code.
2. Replace the polling `Result` with `Task`-based or `UniTask`-style calls with timeouts, keep a
   coroutine-friendly wrapper.
3. Make the transport thread-safe (concurrent queue, immutable handler map), add reconnection
   with session resume, and a real heartbeat with RTT.
4. Share the protocol definition with the server (see docs/wire-protocol.md) instead of a
   diverged copy.
5. Rebuild the sample scene and prefab, rename the brand (Apadana) to the new project name,
   replace the editor menu links, and package as a UPM package (`com.<org>.<name>`).
6. Server side must stop trusting client-supplied inventory grants, scores and game-server
   calls before the SDK exposes them again.
