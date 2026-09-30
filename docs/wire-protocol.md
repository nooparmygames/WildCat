# Wire protocol between the Unity SDK and the gateway

One source, `backend/WildCat.Protocol` (netstandard2.1, C# 9, no Orleans, ASP.NET or Newtonsoft
references, namespace `WildCat.Protocol`), shared by the server and the Unity SDK. The SDK's copy
in `unity-sdk/Assets/Backend/Plugins/Protocol` is generated from it (see "The SDK copy" below);
until roadmap step 6 the SDK kept its own diverged `LowLevelAPI/Messages.cs`. Documented from the
code on 2026-09-30 and updated after roadmap steps 2 (removed messages), 3 (the WildCat rename:
namespace `RealtimeServer`, `ApadanaMessageBase` and `ApadanaMsgType` became `WildCat.Protocol`,
`WildCatMessageBase` and `WildCatMsgType`), 4 (security fixes) and 6 (this format, the codec and
the split gateway; `docs/gateway.md` describes the gateway side).

## Source layout

```
backend/WildCat.Protocol/
  Codec/         WireCodec (the only entry point), WireWriter, WireReader, WireFormatException
  Envelope/      WildCatMessageBase (the frame), WildCatMsgType, WildCatMsgHeader, MessageBase,
                 OptionalResponse<T>
  Common/        EmptyMessage, StringMessage, IntegerMessage, GuidMessage, RawMessage,
                 StringAndByteArrayMessage
  Session/ Auth/ CloudData/ Friends/ Challenges/ Matches/ Leaderboards/ Inventory/ Scripts/ Time/
                 the payloads of each feature, one public type per file
  ServerOnly/    payloads that carry a developer secret or serve server tools (62-65 publisher data,
                 44/45 CreateEnvironment); never copied into the SDK
```

## The codec boundary

Everything that turns messages into bytes goes through `WireCodec`; nothing outside
`WildCat.Protocol` touches `BinaryWriter` or `BinaryReader`, so the format can be replaced in one
place later (the owner wants to, and judged MessagePack too large a payload):

```csharp
byte[]             WireCodec.Encode(WildCatMessageBase frame)
WildCatMessageBase WireCodec.Decode(byte[] frame)                       // also (buffer, offset, count)
byte[]             WireCodec.EncodePayload<T>(T payload)
T                  WireCodec.DecodePayload<T>(byte[] payload)           // also (payload, out int consumed)
T                  frame.GetPayload<T>()     void frame.SetPayload<T>(T payload)   // the same, on a frame
```

Frames are built with `WildCatMessageBase.Create`, `CreateWithRequestID`,
`CreateWithAuthData[AndRequestID]`, `CreateWithGameTarget`, `CreateWithPlayerTargets` and, for
answers, `CreateResponse(payload, responseType, request)`. Payload classes derive from
`MessageBase` and write their fields in `Serialize(WireWriter)` and read them back in the same
order in `Deserialize(WireReader)`; the writer and reader hold the value encodings below. Before
step 6 the payloads wrote to `BinaryWriter` directly and the frame had `ToByteArray()`,
`new WildCatMessageBase(byte[])`, `CreateMessageFromByteArray`, `GetData` and `SetData`.

Decoding checks every length against the bytes that are left before it allocates, and throws
only `WireFormatException` for bad input: a frame cut short, bytes after the payload, a negative
length, a count larger than the rest of the frame, a Guid that is not 16 bytes, a DateTime out of
range, an unknown script value type. A payload that reads fewer bytes than it was given is
accepted (a failed response of any type reads as `OptionalResponse<EmptyMessage>`); a frame is
not.

## Framing

One WebSocket binary message carries exactly one frame. Fragmented messages are reassembled by
the gateway up to `Gateway:MaxMessageBytes` (default 1 MiB); a larger message closes the socket
with MessageTooBig. Text messages are ignored.

```
int16  msgType            WildCatMsgType (explicit values 6..164 with gaps, HighestInternal = 1023)
byte   header flags       HasGameTarget=1, HasPlayerTarget=2, HasAuthData=4, (8 reserved),
                          HasRequestID=16, stored=32, buffered=64, hasTime=128 (last three unused)
[uint32 requestID]        if HasRequestID   (client sets it; the server echoes it on the answer)
[string ticket]           if HasAuthData    (session Guid as a string)
[string game]             if HasGameTarget  (match Guid as a string)
[int32 count + strings]   if HasPlayerTarget
int32 length + payload    one MessageBase subclass, no type tag; nothing may follow it
```

Value encodings (`WireWriter`, `WireReader`):

| Type | Encoding |
|---|---|
| numbers | little-endian: byte, int16, int32, uint32, int64, float, double; bool is one byte (0 or 1) |
| string | UTF-8 byte count as a 7-bit varint, then the bytes; null is written as "" and strings never read back as null (except `MatchPropertyChangeData.Player`, which has a presence flag) |
| byte[] | int32 length, then the bytes; null is written as empty |
| Guid | a 16-byte array (int32 16, then `Guid.ToByteArray()`) |
| DateTime | int64 tick count of the UTC instant. Local values are converted to UTC, Unspecified values are taken as UTC; values read back have Kind Utc. Never text, so no culture or calendar is involved |
| TimeSpan | double of milliseconds, rounded to the nearest tick when read |
| collections | int32 count, then the items; null is written as empty, null items as empty items. Maps (string to string, string to bytes) write key then value; a repeated key keeps the last value |
| nested payload | inline, no length or tag |

Answers are `OptionalResponse<T>`: `bool isSuccesful`, then either `int32 errorCode, string
errorMsg` or `T` inline. An answer frame carries only its type, the request id (when the request
had one) and the payload: no ticket, game or player targets, whatever the request carried.
Server events pushed from streams (friends, challenges) are `OptionalResponse<T>` without a
request id; match events built by `RelayServerGame` are the raw payload. `EmptyMessage` writes one
`true` byte.

Error codes: 1 a refused or failed request (a client error or an expected outcome), 2 already
exists or "requested you as well", -1 an internal error, answered with the text "Internal error"
while the details go to the server log (before step 6 the raw exception message went to the
client), and other values where a message documents them.

## Refusals before a handler runs

ErrorMsg (8) is the gateway's answer to a request it refuses before any handler runs. Its payload
is an `OptionalResponse<EmptyMessage>` that is never successful, and it echoes the request id when
the request had one:

- code 1, "The message could not be read.": the frame does not decode (no request id then);
- code 1, "Unsupported message type": no handler for an id up to 1023. This covers requests the
  server does not serve and every server event and response id, so a player cannot send forged
  events or answers to the rest of a match (before step 4 such frames with a game target were
  relayed);
- code 1, session refusals for requests that need a session: "This message needs a session
  ticket.", "The session ticket is not valid.", the unknown, ended or expired session texts, and
  "Log in before sending this message."; code -1, "The session could not be checked. Try again."
  when the session grain failed.

A payload that does not decode as the request's type is answered on the request's own response
type with code 1 and "The message could not be read.".

Sign-in limit: 76, 78, 80, 82, 84, 86 and 163 share a per-connection allowance, a token bucket of
`Gateway:AuthAttemptBurst` attempts (default 5) that refills at `Gateway:AuthAttemptsPerMinute`
(default 6, one every 10 seconds). An attempt over the limit gets its normal response type with
error code 1 and "Too many attempts, try again later", and reaches no grain.

## Game messages and relay

Message types above 1023 are game (user) messages. They need a session with a logged in player
(the ErrorMsg refusals above apply), and with `HasGameTarget` the gateway relays them to the
other players of that match: one relay worker per connection handles them in arrival order, so
they arrive in the order they were sent. The relayed copy is rebuilt without the ticket
(`HasAuthData` cleared since step 4; before that the raw frame, sender's ticket included, went
out, which let any player hijack an opponent's session) and without the player targets (the relay
always goes to every other player); message type, request id, game target and payload bytes stay
exactly as sent. A game message without `HasGameTarget` has nowhere to go and is dropped without
an answer, and so is one for a match that is not running.

The client sends `HeartBeatRequest` after 10 s without any send; the gateway also pings the
player grain at most every 5 s per connection while requests arrive.

## Handled messages

Every `WildCatMsgType` member carries an explicit value; the ids that survived step 2 equal the
ordinals the legacy code got by counting the enum, removed messages leave gaps, and new messages
take the next free id (163 and 164 are the first). Do not reuse a gap. C = client, S = server.
"Handler" names the class in `WildCat.Gateway/Handlers`; "adapter" means the message is still
served by the old `FrontEndServer` code through `LegacyAdapterHandler` until roadmap step 6b.

Session and auth (handlers since step 6)

| Id | Message | Payload | Handler, grain path |
|---|---|---|---|
| 6/7 | InitializeSession | {Guid DeveloperKey, Guid GameKey} / GuidMessage (the ticket) | `InitializeSessionHandler`, `IDeveloper.InitializeSession`; no session needed; code 1 for a game that is not the developer's |
| 100/101 | HeartBeat | empty | `HeartBeatHandler`, `IPlayer.HeartBeat`; needs a logged in player |
| 42/43 | GetServerTime | empty / DateTimeMessage (UTC ticks; a culture dependent string before step 6) | `GetServerTimeHandler`, `ITimeService`; no session needed, a ticket is ignored |
| 76/77 | Authenticate | {ticket, byte LoginType (Device, UserWithPassword, Email), Id, PasswordHash (plain password), bool AutoCreate, map} / {userId, name, ticket, bool isNewlyCreated} | `AuthenticateHandler`: id-map grains, `IPlayerCreator(0)`, `IPlayer.LoginWith*`; ticket in the payload; code 1 for a failed login |
| 78/79 | CreateNewAccount | {ticket, LoginType, Id, PasswordHash, map} / StringMessage (account id) | `CreateNewAccountHandler`, `IPlayerCreator(0)`; code 2 taken, 1 bad input |
| 80/81 | EmailActivation | {ticket, email, code} / empty | `EmailActivationHandler`, `IPlayer.Activate`; code 1 unknown address or wrong code |
| 82/83 | AddAuthMechanismToAccount | {LoginType, Id, PasswordHash, map} / empty | `AddAuthMechanismToAccountHandler`, `IPlayer.AddPlayer*`; needs a logged in player; code 1 taken ("Already exists") or bad input |
| 84/85 | RecoverPassword | {email} / empty | `RecoverPasswordHandler`, `IPlayer.SendPasswordChangeCodeToEmail`; needs a session; always succeeds |
| 86/87 | ResendActivation | {email} / empty | `ResendActivationHandler`, `IPlayer.ResendActivationCode`; needs a session; always succeeds |
| 163/164 | ResetPasswordWithCode | {email, code, newPassword} / empty | `ResetPasswordWithCodeHandler`, `IPlayer.ChangePasswordForEmailByCode`; needs a session; code 1 for a bad password and, with one text, for an unknown address or a wrong code |
| 8 | ErrorMsg (S) | OptionalResponse<EmptyMessage>, always failed | refusals before dispatch (see above) |

Authenticate, CreateNewAccount and EmailActivation carry the session ticket in the payload and
check it themselves (a malformed ticket is code 1 "The session ticket is not valid."); the newer
messages carry it in the frame header. Email addresses are compared in lower case.

Passwords: since step 4 the `PasswordHash` fields of 76, 78 and 82 carry the plain password (the
misleading name stays until the SDK rewrite), and so does `newPassword` of 163. The server stores
only a salted PBKDF2 hash and checks logins against it, so the connection must use TLS (wss://;
TLS terminates at the proxy in front of the host). Before step 4 the SDK sent an unsalted MD5.

Email codes: the activation code and the password reset code are stored apart, so an activation
code never resets a password. Each code is used up when it works, expires 30 minutes after it was
sent (`Player.EmailCodeLifetime`), and is thrown away after 5 wrong codes
(`Player.MaxEmailCodeAttempts`); codes are compared in constant time. RecoverPassword (84) sends
a reset code, ResetPasswordWithCode (163) uses it (a reset also activates the address), and
ResendActivation (86) replaces the activation code of an address that is not activated yet.
RecoverPassword and ResendActivation answer success whatever the address, so they do not reveal
which addresses are registered.

Profile and cloud data (adapter)

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 36..39 | GetName, SetName | {string} | `IPlayer.Get/SetPlayerName` |
| 68/69 | GetPlayerName | {playerId} / {name} | `IPlayer(id).GetPlayerName` |
| 30..33 | GetCloudData, SetCloudData | {key} / {value}; {key, value} | `IPlayer.Get/SetPlayerString` |
| 72..75 | SetPublicCloudData, GetPublicCloudData | {key, value}; {key} / {bool hasValue, [value]} | `IPlayer.*PublicString` |
| 70/71 | GetPlayerPublicCloudData | {player, key} / {hasValue, [value]} | `IPlayer(player).GetPlayerPublicString` |
| 34/35 | GetPublisherData | {key} / {value} | `IDeveloperStorage(devKey).GetString` |
| 62..65 | Set/GetPublisherData_S | {devKey, devSecret, key[, value]} | `IDeveloperStorage.SetString`, `GetStringForDeveloper` (both check the secret); server to server, no session; payloads in ServerOnly/ |
| 66/67 | ExecuteScriptEvent | {bool isTypeSafe, eventName, (byte ParamType, value)[]} / {bool has, [type, value]} | `IScriptEventRunner.Run` |

Friends (adapter)

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 88/89 | GetFriends | {string[] publicKeys} / {friends: {userId, name, status, map}[], sent, received} | `IPlayerFriendList.GetFriends` |
| 90/91 | InviteFriend | {value, byte InvitationType (AccountId, playerName, Email, UserId), bytes} / empty | resolves id maps then `Invite` |
| 93/94, 96/97 | AcceptFriend, DenyFriend | {otherPlayerId, bytes} / empty | `AcceptInvitation`, `DenyInvitation` |
| 92, 95, 98, 99 | FriendshipInvitationEvent, FriendshipAcceptedEvent, FriendshipDeniedEvent, FriendStatusChangedEvent (S) | OptionalResponse<{status, friend, friendName, bytes}> | from the FriendEvents stream (`Push/FriendEventFrames.cs`) |

Challenges (adapter)

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 17/18 | StartChallenge | {uint TimeOut, Player, PlayerName, bytes, Guid GameKey} / empty (1 disconnected, 2 requested you as well) | `IPlayerChallengeHandler.SendChallengeRequest` |
| 20/21, 23/24 | AcceptChallenge, DenyChallenge | ChallengeMessage / empty (1 timeout) | `AcceptChallenge`, `DenyChallenge` on the challenger's handler |
| 161/162 | GetPlayerChallenges | empty / {sent[], received[]} of {status, Guid gameId, Player, PlayerName, bytes} | `GetChallengeRequests` (bug: received lands in sent) |
| 19, 22, 25 | StartChallengeRequestedEvent, ChallengeAcceptedEvent, ChallengeDeniedEvent (S) | OptionalResponse<ChallengeMessage> (GameKey carries the match id on accept) | from the ChallengeEvents stream (`Push/ChallengeEventFrames.cs`) |

Matchmaking, match state and relay (adapter)

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 15/16 | FindMatch | {matchType, uint skill, uint waitTime} / {Guid gameId, string, string[] players} | `ICustomMatchMaker(gameKey, matchType).FindMatch`, then `PlayerStarted`; answered through the PlayerConnection stream |
| 129/130 | SetMatchProperty | {Guid game, bool isPlayerProperty, key, bytes} / empty | `IRelayServerGame.SetPlayer/GameProperty` |
| 133/134 | RejoinMatch | {Guid} / empty | `IPlayer.RejoinMatch` |
| 131/132 | GetPlayerPendingMatches | empty / {matchId, startTime, players[]}[] | `IPlayer.GetPendingMatches` |
| 13/14 | LeaveGame | header game / empty | `IRelayServerGame.PlayerFinished`; answered through the PlayerConnection stream (the answer threw before step 6) |
| >1023 | game messages | frame with HasGameTarget, relayed without the ticket, in order | dispatcher relay worker, `SendToAllBut(player, 0, frame)` (retried up to 5 times before step 6) |
| 9, 10 | GamePlayerAddedEvent {Player, Game}, GameFinishedEvent {bool FinishedWithDisconnect, Game} (S) | | from RelayServerGame |
| 125..128 | GamePlayerRemovedEvent, GamePlayerRejoinedEvent, MatchRejoinSyncEvent {Game, gameProps, playerProps}, MatchPropertyChangedEvent {Game, Player?, key, bytes} (S) | | from RelayServerGame |

Leaderboards, inventory (adapter)

| Id | Message | Payload | Grain path |
|---|---|---|---|
| 26/27 | GetLeaderboard | {byte type Top/Around/Me, name, [count], [offset]} / {playerRank, count, entries: {playerId, userName, score, entryTime}[]} | `ILeaderBoard` |
| 28/29 | SetLeaderboard | {name, int score} / empty | refused from clients (code 1, "Not allowed from clients. Grant items and set scores from a server script event."); scripts call `SetScoreInLeaderBoard` |
| 109/110 | GetInventory | {invName} / {(name, count)[]} | `IPlayerInventory.GetInventory` |
| 111..114 | AddItemToInventory, SetItemToInventory | {invName, (item, count)[]} / inventory list | refused from clients like 28; scripts call `AddInventoryItems`, `SetInventoryItems` |
| 115/116 | RemoveInventoryItem | {invName, (item, count)[]} / inventory list | `RemoveItem`; code 1 when the player does not hold that many |
| 119..122 | UnlockInventory, GetInventoryUnlockTime | {invName} | `Unlock`, `GetUnlocktime` |

Defined but unhandled (answered with ErrorMsg "Unsupported message type", never relayed): 107/108
GetDefinedItems, 117/118 BuyItem, 123/124 ConsumeItem, and 44/45 CreateEnvironment (step 4
deleted its unauthenticated handler; the host seeds developers and games from the `WildCat:Seed`
configuration section). The ConsumeItem payload class was removed in step 2; a future handler
defines its own payload.

Removed in step 2 (the ids are now gaps): 0-5 old CreateAccount/Login/Logout, 11/12 JoinGame,
40/41 VerifyStoreReceipt (CafeBazaar), 46-61 developer and CafeBazaar admin ids, 102-106 guild,
and 135-160 achievements, store data, league, quests, Mulligan, BuyUBCItem, SetDeck,
SetUBCMatchResult, GetMainData, RegisterInTournament. `LoginType.Custom` and
`InvitationType.PlayfabId` went with PlayFab; both were the last members of their byte enums, so
the other values did not move.

## Protocol bugs fixed in step 6

Wire format changes, made on both sides at once since the SDK now gets the same source:

- The player target count was written as int32 and read as int16; it is int32 both ways.
- `GetFriendsRequestMessage.Deserialize` wrote into an empty array (any key threw); it allocates.
- `GetPublicCloudDataResponse` and `GetPlayerPublicCloudDataResponse` wrote the value even when
  missing, and a missing value was null, so the write threw and no answer went out; the value is
  now written only with `hasValue`, and null strings are written as "" everywhere.
- `FindMatchResponse(string gameId, ...)` dropped its id; the constructor takes the Guid.
- `MatchSyncEventData` wrote the count of all player bags but skipped the empty ones; every bag
  is written.
- DateTime went out as culture dependent text (`ToString`/`Parse`); it is the UTC tick count.
  GetServerTime (43) answers with a `DateTimeMessage` instead of a string.
- Answers copied the request header, so they carried `HasAuthData` with an empty ticket, and a
  request with `HasPlayerTarget` made the answer throw; answers now carry only the request id.
  The LeaveGame answer, built with an empty ticket, threw every time.
- The string maps of 76, 78 and 82 wrote their count as one byte; it is int32 like every count.
- Null strings, arrays, lists and payloads threw when written; they are written as empty.
- Lengths and counts from the wire were trusted, so a small frame could make the reader allocate
  gigabytes or silently read short arrays; they are checked against the bytes left.

## The SDK copy

`unity-sdk/Assets/Backend/Plugins/Protocol` is generated, never edited: change
`backend/WildCat.Protocol`, then run from anywhere

```bash
tools/sync-protocol.sh          # or, on Windows: pwsh tools/sync-protocol.ps1
```

The script deletes the copy and writes it again from every `.cs` file of the protocol project
except `ServerOnly/`, `obj/` and `bin/`, adds a README saying so, and writes Unity `.meta` files
whose GUIDs are the MD5 of `wildcat-protocol:` plus the path relative to the copy (`""` for the
folder itself), so running it twice changes nothing. `WildCat.Tests/ProtocolSyncTests` fails when
the copy's files or contents differ from the source (ignoring line endings and BOMs), when a
`.meta` GUID is not the expected one, when `ServerOnly/` leaks into the copy, or when the copy does
not compile on its own as C# 9. The protocol project itself targets netstandard2.1 with
`LangVersion` 9 so the server build catches anything Unity 6 would not compile; keep assembly
attributes in the `.csproj`, not in `.cs` files.

## Direction

Keep the transport shape (one binary WebSocket message per frame, request id echo, typed error
envelope) because the Unity SDK is built around it, keep the pinned message ids and the
ticket-free relayed frames. A different encoding goes behind `WireCodec`, `WireWriter` and
`WireReader`; the payload classes and everything outside the protocol project stay as they are.
