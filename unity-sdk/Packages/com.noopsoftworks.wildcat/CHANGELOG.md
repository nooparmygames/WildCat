# Changelog

Before 1.0.0, minor versions may break the API.

## Unreleased

- Target Unity 6.6; the development project selects 6000.6.5f1.
- Run Web connection timeouts, heartbeats and reconnection on a frame-driven scheduler.
  Browser clients preserve Unity's synchronization context and wake their writer without
  thread-pool work. `WildCatBehaviour` configures this automatically; custom Web clients
  supply and pump `PumpedConnectionScheduler`.
- Add a repeatable Web integration scene and a headless Chrome runner.

## [0.1.0] - 2026-09-30

The first release as a UPM package for Unity 6, replacing the `Assets/Backend` SDK
(`BackendPlatform`) of the Apadana era. See the migration table in README.md.

### Added

- `WildCatClient` with one Task-based client per feature: `Session`, `Auth`, `Profile`,
  `CloudData`, `Friends`, `Challenges`, `Matchmaking`, `Matches`, `Relay`, `Leaderboards`,
  `Inventory`, `Scripts` and `ServerTime`.
- `WildCatConnection`: a thread-safe transport with a single writer, request/response matching
  by request id, a timeout per request (15 seconds by default), a heartbeat every 10 seconds
  with round trip time measurement, and automatic reconnection with exponential backoff (1 to 30
  seconds, with jitter) that resumes the session and the login (giving up only when the server
  refuses it with error code 1 or 2). Session state is applied on the network thread as each
  answer arrives, so a late pump cannot undo a reconnection or a disconnect.
- Send limits: outgoing messages above `MaxMessageBytes` (1 MiB) fail with ArgumentException
  before they are sent, the send queue is bounded (`MaxQueuedMessages`, `MaxQueuedBytes`; a full
  queue fails requests with `WildCatErrorCodes.SendQueueFull`), and requests that timed out while
  queued are not sent.
- Errors as exceptions: `WildCatException` (server error code and text, including the gateway's
  ErrorMsg refusals), `TimeoutException`, `WildCatDisconnectedException`.
- `IMainThreadDispatcher` lets applications control where callbacks run. `WildCatBehaviour`
  pumps a `QueuedDispatcher` so events and Task
  completions run on Unity's main thread.
- Calls for password recovery and activation codes: `Auth.RecoverPasswordAsync`,
  `Auth.ResetPasswordAsync`, `Auth.ResendActivationCodeAsync`.
- `WildCatSettings` asset, `WildCatBehaviour` component (with an opt-in Run In Background
  option), coroutine helpers (`AsCoroutine`; argument errors come back as failed Tasks, so it
  never throws), and a browser WebSocket transport for WebGL (not verified in a browser yet).
- Editor menus: About window, documentation links, settings asset creation, Add Client To Scene,
  Create Clone.
- The Basic sample.

### Changed

- The wire protocol is the one shared with the server (`Runtime/Protocol`, generated from
  `backend/WildCat.Protocol`). It is not compatible with the earlier SDK's wire format.
- Passwords are sent only over `wss://` or to localhost unless
  `AllowPlainTextPasswordsWithoutTls` is set.

### Deprecated

- `Friends.InviteByDisplayNameAsync`: the server cannot find players by display name yet, so it
  always fails with error code 1.

### Removed

- `BackendPlatform`, `Result`, `Singleton`, the UNet `NetworkTransmitter`, websocket-sharp,
  `OneTimeRunner`, the broken `WildCat.prefab` and the old sample scene.
- `SetScoreInLeaderBoard` and `AddInventoryItems`: the server refuses both from clients; use a
  script event.
- QoS channel parameters (everything is reliable and ordered over one WebSocket).

### Fixed

- Reconnecting after an explicit disconnect no longer loses responses or raises the final
  disconnect event twice.
- Denying a challenge completes the request. Gateway refusals also complete pending requests
  with an error instead of leaving them waiting.
- Inventory calls accept a null inventory name, and account creation sends the correct
  password field.
- Leaderboard responses match requests by ID and query type, including empty results.
- The handler table and outgoing queue synchronize access between the socket and main threads.
