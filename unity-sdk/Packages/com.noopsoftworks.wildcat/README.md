# WildCat SDK for Unity

`com.noopsoftworks.wildcat` is the Unity 6 client of the [WildCat](https://github.com/nooparmygames/wildcat)
game backend: sessions, device, user name and email accounts, friends, challenges, matchmaking,
relay matches, leaderboards, inventory, cloud data and server script events, over one WebSocket.
Every call returns a Task; the connection has request timeouts, a heartbeat and automatic
reconnection with session resume.

Status: 0.1.0, pre-release. The engine-free core is tested with plain .NET against the real
server; the Unity-specific parts (the MonoBehaviour, the editor menus, the sample and the WebGL
transport) compile against stand-in Unity assemblies but have not been run in the Unity editor
yet. See [Documentation~/index.md](Documentation~/index.md#what-is-verified-and-what-is-not).

## Install

Unity 6 (6000.0) or later. In Window > Package Manager, choose + > Install package from git URL
and enter

```
https://github.com/nooparmygames/wildcat.git?path=unity-sdk/Packages/com.noopsoftworks.wildcat
```

or add it to `Packages/manifest.json`:

```json
"com.noopsoftworks.wildcat": "https://github.com/nooparmygames/wildcat.git?path=unity-sdk/Packages/com.noopsoftworks.wildcat"
```

To work on the SDK itself, open `unity-sdk/` in the repository: the package is embedded there.

## Quick start

1. WildCat > Create Or Select Settings, and fill in the server URL (`wss://...` in production,
   `ws://localhost:5000/ws` for a local server) and the developer and game keys.
2. WildCat > Add Client To Scene adds a "WildCat" object with the WildCat Client component
   (`WildCatBehaviour`) using those settings.
3. Use its client from any script:

```csharp
using UnityEngine;
using WildCat;
using WildCat.Unity;

public class Lobby : MonoBehaviour
{
    [SerializeField] private WildCatBehaviour wildCat;

    private async void Start()
    {
        try
        {
            await wildCat.ConnectAndInitializeAsync();          // connect + Session.InitializeAsync
            WildCatClient client = wildCat.Client;
            LoginResult login = await client.Auth.LoginWithThisDeviceAsync();
            Debug.Log("Logged in as " + login.PlayerId);

            client.Friends.InvitationReceived += invitation => Debug.Log("Invited by " + invitation.PlayerName);
            MatchInfo match = await client.Matchmaking.FindMatchAsync("duel");
            if (match == null)
                Debug.Log("Nobody to play with right now.");
        }
        catch (WildCatException ex)
        {
            Debug.LogError("The server said no: " + ex.ErrorCode + " " + ex.Message);
        }
    }
}
```

Prefer coroutines? `yield return client.Profile.GetNameAsync().AsCoroutine()` returns a
yield instruction with `IsSuccessful`, `Result`, `Error` and `ErrorCode`.

The Basic sample (Package Manager > WildCat > Samples) shows a whole flow, relayed game messages
between two clients included.

## Threading

The socket is read and written on background threads. Everything the SDK hands back to the game
(event handlers, relay handlers and the completion of every Task, ConnectAsync and
DisconnectAsync included) goes through `IMainThreadDispatcher`. `WildCatBehaviour` pumps a
`QueuedDispatcher` in `Update`, so in a scene all of it runs on Unity's main thread and may touch
Unity objects. Do not block the main thread on a Task (`.Result`, `.Wait()`): its completion
waits for the next `Update`. Outside Unity (tools, tests) the default `InlineDispatcher` runs
callbacks on the network thread.

## Errors, timeouts and reconnection

- A server error is a `WildCatException` with the server's `ErrorCode` (1 refused, 2 already
  exists, -1 internal) and message. `IsGatewayRefusal` marks refusals from the gateway itself:
  no session, not logged in, unsupported message.
- A request without an answer within `RequestTimeout` (15 s) fails with `TimeoutException`; an
  answer that comes later is dropped. FindMatch waits its matchmaking time on top.
- When the socket drops, waiting requests fail with `WildCatDisconnectedException` at once and
  the client reconnects after 1, 2, 4 ... up to 30 seconds (with jitter), raising `Reconnecting`
  before each attempt. It then opens a new session with the same keys and logs in again with the
  last credentials, and raises `Reconnected`. Matches are not rejoined on their own: call
  `Matches.GetPendingAsync` and `Matches.RejoinAsync`. `Disconnected` is raised once when the
  connection closes for good.
- A heartbeat every 10 seconds keeps the player online on the server and measures the round
  trip time (`Connection.RoundTripTime`, `SmoothedRoundTripTime`); a heartbeat without an answer
  drops the socket and starts a reconnection.

## Security

- Passwords go to the server as plain text (it stores a salted hash), so the SDK sends them only
  over `wss://` or to localhost. `AllowPlainTextPasswordsWithoutTls` lifts that for a test server
  on a trusted network and logs a warning on every use.
- The developer and game keys identify the game and ship in every build; they are not secrets.
  Never put the developer secret in a game.
- Clients cannot set scores or grant items. Do that in a server script event and call it with
  `client.Scripts.ExecuteAsync`.

## Migrating from the Assets/Backend SDK

The old SDK was a singleton MonoBehaviour, `BackendPlatform.Instance`, whose calls returned a
`Result` to poll or `yield return r.Wait()`. The new calls are grouped by feature on a
`WildCatClient` (`wildCat.Client` below) and return Tasks. Result fields map to Tasks as follows:
`isSuccessful` is a completed Task, `errorCode`/`errorMessage` are `WildCatException.ErrorCode` and
`Message`, `value` is the Task's result (or `TaskYieldInstruction<T>.Result` with coroutines).

| Old (`BackendPlatform.Instance.`) | New (`wildCat.Client.`) |
|---|---|
| `Connect(url, port)`, `ConnectLocally()` | `ConnectAsync()` with `WildCatSettings.ServerUrl` (or `WildCatClientOptions.ServerUri`) |
| `Disconnect()` | `DisconnectAsync()` (or `Dispose()`) |
| `InitializeSession(developerKey, gameKey)` | `Session.InitializeAsync(developerKey, gameKey)`; `WildCatBehaviour.ConnectAndInitializeAsync()` does both |
| `OnConnected`, `OnConnectionFailed`, `OnDisconnected` | `Connected`; ConnectAsync throws `WildCatDisconnectedException`; `Disconnected` (raised once), plus `Reconnecting`, `Reconnected` |
| `isConnected`, `isLoggedIn`, `userID`, `userName` | `IsConnected`, `IsLoggedIn`, `Auth.PlayerId`, `Auth.PlayerName` |
| `AuthenticateWithDevice(autoCreate)` | `Auth.LoginWithThisDeviceAsync(createAccount)` |
| `AuthenticateWithTestDevice(device, autoCreate)` | `Auth.LoginWithDeviceAsync(deviceId, createAccount)` |
| `AuthenticateWithUserAndPassword(user, password, autoCreate)` | `Auth.LoginWithUserNameAsync(userName, password, createAccount)` |
| `AuthenticateWithEmail(email, password)` | `Auth.LoginWithEmailAsync(email, password)` |
| `CreateAccountWithDevice()` | `Auth.CreateAccountWithThisDeviceAsync()` or `CreateAccountWithDeviceAsync(deviceId)` |
| `CreateAccountWithUserAndPassword(user, password)` | `Auth.CreateAccountWithUserNameAsync(userName, password)` (the old one sent the wrong field) |
| `CreateAccountWithEmail(email, password)` | `Auth.CreateAccountWithEmailAsync(email, password)` |
| `AddUserNameAndPasswordToAccount`, `AddEmailAndPasswordToAccount` | `Auth.AddUserNameAsync`, `Auth.AddEmailAsync` |
| `ActivateEmail(email, code)` | `Auth.ActivateEmailAsync(email, code)` |
| (none) | `Auth.ResendActivationCodeAsync`, `Auth.RecoverPasswordAsync`, `Auth.ResetPasswordAsync` |
| `SetName`, `GetName`, `GetPlayerName(playerId)` | `Profile.SetNameAsync`, `Profile.GetNameAsync`, `Profile.GetPlayerNameAsync` |
| `SaveCloudData`, `GetCloudData` | `CloudData.SetAsync`, `CloudData.GetAsync` |
| `SavePublicCloudData`, `GetPublicCloudData` | `CloudData.SetPublicAsync`, `CloudData.GetPublicAsync` (null when missing) |
| `GetPlayerPublicCloudData(player, key)` | `CloudData.GetPlayerPublicAsync(playerId, key)` |
| `GetPublisherData(key)` | `CloudData.GetPublisherDataAsync(key)` |
| `GetFriends(keys)`, `OnFriendListReceived` | `Friends.GetAsync(publicDataKeys)` returning a `FriendList` |
| `InviteFriendByUserName`, `InviteFriendByEmail`, `InviteFriendByPlayerId` | `Friends.InviteByUserNameAsync`, `InviteByEmailAsync`, `InviteByPlayerIdAsync` (and `InviteByDisplayNameAsync`) |
| `AcceptFriendInvitation`, `DenyFriendInvitation` | `Friends.AcceptAsync`, `Friends.DenyAsync` |
| `OnFriendshipInvitation`, `OnFriendshipAccepted`, `OnFriendshipDenied`, `OnFriendStatusChanged` | `Friends.InvitationReceived`, `InvitationAccepted`, `InvitationDenied`, `StatusChanged` |
| `SendChallengeRequest`, `AcceptChallengeRequest`, `DenyChallengeRequest`, `GetChallenges` | `Challenges.SendAsync`, `AcceptAsync`, `DenyAsync` (the old one never completed), `GetAsync` |
| `OnchallengeRequestReceived`, `OnchallengeStarted`, `OnchallengeDenied` | `Challenges.ChallengeReceived`, `ChallengeAccepted` (with `MatchId`), `ChallengeDenied` |
| `FindMatch(game, skill)` | `Matchmaking.FindMatchAsync(gameMode, skill, maxWait)`; null when nobody was found (was error 1) |
| `FinishGame(game)` | `Matches.LeaveAsync(matchId)` |
| `SetPlayerMatchProperty`, `SetGlobalMatchProperty` | `Matches.SetPlayerPropertyAsync`, `Matches.SetGlobalPropertyAsync` (byte[] or MessageBase) |
| `RejoinMatch`, `GetPendingMatches` | `Matches.RejoinAsync`, `Matches.GetPendingAsync` |
| `OnGameFinished`, `OnGameFinishedDueToDisconnect` | `Matches.Finished` with `ByDisconnect` |
| `OnMatchPropertyChanged`, `OnPlayerInMatchPropertyChanged` | `Matches.PropertyChanged` with `IsGlobal` and `PlayerId` |
| `OnMatchPlayerRemoved`, `OnMatchPlayerRejoined`, `OnMatchRejoinSyncDataReceived` | `Matches.PlayerLeft`, `PlayerRejoined`, `Resynced`; new: `PlayerJoined` |
| `UpdateGame<T>(game, msg, type, qos)` | `Relay.Send(matchId, type, message)` (no QoS: one reliable, ordered socket) |
| `RegisterHandler(type, handler)`, `UnregisterHandler` | `Relay.On<T>(type, handler)` returning an IDisposable; raw frames: `Connection.RegisterHandler` |
| `(WildCatMsgType)(WildCatMsgType.HighestInternal + n)` | `RelayClient.MessageType(n)` (the cast still works) |
| `GetTopPlayersInLeaderBoard`, `GetAroundPlayersInLeaderBoard`, `GetRankInLeaderBoard` | `Leaderboards.GetTopAsync`, `GetAroundMeAsync`, `GetRankAsync` |
| `SetScoreInLeaderBoard` (obsolete) | removed: a server script event calls `WildCat.SetScoreInLeaderBoard`; run it with `Scripts.ExecuteAsync` |
| `GetInventory()` | `Inventory.GetAsync(inventoryName)` (the old one sent no inventory name) |
| `RemoveInventoryItems(items)` | `Inventory.RemoveAsync(inventoryName, items)`; new: `UnlockAsync`, `GetUnlockTimeAsync` |
| `AddInventoryItems` (obsolete) | removed: a server script event calls `WildCat.AddInventoryItems` |
| `ExecuteScriptEvent(name, args)` | `Scripts.ExecuteAsync(name, args)` or `ExecuteAsync<T>(name, args)` |
| `GetServerTime()` | `ServerTime.GetAsync()`; `ServerTime.Now` estimates it afterwards |
| `Result.Wait()` in a coroutine | `task.AsCoroutine()` |
| `BackendPlatform.majorVersion` and so on | `WildCatVersion.Current` |

Protocol types (`WildCat.Protocol`, such as `MessageBase`, `WireWriter`, `WireReader`,
`WildCatMsgType`) are the same as in the step 6 SDK; custom game messages carry over unchanged.

## Documentation

- [Documentation~/index.md](Documentation~/index.md): the manual (architecture, API, testing).
- [CHANGELOG.md](CHANGELOG.md)
- The server's wire protocol: `docs/wire-protocol.md` in the repository.

Licensed under the MIT license (LICENSE.md).
