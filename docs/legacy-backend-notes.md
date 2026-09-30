# Legacy backend notes: grains, flows, dependencies, known bugs

Survey of `backend/` as copied from the Apadana 2 repository on 2026-09-30 (Orleans 3.4.3,
net5.0, about 18,000 lines), updated after roadmap step 2 removed PlayFab, CafeBazaar, billing
and the dead subsystems. Removed code keeps a short note here (see "Removed in step 2" at the
end) so bug numbers and history stay readable. CLAUDE.md describes the Orleans 10 host from
step 1. Read this before touching a grain. File names are relative to `backend/`. Line numbers
refer to the copied files.

Paths and names follow the WildCat rename from roadmap step 3; the table below gives each
project's legacy name.

## Projects

| Project (legacy name) | Role |
|---|---|
| `WildCat.Interfaces` (`Interfaces`) | grain interfaces, state classes, DTOs, helpers (`ResourcesUtility` builds grain keys) |
| `WildCat.Grains` (`Collections`) | grain implementations plus SMTP and Roslyn scripting (namespace `WildCat.Scripting`, was `ScriptingAPI`) |
| `WildCat.Protocol` (`Messages`) | `uNetMessages.cs`: the binary wire protocol shared with the Unity SDK (netstandard2.0; namespace was `RealtimeServer`) |
| `WildCat.Host` (`Silo`) | the host: silo plus WebSocket gateway in one process, memory providers until the PostgreSQL port (Release used MongoDB before step 1); seeds one developer and game from `devsconfig.json` |
| `WildCat.Gateway` (`FrontEnd`) | WebSocket gateway library mapped at `/ws` by the host, using the silo's `IClusterClient` (a separate Orleans client process before step 1); `FrontEndServer.cs`, one class of over 2,000 lines, dispatches every message |
| `WildCat.Tests` (`Tests`) | xUnit on `InProcessTestCluster` (`TestCluster` before step 1), one shared cluster per collection |

Environment facts that apply everywhere: only `LeaderBoard` names a storage provider ("blob");
every other `Grain<T>` uses the default provider. All streams go through one memory stream
provider, "game", with ids built by `WildCat.Interfaces/StreamNames.cs` (the "SMS" provider
before step 1). No grain uses transactions; the 3.4.3 silo called `UseTransactions()` anyway and
the port dropped it.

## Grain inventory

Key column: G = Guid, S = string, I = integer, G+S = Guid compound key.

| Grain (file) | Interface, key | State and features | Responsibility |
|---|---|---|---|
| Player (Player.cs) | IPlayer, S (Guid string) | PlayerState; 10 s heartbeat timer marks Offline after 20 s silence; produces on its PlayerConnection stream | Account aggregate: create account by device/user+password/email, login variants, email activation and password reset codes, stored ELO score (get/set only), running relay games, pending matches and rejoin, `SendDataToConnection` push to the gateway; forwards key/value calls to PlayerStorage |
| PlayerCreator | IPlayerCreator, I | stateless worker | allocates a new Guid account id and calls `IPlayer.CreateAccountWith*` |
| PlayerStorage | IPlayerStorage, S "gameKey_playerId[_file]" | PlayerStorageState | private and public key/value store per player per game; sets global `JsonConvert.DefaultSettings`, which the PlayerInventory JSON round trip depends on |
| PlayerInventory | IPlayerInventory, S "g_p_[name_]inv" | Grain<PlayerInventoryState> but persists a JSON blob through PlayerStorage instead | add/set/buy/consume/remove items, packages, timed unlock chests |
| PlayerFriendList | IPlayerFriendList, G+S | FriendListState; produces on the FriendEvents stream for (gameKey, playerId); the `[AlwaysInterleave]` on its status setters was ignored by 3.x and removed in the port | invite handshake, friend list with public data and presence fan-out |
| PlayerChallengeHandler | IPlayerChallengeHandler, G+S | PlayerChallengesState; produces on the ChallengeEvents stream for (gameKey, playerId) | direct 1v1 challenges; on accept creates a RelayServerGame with the default mode |
| Session | ISession, G | not persisted (WriteState calls commented out) | bearer ticket: developer and game binding, user binding after login, `Touch()` and `TouchAndGetInfo()` refresh `LastCallTime`, 60 min idle expiry |
| Developer | IDeveloper, G | DeveloperState | publisher account: SHA1 password hash, developer secret, game registry, `InitializeSession(gameKey)` creates the ticket |
| DeveloperManager | IDeveloperManager, S ("main") | DeveloperManagerState | email to developer key index |
| DeveloperStorage | IDeveloperStorage, G | DeveloperStorageState | publisher key/value data; writes require the developer secret |
| RelayServerMatchMaker | ICustomMatchMaker, G+S (gameKey + mode name) | in-memory, `[Reentrant]`, 3 s timer | the only matchmaker, with time-based criteria relaxation (see flows) |
| RelayServerGame | IRelayServerGame, G+S (matchId + gameKey) | RelayServerGameState | match membership, status, game and player properties (10 KB cap), relays opaque byte[] to other players through `IPlayer.SendDataToConnection` |
| GameModeManager | IGameModeManager, G | GameModeManagerState | CRUD for GameMode (min/max players, finish on one disconnect, criteria list) |
| LeaderBoard | ILeaderBoard, S "{gameKey}_L_{name}" | LeaderBoardState on provider "blob"; full sorted list in memory, chunks in LeaderBoardStorage | add score, top, around me, rank, rewards |
| LeaderBoardStorage | IleaderBoardStorage, S | LeaderBoardStorageState | one chunk of entries |
| Item, PackageContainer | IItem, IPackageContainer, S | ItemState, PackageContainerState | catalog definitions; random drops not implemented |
| ResourceManager | IResourceManager, S | ResourceManagerState | registry of named resources per game (leaderboards, scripts); can emit a C# constants class for SDKs |
| ScriptEventCompiler | IScriptEventCompiler, S "{gameKey}_G_{event}" | ScriptEventState | stores and compiles a developer's C# event script (Roslyn) into a per-silo static cache |
| ScriptEventRunner | IScriptEventRunner, G+S | stateless | runs a compiled script with `Globals { args, WildCat }` |
| TimeService | ITimeService, G | stateless worker, reentrant | server time as a culture-dependent string |
| CustomIDToAccountIDMap, EmailAccountMapId, UserAccountIdMap, NameAccountIdMap | S | small state each | first-writer-wins uniqueness claims mapping device id, email, user name and display name to an account |

Non-grain classes: `EmailSenderUtility` (SMTP, sends only in `MASTER` builds, otherwise prints
the code), `JSONSerializer.cs` (Newtonsoft settings with `TypeNameHandling.All` and grain
reference converters), `ScriptCompilationUtility` and `WildCat.Scripting.WildCatScriptApi` (the
script sandbox, `ScriptingAPI.Apadana` before step 3). Step 2 removed `PlayfabManager` (the DI
singleton wrapping the PlayFab APIs), `HttpClientUtility` and `ApadanaAPI` (an older, unused
script API).

## Flows

Session: client sends developer key and game key; `Developer.InitializeSession` checks the game
belongs to the developer, creates a Guid ticket and initializes `ISession(ticket)`; the gateway
then calls `Touch()`, which starts the idle clock. Login binds the user id to the session, sets
presence, starts the heartbeat timer. Every gateway request carries the ticket; for API calls
and relay traffic alike the gateway calls `TouchAndGetInfo()`, which throws if the session is
not initialized, refreshes `LastCallTime`, checks the 60 minute idle expiry, and returns the
developer key, game key and user id that scope everything. Because the touch runs before the
check, the expiry never fires on this path; an idle session really ends when its unpersisted
grain is deactivated (bug 9). Logout or disconnect ends the session. Before step 2 these calls
also counted API calls and relay messages and flushed the counts to `AccountingSystem` for
billing.

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
`UNetConnectionDataTransferMessage` on the player's PlayerConnection stream; the gateway node
holding the socket is subscribed and writes the bytes out. Channel 0 carries system events
(player added, removed, rejoined, rejoin sync, property changed, game finished). Statuses:
Started, SomeJoined, AllJoined, SomeFinished, AllFinished, FinishedWithDisconnect. Disconnect
either finishes the game (`FinishGameWithOneDisconnect`) or marks the player Disconnected so
they can rejoin through `GetPendingMatches` and `RejoinMatch`.

Script events: a developer stores a C# method per event. `ScriptCompilationUtility` prepends a
generated call line, compiles with Roslyn against the WildCat.Grains and WildCat.Protocol
assemblies, and validates that every referenced type is a whitelisted primitive, declared in the
script, `WildCatScriptApi`, or `GetLeaderBoardResponse`. Scripts get `args` and a `WildCat`
object (a `WildCatScriptApi`, so scripts write `WildCat.GetPlayerData(...)`) with player data
get/set, time, leaderboard read/write (count clamped to 20), publisher data, and JSON
helpers; each call touches the session and is marshalled back onto the grain scheduler. Runs
are limited to 3 s (but see bugs).

## External dependencies

- SMTP: EmailSenderUtility, from Player activation and password reset. Credentials were removed;
  wire to configuration.
- Roslyn scripting: Microsoft.CodeAnalysis.CSharp.Scripting (5.0.0 since the port, 3.10 before).

Removed in step 2: PlayFab (`PlayFabAllSDK`; it backed PlayerStorage with entity files, set the
Player key format, validated provider logins and sent push notifications from TimedExecuter)
and CafeBazaar (receipt verification in IAPVerification). Nothing replaces either.

## Known bugs, ordered by severity

Security:
1. `FrontEndServer.ProcessUserMessage` relays the raw incoming frame, including the sender's
   session ticket, to every other player in the match. Any player can hijack an opponent's
   session. Rebuild the frame without the ticket.
2. Clients grant themselves inventory items (Add/Set inventory) and set their own leaderboard
   scores. Item grants and scores must come from server logic (scripts, match results).
3. Removed in step 2: `Player.LoginWithProvider` never checked the returned PlayFab id and
   trusted other provider names; the whole provider login family is gone.
4. `GetPublisherDataRequest_S` ignores the developer secret; `DeveloperStorage.Remove*` skip the
   secret check.
5. `CreateNewAccountRequest` never checks the session; `CreateEnvironmentRequest`
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
9. `Session` is never persisted: any deactivation or restart drops every session. (It also
   lost unflushed accounting counters until step 2 removed them.)
10. `RelayServerGame`: `gameMode` is not persisted (NullReferenceException after reactivation in
    disconnect and finish paths); a `Select` with side effects is never enumerated; property
    setters and the disconnect-finish path never write state.
11. `LeaderBoard.CreateList` indexes an empty list (any board with chunks fails to activate);
    partial chunks are never persisted; chunks are never cleared.
12. Removed in step 2: `DeveloperInvoiceCollection.GenerateInvoicesIfNeeded` looped forever;
    invoices are gone with billing.
13. Removed in step 2: `TimedExecutionManager` had broken DateTime keys and an A to B to A
    deadlock with the executer; the TimedExecuter subsystem is gone.
14. `RelayServerMatchMaker` (and the `MatchMaker` removed in step 2) uses `.Seconds`/`.Minutes`
    instead of `TotalSeconds` (waits capped at 60 s), never resolves the parked task on
    `RemoveDisconnectedPlayer`, and holds the grain call open longer than the 30 s response
    timeout. It also throws when the first criterion start time is not 0.
15. `ScriptCompilationUtility.RunScript` called `Thread.Abort` (PlatformNotSupported on .NET 5+;
    the port abandons the thread instead, so a runaway script keeps running). It races on the
    placeholder task so every call waits the full 3 s, keeps a non-thread-safe static per-silo
    cache (multi-silo clusters get KeyNotFound), and ignores compile diagnostics.
    `WildCatScriptApi.SetPlayerData` and `SetScoreInLeaderBoard` return the inner task without
    awaiting.
16. `Player.RejoinMatch` calls `game.PlayerRejoined`, which calls back into the same player.
    That worked on 3.4.3 only because call chain reentrancy was on by default there
    (`SchedulingOptions.AllowCallChainReentrancy = true`); Orleans 10 dropped the option, so the
    port restores it with `CallChainReentrancyFilter` until the cycles get targeted
    `RequestContext.AllowCallChainReentrancy()` calls. `Disconnected` ignores the login ticket, so a
    stale connection logs out a newer session. `SetEloScore` does not persist. (Step 2 removed
    `CalculateELO`, which treated a draw as a loss, and `AddPlayerProvider`, which allowed one
    account per provider name.)
17. `PlayerInventory`: rollback copies a reference (no-op); `Unlock()` on the main inventory
    calls itself; `throw ex` loses stacks.
18. `PlayerFriendList`: `GetSentInvitations` returns received; `InvitationAccepted` calls
    `First` before its null check; `SetFriendStatus` throws on asymmetric lists and can fail
    logins. `PlayerChallengeHandler` never persists sends and accepts; two simultaneous
    challenges deadlock.
19. Removed in step 2: `PlayfabManager` had an endless retry loop ending in
    `Environment.Exit(1)`, a wrong ticket check and an unsafe HashSet; PlayFab is gone.
20. `NameAccountIdMap.SetId` is never called, so name lookups always return null.
21. Gateway: LeaveGame response always throws (empty ticket assert); SessionRequired error goes to
    a stream nobody subscribes to; GetPlayerChallenges puts received into sent;
    `GetFriendsRequestMessage` deserializes into an empty array (the deserializers with null
    lists were card-game messages removed in step 2); `LogError(eventId, "text", exception)`
    everywhere loses stack traces; copy-pasted log messages; recursive retries with a 50 ms
    delay on non-idempotent operations; unawaited `OnDataReceived` so messages from one socket
    run concurrently and out of order.

Naming to fix: `iPlayer.cs`, `iLeaderBoard.cs`, `IleaderBoardStorage`, `EmailAccountMapId` vs
`IEmailToAccountIdMap`, `UserWithPassowrd`, `AuthenticationExtentions`,
`GetInventoryUnlockTimeResquest`, `ResendActivaitonResponse`, Consummed, Presense,
`IUNetConnectionObserver.cs` (now holds only `UNetConnectionDataTransferMessage`), and the
UNet/uNet prefix left over from Unity's old transport. (`iGame.cs`, `iMatchMaker.cs`,
`AddGameRespnse` and `GuidEvent` went away in step 2.)

Persian strings remain in `EmailSenderUtility.EMAIL_TEXT` (the activation email template). The
Persian comments in `IIAPVerificationManager.cs` went with CafeBazaar in step 2.

## Removed in step 2

- PlayFab: `PlayfabManager`, `IBackendStorageServer`, every PlayFab branch in Player,
  PlayerStorage, PlayerFriendList and PlayerChallengeHandler, the `titleId|playfabId` key format,
  the provider login family (`IPlayer.LoginWithProvider`, `AddPlayerProvider`,
  `CreateAccountWithProvider`, `GetPlayerProviderAccounts`,
  `IPlayerCreator.CreateAccountWithProvider`, `PlayerState.Providers`), `LoginType.Custom` and
  `InvitationType.PlayfabId`.
- CafeBazaar: `IAPVerification`, `IAPVerificationManager`, `HttpClientUtility`, messages 40/41
  (VerifyStoreReceipt) and 60/61 (AddCafePackageData).
- Billing: `AccountingSystem`, `DeveloperInvoiceCollection`, `InvoiceUtility`,
  `Developer.GetAccountingInfo` and the Session call and message counters.
- Dead code: `ApadanaAPI`; `Game`, `MatchMaker`, `IGame`, `IMatchMaker` and the Player code that
  only served them (Player keeps `GetEloScore`/`SetEloScore`); the TimedExecuter subsystem and
  its startup call; `RelayServerGame.ReceivePlayerData`; the gateway's `gameServerConnections`;
  the Login, Logout, GameCreated and GameDestroyed connection event types;
  `IPlayerAchievementManager`, `IPlayerQuestManager`, `IPlayerGroup` and `StorageBase` (no
  implementations); the card-game key helpers in `ResourcesUtility`; and the message types and
  classes for UBC purchases, decks, quests, achievements, leagues, tournaments, store data, main
  data, guilds and the old login and account calls. Message ids that survived did not change
  (see docs/wire-protocol.md).
