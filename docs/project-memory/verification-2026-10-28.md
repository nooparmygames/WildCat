# Review fixes and Unity Web verification on 2026-10-28

The owner requested fixes for the nine findings from the review of `acd5b8d`, using a Sol
subagent and regression tests, plus a Unity 6.6 upgrade and investigation of Claude's Web
threading concern. The owner then authorized committing and pushing each completed step.

## Backend fixes

Three GPT-6.1 Sol agents implemented and cross-reviewed the backend work. The parent reviewed
the diffs and handled commits and pushes on master.

| Finding | Result |
|---|---|
| Leaderboard scores lost on deactivation | Complete and partial chunks are saved, loaded through a populated task list, and covered across chunk boundaries |
| Admin tokens survive password reset | Persisted credential stamps are checked during JWT validation; password changes/resets rotate them and end existing sessions |
| Disconnected players remain in matchmaking | Searches carry cancellation and login identity; creation/join races recheck them and clean up without disconnecting a newer login |
| Pending matches cross game boundaries | The request's game filters match grain keys before their state is read |
| Mode edits ignored by active matchmakers | Each matching pass reads the current mode snapshot; deletion returns to documented default rules |
| Deleted leaderboards retain scores | Clearing removes chunk state and the active in-memory entries before recreation |
| Packages grant rewards repeatedly | Opening consumes one package, with rollback when costs or capacity prevent completion |
| Inventories exceed stack capacity | Capacity checks count distinct new stacks, while allowing changes to existing stacks |
| Score comparison overflows | Sorting uses CompareTo across the full integer range |

The new backend test classes contain 47 cases, including deterministic gates around delayed
StartGame and join replies. They also cover changed passwords during login, old cancellation
versus a replacement search, login takeover through another gateway, expired creation, package
rollback, stored leaderboard chunk boundaries, and deletion through the admin API.

Completed backend stages were committed and pushed:

- `e44bcfa`: Revoke admin sessions when developer credentials change.
- `d6a1e5c`: Persist leaderboard scores and enforce inventory consumption and capacity.
- `904c3a9`: Cancel stale matchmaking searches and scope pending matches by game.

## Unity and Web

The project was opened and upgraded by Unity 6000.6.5f1. The Web Build Support module was
installed for this Editor. The package minimum, project version, compatible built-in packages,
lockfile and serialized settings are recorded in `e70888d`, pushed separately.

Unity's 6.6 documentation confirms that managed threads, Timer, Task.Delay and timer-based
CancellationTokenSource deadlines do not work on Web. The previous SDK compiled for Web but
still depended on these mechanisms. Upgrading the Editor alone did not address that problem.
Primary references checked:

- [Web technical limitations](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/intro/technical-overview).
- [.NET API support on Web](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/web-dotnet-api-support).
- [Multithreading in Unity Web](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/web-multithreading).

The connection now receives an IConnectionScheduler. Native clients retain a managed timer
scheduler. Web clients use PumpedConnectionScheduler, with a monotonic clock pumped before
the callback dispatcher in WildCatBehaviour.Update. Connection and feature awaits preserve
the synchronization context in this mode. A single-waiter AsyncSignal replaces the writer's
SemaphoreSlim, so waking the writer does not require a worker thread. Custom Web clients
must supply and pump this scheduler themselves.

Five deterministic SDK tests use a single-thread synchronization context and a controlled
clock. They cover writer wakeup/response delivery, request timeout and cancellation, connect
timeout, heartbeat failure/reconnect backoff, and a stalled close handshake. An independent
Sol review found no further runtime defects in scheduling or disposal.

The checked-in scene `unity-sdk/Assets/WebGLSmoke/Smoke.unity` exercises both a direct SDK
client and WildCatBehaviour against the real Development host on port 5000. Its build helper
is WebGLSmokeBuild.Build; `tools/webgl-smoke.mjs` serves the compressed output and launches
installed Chrome through Playwright. The SDK project README documents the commands.

The first build attempt ran in an Editor started before the Web module finished installing.
Packaging reported that WebGL was unsupported, despite an inconsistent BuildReport success
value. No player was produced. Restarting the saved verification Editor loaded the module.
The subsequent build succeeded. After removing the temporary Pipeline package through Package
Manager, a fresh batch build succeeded again with zero errors and warnings, reporting
32,428,411 bytes. This final player passed in headless Chrome against the final backend code:

1. Two browser clients connected, initialized sessions and logged in.
2. Profile writes and reads completed.
3. Heartbeat RTT became nonzero.
4. An unanswered request hit its 100 ms timeout.
5. Both clients joined one match and a relayed integer reached the other client.
6. Aborting a socket triggered backoff, reconnect and session/account restoration.
7. Both clients disconnected and the scene logged WILDCAT_WEBGL_SMOKE_PASS.

## Validation and limits

| Check | Result |
|---|---|
| Backend Release suite | 755 passed, 10 skipped, 0 failed |
| Focused matchmaking tests | 37 passed |
| SDK Release suite | 66 passed, including five new scheduling cases |
| SDK CoreCheck | netstandard2.1/C# 9 build passed with warnings as errors |
| Panel | 38 tests, lint and production build passed |
| Unity final batch Web build | Passed, zero errors/warnings, without Pipeline dependency |
| Final browser integration run | Passed in headless Chrome, no uncaught browser exceptions |
| Solution Release build | Passed after stopping the smoke host that held Windows output DLLs open |

Nine skipped tests require PostgreSQL, including the new leaderboard persistence/deletion
case. Docker Desktop failed during startup while initializing its inference manager pipe,
so no live PostgreSQL provider validation was performed. The tenth skip is the existing
PlayerTests.AddEmailAndUserToAccount. Leaderboard chunk writes remain separate grain-state
operations, not a transaction across all chunks on storage failure.

Safari, Firefox, mobile browsers and mobile IL2CPP players were not run. A suspended browser
tab can still delay frame pumping. The browser test covers networking behavior, not visual
layout. This work did not deploy a server, publish a package or change repository visibility.

Local evidence (ignored by Git):

- `backend/WildCat.Tests/TestResults/review-backend-release-final.trx`.
- `backend/WildCat.Tests/TestResults/review-matchmaking-final.trx`.
- `unity-sdk/Tests/WildCat.Sdk.Tests/TestResults/review-sdk-release-final.trx`.
- `.tmp/webgl-review/final-build.log` and `build-result.txt`.
- `.tmp/webgl-review/browser-results.json` and the built `player/` directory.
- `.tmp/webgl-review/solution-final.log`.

The temporary Editors and smoke hosts were stopped after verification. The temporary Pipeline
package was removed; Playwright and browser output remain only under the ignored .tmp folder.
