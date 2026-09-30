# Wire protocol between the Unity SDK and the gateway

Defined in `backend/Messages/uNetMessages.cs` (server copy) and
`unity-sdk/Assets/Backend/Plugins/LowLevelAPI/uNetMessages.cs` (client copy, diverged: the
server copy defaults strings to ""). Documented from the code on 2026-09-30 and updated after
roadmap step 2, which removed the same message types from both copies. The two copies must
become one shared source.

## Framing

One WebSocket binary message carries exactly one `ApadanaMessageBase`. Fragmented messages are
reassembled by the gateway up to 2 MB. Everything is written with .NET `BinaryWriter`
(little-endian; strings are 7-bit-varint length plus UTF-8; Guid is int32 16 plus
`Guid.ToByteArray()`; DateTime is `ToString()`/`Parse` and therefore culture dependent;
TimeSpan is a double of milliseconds).

```
int16  msgType            ApadanaMsgType (explicit values 6..162 with gaps, HighestInternal = 1023)
byte   header flags       HasGameTarget=1, HasPlayerTarget=2, HasAuthData=4, (8 reserved),
                          HasRequestID=16, stored=32, buffered=64, hasTime=128 (last three unused)
[uint32 requestID]        if HasRequestID   (client sets it; server echoes it on the response)
[string ticket]           if HasAuthData    (session Guid as string)
[string game]             if HasGameTarget  (match Guid as string)
[count + string[]]        if HasPlayerTarget (BUG: written as int32, read as int16)
int32 len + payload       one MessageBase subclass, no type tag
```

Message types above 1023 are "user messages": they must carry auth, and with `HasGameTarget`
the gateway relays the raw frame to the other players in the match (including the sender's
ticket, which is the session hijack bug).

Responses and forwarded events are wrapped in `OptionalResponse<T>`: `bool isSuccessful`, then
either `int32 errorCode, string errorMessage` or `T` inline. Error codes: -1 any exception (the
raw exception message is sent to the client), 1 domain failure, 2 already exists or "requested
you as well". Relay events built by `RelayServerGame` are raw `T`, unwrapped. `EmptyMessage`
still writes one `true` byte.

The client sends `HeartBeatRequest` after 10 s without any send; the gateway also pings the
player grain at most every 5 s per connection.

## Handled messages

Every `ApadanaMsgType` member carries an explicit value equal to the ordinal the legacy code
got by counting the enum, so no surviving id changed. Messages removed in step 2 leave gaps;
do not reuse those ids. C = client, S = server.

Session and auth

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 6/7 | InitializeSessionRequest / Response | {Guid DeveloperKey, Guid GameKey} / {Guid session} | `IDeveloper.InitializeSession` |
| 76/77 | AuthenticateRequest / Response | {ticket, byte LoginType (Device, UserWithPassword, Email), Id, PasswordHash (client MD5), bool AutoCreate, dict} / {userId, name, ticket, bool isNewlyCreated} | id-map grains, `IPlayerCreator(0)`, `IPlayer.LoginWith*` |
| 78/79 | CreateNewAccountRequest / Response | {ticket, LoginType, Id, PasswordHash, dict} / {accountId} | `IPlayerCreator(0)` (no session check) |
| 80/81 | EmailActivationRequest / Response | {ticket, email, code} / empty | `IPlayer.Activate` |
| 82/83 | AddAuthMechanismToAccountRequest / Response | {LoginType, Id, PasswordHash, dict} / empty | `IPlayer.AddPlayer*` |
| 100/101 | HeartBeatRequest / Response | empty | `IPlayer.HeartBeat` |
| 8 | ErrorMsg (S) | {string} | never actually delivered (published to an unsubscribed stream) |

Profile and cloud data

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 36..39 | GetName, SetName | {string} | `IPlayer.Get/SetPlayerName` |
| 68/69 | GetPlayerName | {playerId} / {name} | `IPlayer(id).GetPlayerName` |
| 30..33 | GetCloudData, SetCloudData | {key} / {value}; {key, value} | `IPlayer.Get/SetPlayerString` |
| 72..75 | SetPublicCloudData, GetPublicCloudData | {key, value}; {key} / {bool hasValue, value} | `IPlayer.*PublicString` |
| 70/71 | GetPlayerPublicCloudData | {player, key} / {hasValue, value} | `IPlayer(player).GetPlayerPublicString` |
| 34/35 | GetPublisherData | {key} / {value} | `IDeveloperStorage(devKey).GetString` |
| 62..65 | Set/GetPublisherData_S | {devKey, devSecret, key[, value]} | `IDeveloperStorage` (Get ignores the secret); server-to-server, the SDK no longer sends them |

Friends

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 88/89 | GetFriends | {string[] publicKeys} / {friends: {userId, name, status, dict}[], sent[], received[]} | `IPlayerFriendList.GetFriends` |
| 90/91 | InviteFriend | {value, byte InvitationType (AccountId, playerName, Email, UserId), bytes} / empty | resolves id maps then `Invite` |
| 93/94, 96/97 | AcceptFriend, DenyFriend | {otherPlayerId, bytes} / empty | `AcceptInvitation`, `DenyInvitation` |
| 92, 95, 98, 99 | FriendshipInvitationEvent, FriendshipAcceptedEvent, FriendshipDeniedEvent, FriendStatusChangedEvent (S) | OptionalResponse<{status, friend, friendName, bytes}> | from the FriendEvents stream |

Challenges

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 17/18 | StartChallenge | {uint TimeOut, Player, PlayerName, bytes, Guid GameKey} / empty (1 disconnected, 2 requested you as well) | `IPlayerChallengeHandler.SendChallengeRequest` |
| 20/21, 23/24 | AcceptChallenge, DenyChallenge | ChallengeMessage / empty (1 timeout) | `AcceptChallenge`, `DenyChallenge` on the challenger's handler |
| 161/162 | GetPlayerChallenges | empty / {sent[], received[]} of {status, Guid gameId, Player, PlayerName, bytes} | `GetChallengeRequests` (bug: received lands in sent) |
| 19, 22, 25 | StartChallengeRequestedEvent, ChallengeAcceptedEvent, ChallengeDeniedEvent (S) | OptionalResponse<ChallengeMessage> (GameKey carries the match id on accept) | from the ChallengeEvents stream |

Matchmaking, match state and relay

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 15/16 | FindMatch | {matchType, uint skill, uint waitTime} / {Guid gameId, bytes, string[] players} | `ICustomMatchMaker(gameKey, matchType).FindMatch`, then `PlayerStarted` |
| 129/130 | SetMatchProperty | {Guid game, bool isPlayerProperty, key, bytes} / empty | `IRelayServerGame.SetPlayer/GameProperty` |
| 133/134 | RejoinMatch | {Guid} / empty | `IPlayer.RejoinMatch` |
| 131/132 | GetPlayerPendingMatches | empty / {matchId, startTime, players[]}[] | `IPlayer.GetPendingMatches` |
| 13/14 | LeaveGame | header game / empty | `IRelayServerGame.PlayerFinished` (response currently throws) |
| >1023 | user messages | raw frame with HasGameTarget | `SendToAllBut(player, 0, frame)` retried 5x |
| 9, 10 | GamePlayerAddedEvent {Player, Game}, GameFinishedEvent {bool FinishedWithDisconnect, Game} (S) | | from RelayServerGame |
| 125..128 | GamePlayerRemovedEvent, GamePlayerRejoinedEvent, MatchRejoinSyncEvent {Game, gameProps, playerProps}, MatchPropertyChangedEvent {Game, Player?, key, bytes} (S) | | from RelayServerGame |

Leaderboards, inventory, scripting, time, dev

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 26/27 | GetLeaderboard | {byte type Top/Around/Me, name, [count], [offset]} / {playerRank, count, entries: {playerId, userName, score, entryTime}[]} | `ILeaderBoard` |
| 28/29 | SetLeaderboard | {name, int score} / empty | `ILeaderBoard.AddPlayerScore` (client-chosen score) |
| 109/110 | GetInventory | {invName} / {(name, count)[]} | `IPlayerInventory.GetInventory` |
| 111..116 | AddItemToInventory, SetItemToInventory, RemoveInventoryItem | {invName, (item, count)[]} / inventory list | `AddItem`, `SetItem`, `RemoveItem` (client-chosen grants) |
| 119..122 | UnlockInventory, GetInventoryUnlockTime | {invName} | `Unlock`, `GetUnlocktime` |
| 66/67 | ExecuteScriptEvent | {bool isTypeSafe, eventName, (byte ParamType, value)[]} / {bool has, [type, value]} | `IScriptEventRunner.Run` |
| 42/43 | GetServerTime | none / {string} | `ITimeService` |
| 44/45 | CreateEnvironment (not in MASTER builds) | developer profile / {DeveloperKey, GameKey} | seeds developer, game "game1", leaderboard "global", mode "multi", script "Test" |

Defined but unhandled (fall to `default`; relayed if HasGameTarget, otherwise dropped without a
response): 84-87 RecoverPassword and ResendActivation, 107/108 GetDefinedItems, 117/118 BuyItem,
123/124 ConsumeItem. The grain logic behind them exists; only the gateway handlers are missing.
The ConsumeItem payload class (`ConsumeItemInventoryRequestMessage`) was removed in step 2 because
nothing used it; a future handler defines its own payload.

Removed in step 2 (the ids are now gaps): 0-5 old CreateAccount/Login/Logout, 11/12 JoinGame,
40/41 VerifyStoreReceipt (CafeBazaar, the only handled one), 46-61 developer and CafeBazaar admin
ids, 102-106 guild, and 135-160 achievements, store data, league, quests, Mulligan, BuyUBCItem,
SetDeck, SetUBCMatchResult, GetMainData, RegisterInTournament. `LoginType.Custom` and
`InvitationType.PlayfabId` went with PlayFab; both were the last members of their byte enums, so
the other values did not move.

## Protocol-level bugs

- Player-target count int32 vs int16 mismatch.
- `GetFriendsRequestMessage.Deserialize` writes into an empty array (throws for any non-zero
  count). `GetPlayerStoreDataMessage` and `SetDeckRequestMessage`, which deserialized into null
  lists, were removed in step 2.
- `GetPublicCloudDataResponse` writes a null string when the key is missing (throws, no
  response).
- `FindMatchResponse(string gameId, ...)` ignores its `gameId`.
- `MatchSyncEventData` writes the full count but skips empty bags.
- Culture-dependent DateTime text.
- Response frames copy the request header, so they carry `HasAuthData` with an empty ticket;
  a request with `HasPlayerTarget` makes the response serialization throw.

## Direction for the rewrite

Keep the transport shape (one binary WebSocket message per request, request id echo, typed
error envelope) because the Unity SDK is built around it, keep the pinned message ids, strip
the ticket from relayed frames, serialize DateTime as ticks or ISO 8601, split the file per
feature area, and consider generating both copies from one schema (MessagePack or protobuf are
the obvious candidates).
