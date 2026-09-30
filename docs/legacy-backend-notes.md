# Legacy backend notes: grains, flows, dependencies, known bugs

Survey of `backend/` as copied from the Apadana 2 repository on 2026-09-30 (Orleans 3.4.3,
net5.0, about 18,000 lines). Read this before touching a grain. File names are relative to
`backend/`. Line numbers refer to the copied files.

## Projects

| Project | Role |
|---|---|
| `Interfaces` | grain interfaces, state classes, DTOs, helpers (`ResourcesUtility` builds grain keys) |
| `Collections` | grain implementations plus PlayFab wrapper, SMTP, Roslyn scripting |
| `Messages` | `uNetMessages.cs`: the binary wire protocol shared with the Unity SDK (netstandard2.0) |
| `Silo` | silo host: MongoDB providers in Release, memory providers in Debug; seeds one developer and game from `devsconfig.json` |
| `FrontEnd` | ASP.NET Core WebSocket gateway at `/ws`; an Orleans client; the 2,444-line `FrontEndServer.cs` dispatches every message |
| `Tests` | xUnit on `TestCluster`, one shared cluster per collection |

Environment facts that apply everywhere: only `LeaderBoard` names a storage provider ("blob");
every other `Grain<T>` uses the default provider. All streams use the "SMS" provider. No grain
uses transactions even though the silo calls `UseTransactions()`.

## Grain inventory

Key column: G = Guid, S = string, I = integer, G+S = Guid compound key.

| Grain (file) | Interface, key | State and features | Responsibility |
|---|---|---|---|
| Player (Player.cs) | IPlayer, S (Guid string, or "titleId|playfabId" in PlayFab mode) | PlayerState; 10 s heartbeat timer marks Offline after 20 s silence; produces on SMS stream (Guid.Empty, playerKey) | Account aggregate: create account by device/user+password/email/provider, login variants, email activation and password reset codes, ELO (K=15), running relay games, pending matches and rejoin, `SendDataToConnection` push to the gateway; forwards key/value calls to PlayerStorage |
| PlayerCreator | IPlayerCreator, I | stateless worker | allocates a new Guid account id and calls `IPlayer.CreateAccountWith*` |
| PlayerStorage | IPlayerStorage, S "gameKey_playerId[_file]" | PlayerStorageState | private and public key/value store per player per game; in PlayFab mode bypasses Orleans storage and stores JSON as PlayFab entity files; sets global `JsonConvert.DefaultSettings` |
| PlayerInventory | IPlayerInventory, S "g_p_[name_]inv" | Grain<PlayerInventoryState> but persists a JSON blob through PlayerStorage instead | add/set/buy/consume/remove items, packages, timed unlock chests |
| PlayerFriendList | IPlayerFriendList, G+S | FriendListState; produces on SMS (gameKey, "FriendEvents"+playerId); `[AlwaysInterleave]` on status setters | invite handshake, friend list with public data and presence fan-out |
| PlayerChallengeHandler | IPlayerChallengeHandler, G+S | PlayerChallengesState; produces on SMS (gameKey, "ChallengeEvents"+playerId) | direct 1v1 challenges; on accept creates a RelayServerGame with the default mode |
| Session | ISession, G | not persisted (WriteState calls commented out) | bearer ticket: developer and game binding, user binding after login, per-call and per-message counters flushed to AccountingSystem, 60 min idle expiry |
| AccountingSystem | IAccountingSystem, G+S (gameKey + "M-YYYY") | MonthlyAccountingInfo; 10 min save timer | per-game monthly call and message counters for billing |
| Developer | IDeveloper, G | DeveloperState | publisher account: SHA1 password hash, developer secret, game registry, `InitializeSession(gameKey)` creates the ticket |
| DeveloperManager | IDeveloperManager, S ("main") | DeveloperManagerState | email to developer key index |
| DeveloperStorage | IDeveloperStorage, G | DeveloperStorageState | publisher key/value data; writes require the developer secret |
| DeveloperInvoiceCollection | IDeveloperInvoiceCollection, G | DeveloperInvoiceCollectionState | monthly invoices from accounting data (broken, see bugs) |
| Game, MatchMaker | IGame (G+S), IMatchMaker (S) | GameState; MatchMaker is reentrant with a 3 s timer | legacy two-player score game and ELO matchmaker; only tests use them |
| RelayServerMatchMaker | ICustomMatchMaker, G+S (gameKey + mode name) | in-memory, `[Reentrant]`, 3 s timer | production matchmaker with time-based criteria relaxation (see flows) |
| RelayServerGame | IRelayServerGame, G+S (matchId + gameKey) | RelayServerGameState | match membership, status, game and player properties (10 KB cap), relays opaque byte[] to other players through `IPlayer.SendDataToConnection` |
| GameModeManager | IGameModeManager, G | GameModeManagerState | CRUD for GameMode (min/max players, finish on one disconnect, criteria list) |
| LeaderBoard | ILeaderBoard, S "{gameKey}_L_{name}" | LeaderBoardState on provider "blob"; full sorted list in memory, chunks in LeaderBoardStorage | add score, top, around me, rank, rewards |
| LeaderBoardStorage | IleaderBoardStorage, S | LeaderBoardStorageState | one chunk of entries |
| Item, PackageContainer | IItem, IPackageContainer, S | ItemState, PackageContainerState | catalog definitions; random drops not implemented |
| ResourceManager | IResourceManager, S | ResourceManagerState | registry of named resources per game (leaderboards, scripts); can emit a C# constants class for SDKs |
| IAPVerificationManager, IAPVerification | G, G+S | IAPState (CafeBazaar OAuth credentials in plaintext) | CafeBazaar receipt validation and token refresh. Dropped. |
| ScriptEventCompiler | IScriptEventCompiler, S "{gameKey}_G_{event}" | ScriptEventState | stores and compiles a developer's C# event script (Roslyn) into a per-silo static cache |
| ScriptEventRunner | IScriptEventRunner, G+S | stateless | runs a compiled script with `Globals { args, Apadana }` |
| TimedExecutionManager, TimedExecuter | S "main", S (DateTime string) | reminder "StartTasks" every minute; executer has a retry timer | scheduled tasks (only PlayFab push notifications); no caller ever adds a task |
| TimeService | ITimeService, G | stateless worker, reentrant | server time as a culture-dependent string |
| CustomIDToAccountIDMap, EmailAccountMapId, UserAccountIdMap, NameAccountIdMap | S | small state each | first-writer-wins uniqueness claims mapping device id, email, user name and display name to an account |

Non-grain classes: `PlayfabManager` (the only `IBackendStorageServer`, DI singleton wrapping
PlayFab Server/Admin/Data/Auth/Groups APIs; `IsPlayfabRun()` is "title id not empty"),
`EmailSenderUtility` (SMTP, sends only in `MASTER` builds, otherwise prints the code),
`HttpClientUtility` (new HttpClient per call), `JSONSerializer.cs` (Newtonsoft settings with
`TypeNameHandling.All` and grain reference converters), `ScriptCompilationUtility` and
`ScriptingAPI.Apadana` (the script sandbox), `ApadanaAPI` (older unused script API).

## Flows

Session: client sends developer key and game key; `Developer.InitializeSession` checks the game
belongs to the developer, creates a Guid ticket and initializes `ISession(ticket)`. Login binds
the user id to the session, sets presence, starts the heartbeat timer. Every gateway request
carries the ticket; the gateway calls `AddUserCallCheckSessionAndGetInfo()` (API calls) or
`AddUserMessagesCheckSessionAndGetInfo(2)` (relay traffic) and scopes everything by the returned
developer key, game key and user id. Counters flush to AccountingSystem after 20 increments or
60 s. Logout or disconnect ends the session.

Matchmaking: the gateway calls `ICustomMatchMaker(gameKey, modeName).FindMatch(player, skill,
waitTime)`. The matchmaker loads its GameMode (or the default 2v2 mode), parks a
TaskCompletionSource per waiting player and every 3 s recomputes each player's criteria index
(the latest criterion whose start time has passed), times players out, and groups players whose
skill distance is within the current threshold, requiring MinPlayers-1 opponents when
`IsMinPlayersEnough` else MaxPlayers-1. On a match it creates `IRelayServerGame(Guid.NewGuid(),
gameKey)`, calls `StartGame`, and resolves every waiting task. The gateway then calls
`PlayerStarted` for the requesting player (marked TODO to make explicit). Challenges bypass the
matchmaker and create the game directly.

Relay: the backend never parses game payloads. `SendToAllBut(player, channel, bytes)` forwards
to every other InGame player via `IPlayer.SendDataToConnection`, which publishes a
`UNetConnectionDataTransferMessage` on the player's SMS stream; the gateway node holding the
socket is subscribed and writes the bytes out. Channel 0 carries system events (player added,
removed, rejoined, rejoin sync, property changed, game finished). Statuses: Started, SomeJoined,
AllJoined, SomeFinished, AllFinished, FinishedWithDisconnect. Disconnect either finishes the
game (`FinishGameWithOneDisconnect`) or marks the player Disconnected so they can rejoin through
`GetPendingMatches` and `RejoinMatch`.

Script events: a developer stores a C# method per event. `ScriptCompilationUtility` prepends a
generated call line, compiles with Roslyn against the Collections and Messages assemblies, and
validates that every referenced type is a whitelisted primitive, declared in the script,
`Apadana`, or `GetLeaderBoardResponse`. Scripts get `args` and an `Apadana` object with player
data get/set, time, leaderboard read/write (count clamped to 20), publisher data, and JSON
helpers; each call bills the session and is marshalled back onto the grain scheduler. Runs are
limited to 3 s (but see bugs).

## External dependencies

- PlayFab: compile-time hard dependency (`PlayFabAllSDK`), runtime optional by title id.
  Deep in PlayerStorage (entity files instead of Orleans storage), medium in Player (key format,
  provider login validation), hard in TimedExecuter (push). Most PlayfabManager methods are
  never called. Recommendation: remove entirely.
- CafeBazaar: IAPVerification only. Dropped.
- SMTP: EmailSenderUtility, from Player activation and password reset. Credentials were removed;
  wire to configuration.
- Roslyn scripting: Microsoft.CodeAnalysis.CSharp.Scripting 3.10.

## Known bugs, ordered by severity

Security:
1. `FrontEndServer.ProcessUserMessage` relays the raw incoming frame, including the sender's
   session ticket, to every other player in the match. Any player can hijack an opponent's
   session. Rebuild the frame without the ticket.
2. Clients grant themselves inventory items (Add/Set inventory) and set their own leaderboard
   scores. Item grants and scores must come from server logic (scripts, match results).
3. `Player.LoginWithProvider` only validates the "playfab" provider and never compares the
   returned PlayFab id with the claimed one; other provider names trust persisted
   `State.IsLoggedIn`.
4. `GetPublisherDataRequest_S` ignores the developer secret; `DeveloperStorage.Remove*` skip the
   secret check.
5. `CreateNewAccountRequest` neither validates nor bills the session; `CreateEnvironmentRequest`
   is unauthenticated and compiled in unless `MASTER` is defined (it never is).
6. Passwords are client-side MD5 then server-side unsalted SHA1. Scripts can read and write any
   player's private data by id. `TypeNameHandling.All` is set process-wide by PlayerStorage.
7. Gateway: no origin check, `AllowedHosts *`, HTTPS redirection commented out, a fixed 2 MB
   reassembly buffer per connection that throws above 2 MB.

Correctness:
8. Gateway disconnect cleanup never runs: `PlayerDisconnected` removes the connection entry
   before `ChangeConnectionStateToLoggedOut` looks it up, so stream subscriptions leak,
   `playerConnectionMapper` goes stale, and the player grain is never told. An abrupt socket
   close throws out of `ReceiveMessages` and skips the disconnect callback entirely.
9. `Session` is never persisted: any deactivation or restart drops every session and loses
   unflushed accounting counters.
10. `RelayServerGame`: `gameMode` is not persisted (NullReferenceException after reactivation in
    disconnect and finish paths); a `Select` with side effects is never enumerated; property
    setters and the disconnect-finish path never write state.
11. `LeaderBoard.CreateList` indexes an empty list (any board with chunks fails to activate);
    partial chunks are never persisted; chunks are never cleared.
12. `DeveloperInvoiceCollection.GenerateInvoicesIfNeeded` discards `AddMonths` and loops forever;
    adds `invoices[0]` instead of `invoices[i]`.
13. `TimedExecutionManager` discards `AddSeconds`, uses culture-dependent DateTime strings as
    keys, and has an A to B to A call cycle with the executer (deadlock).
14. Both matchmakers use `.Seconds`/`.Minutes` instead of `TotalSeconds` (waits capped at 60 s),
    never resolve the parked task on `RemoveDisconnectedPlayer`, and hold the grain call open
    longer than the 30 s response timeout. RelayServerMatchMaker throws when the first criterion
    start time is not 0.
15. `ScriptCompilationUtility.RunScript` calls `Thread.Abort` (PlatformNotSupported on .NET 5+),
    races on the placeholder task so every call waits the full 3 s, keeps a non-thread-safe
    static per-silo cache (multi-silo clusters get KeyNotFound), ignores compile diagnostics.
    `Apadana.SetPlayerData` and `SetScoreInLeaderBoard` return the inner task without awaiting.
16. `Player.RejoinMatch` calls `game.PlayerRejoined`, which calls back into the same
    non-reentrant player: deadlock until timeout. `Disconnected` ignores the login ticket, so a
    stale connection logs out a newer session. `CalculateELO` treats a draw as a loss.
    `SetEloScore` does not persist. `AddPlayerProvider` allows one account per provider name.
17. `PlayerInventory`: rollback copies a reference (no-op); `Unlock()` on the main inventory
    calls itself; `throw ex` loses stacks.
18. `PlayerFriendList`: `GetSentInvitations` returns received; `InvitationAccepted` calls
    `First` before its null check; `SetFriendStatus` throws on asymmetric lists and can fail
    logins. `PlayerChallengeHandler` never persists sends and accepts; two simultaneous
    challenges deadlock.
19. `PlayfabManager`: retry counter bug loops forever then `Environment.Exit(1)`; wrong result
    checked in `ValidateSessionTicket`; non-thread-safe HashSet on a singleton.
20. `NameAccountIdMap.SetId` is never called, so name lookups always return null.
21. Gateway: LeaveGame response always throws (empty ticket assert); SessionRequired error goes to
    a stream nobody subscribes to; GetPlayerChallenges puts received into sent; several
    deserializers index empty arrays or null lists; `LogError(eventId, "text", exception)`
    everywhere loses stack traces; copy-pasted log messages; recursive retries with a 50 ms
    delay on non-idempotent operations; unawaited `OnDataReceived` so messages from one socket
    run concurrently and out of order.

Dead code worth deleting: `ApadanaAPI`, `Game`/`MatchMaker`/`IGame` (tests only), the whole
TimedExecuter subsystem (no producers), `RelayServerGame.ReceivePlayerData`,
`gameServerConnections` in the gateway, `IUNetConnectionObserver` commented observer code, the
"UBC" card-game messages (`BuyUBCItem`, `SetDeck`, `Mulligan`, quests, league, achievements
messages with no handlers), `LoginOldRequest`/`CreateAccountOldRequest`, `HttpClientUtility`.

Naming to fix: `iPlayer.cs`, `iGame.cs`, `iMatchMaker.cs`, `iLeaderBoard.cs`,
`IleaderBoardStorage`, `EmailAccountMapId` vs `IEmailToAccountIdMap`, `UserWithPassowrd`,
`AuthenticationExtentions`, `GetInventoryUnlockTimeResquest`, `ResendActivaitonResponse`,
`AddGameRespnse`, `GuidEvent` (meant GuildEvent), Consummed, Presense, and the UNet/uNet prefix
left over from Unity's old transport.

Persian strings remain in `EmailSenderUtility.EMAIL_TEXT` (the activation email template) and in
comments in `IIAPVerificationManager.cs` (CafeBazaar, to be deleted).
