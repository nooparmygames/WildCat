# AGENTS.md

This is the maintained project guide for Codex, ChatGPT working in this checkout, and other
coding agents. Read [the project memory index](docs/project-memory/MEMORY.md) when starting work
and follow its links for the task at hand. Keep confirmed decisions here; update project memory
when new decisions or verified milestones need to survive another session. `CLAUDE.md` imports
this file for compatibility.

This guide currently describes the local `master` checkout at `703ed09`, which still contains
Orleans 3.4.3/net5.0 code and Unity 2020.3 project settings. A remote check on 2026-10-02 found
`origin/claude/elegant-mendel-jr5n9a` at `f3590a4`, 48 commits ahead of `origin/master`. Its
`CLAUDE.md` marks all nine roadmap steps below as done. That work has not been merged into
`master`; do not restart the roadmap from this guide. Read the remote progress section in
[project memory](docs/project-memory/MEMORY.md) and inspect the branch before planning work.
When integrating it, refresh this guide and the Orleans references from the newer branch.

WildCat is a game backend being prepared for open-source release under the MIT license (see
`LICENSE`, NoOpArmy Softworks). It is built on Microsoft Orleans (virtual actors) and comes with a
Unity client SDK and a web admin panel. The code started life in 2019 to 2021 as the "Apadana"
platform (Orleans 3.4.3, net5.0, MongoDB, PlayFab, CafeBazaar IAP, Persian UI). The legacy code
was copied here on 2026-09-30 with a fresh git history and its secrets removed. The old
repository's history still contains an AWS access key, a PlayFab developer secret and an SMTP
password; those credentials must be rotated by the owner regardless of what happens here.

## Repository layout

```
backend/           the .NET solution (Apadana.sln): Interfaces, Collections (grains), Messages
                   (wire protocol), Silo, FrontEnd (WebSocket gateway), Tests
unity-sdk/         Unity project holding only Assets/Backend (the SDK) plus settings
docs/              legacy-backend-notes.md (grain inventory, flows, bug list),
                   wire-protocol.md (framing and message table),
                   unity-sdk-notes.md (API surface, transport, bugs, cleanup plan),
                   legacy-admin-panel.md (what the old panel did and the REST contract it expected)
.agents/skills/orleans/  the Orleans 10 skill: SKILL.md plus reference/*.md
docs/project-memory/    imported project memory, provenance, and handoff status
```

The old AngularJS admin panel is not in this repo. It was built on the commercial Fuse theme
and talked to a REST API that was never checked in, so it is rewritten from scratch using
`docs/legacy-admin-panel.md` as the spec.

## Toolchain and commands

- .NET SDKs 10.0.401, 9.0.318 and 6.0.419 were found on this Windows machine on 2026-10-02.
  The 2026-09-30 setup report also recorded Node 24, npm 11, Docker 29 and no local database
  server. Recheck tools on a new machine; use Docker for the database.
- The legacy solution still targets net5.0. Claude's 2026-09-30 handoff reported a successful
  build on the .NET 10 SDK with 24 known warnings. That result was not rerun during the context
  migration. Build with:

```powershell
dotnet build backend/Apadana.sln
```

- Tests use xUnit on an in-memory `TestCluster`. They are slow (100-player matchmaking loads,
  multi-second sleeps), order dependent within a collection, and some wait forever on failure.
  Run one class at a time when iterating:

```powershell
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~LeaderBoardTests"
```

- Run the silo in Debug (localhost clustering, memory storage, in-memory reminders) and then the
  gateway, which connects as an Orleans client and listens for WebSockets at `/ws` on port 5000:

```powershell
dotnet run --project backend/Silo
```

```powershell
dotnet run --project backend/FrontEnd
```

  Release builds still expect MongoDB at the address in `devsconfig.json`; that file holds
  placeholder keys and an empty password and goes away with the PostgreSQL port.
- Unity compilation was unverified in the original Claude session, which reported no Unity
  installation. Check Editor availability before a Unity task. The vendored PlayFab SDK and
  selected references were removed at copy time; further SDK cleanup is still planned.

## How the system fits together

Read `docs/legacy-backend-notes.md` for the full picture. The short version:

- Grains live in `backend/Collections`, interfaces and state classes in `backend/Interfaces`.
  Player, Session, Developer, RelayServerGame, RelayServerMatchMaker, PlayerFriendList,
  PlayerChallengeHandler, LeaderBoard, PlayerInventory, PlayerStorage, ScriptEventCompiler and
  the id-map grains are the core. `Game`/`MatchMaker` and the TimedExecuter subsystem are dead.
- A developer (publisher) owns games. A client initializes a session with developer key plus
  game key, receives a Guid ticket, logs in as a player, and sends every later message with the
  ticket.
- The gateway (`backend/FrontEnd/FrontEndServer.cs`, one 2,444-line class) decodes the binary
  protocol in `backend/Messages/uNetMessages.cs`, calls grains, and pushes server events back
  through Orleans SMS streams keyed by player id.
- Matches are relay games: the server tracks membership and properties and forwards opaque
  byte arrays between players; it never simulates the game. Matchmaking is per game mode with a
  time-based relaxation schedule.
- Developers can attach C# scripts to named events; they run through Roslyn on the silo with
  legacy type restrictions. Script isolation and timeout hardening are still roadmap work.

## Decisions (confirmed by the owner on 2026-09-30)

- Name: WildCat everywhere. Rename namespaces, `ApadanaMessageBase`, `ApadanaMsgType`, the
  `ScriptingAPI.Apadana` script object, editor menus, solution and project names. Do the rename
  as one dedicated commit so history stays readable.
- Orleans 10 on .NET 10, following `.agents/skills/orleans/reference/migration-3x.md`.
- Storage: PostgreSQL through the official ADO.NET packages for clustering, grain state and
  reminders. Memory providers in Debug and tests. No MongoDB, no Azure, no Redis.
- PlayFab: removed entirely. Delete `PlayfabManager`, `IBackendStorageServer`, the PlayFab
  branches in Player, PlayerStorage, PlayerFriendList, PlayerChallengeHandler, TimedExecuter,
  the `PlayFabAllSDK` package, `IsPlayfabRun` flags, the `titleId|playfabId` key format and the
  SDK's `InvitationType.PlayfabId`.
- CafeBazaar: removed entirely (grains, gateway handler, SDK call, panel tab). No other store
  receipt verification replaces it for now.
- Secrets and personal data: none in code or sample config, ever. SMTP, database and any
  future API keys come from configuration, user secrets or environment variables. Test data
  uses example.com addresses and placeholder GUIDs.
- Wire protocol: keep the one-binary-message-per-WebSocket-frame format the SDK implements,
  but fix it: explicit message ids, no ticket in relayed frames, the serialization bugs in
  `docs/wire-protocol.md`, invariant DateTime encoding, one protocol source shared by server and
  SDK. The owner wants to move to a different format later (MessagePack was judged too large a
  payload), so keep the protocol behind one serializer boundary.
- Admin: ASP.NET Core minimal REST API co-hosted with the silo (JWT bearer auth, OpenAPI), and a
  React + TypeScript SPA (Vite) as the panel. The endpoint list in `docs/legacy-admin-panel.md`
  is the starting contract.
- Tenancy: keep developers, games, developer secrets and per-game scoping. Drop billing:
  delete `AccountingSystem`, `DeveloperInvoiceCollection`, `InvoiceUtility`, the call and
  message counters in `Session`, the accounting endpoints and the dashboard counters in the
  panel spec. Usage metrics come back later through OpenTelemetry if needed.
- Script events: keep and port as is, then harden (per-player data scoping, real timeouts,
  cluster-wide compiled cache, diagnostics surfaced to the panel).
- Unity SDK: target Unity 6 LTS only, shipped as a UPM package (`package.json`, asmdef,
  Runtime/Editor/Samples~ layout). The 2020.3 project settings in `unity-sdk/` are temporary.
- Email: keep email login, activation and password reset with SMTP settings in configuration
  and a pluggable sender that logs the code when SMTP is not configured.
- No legacy data to migrate; state shapes can change freely.
- Deployment: Docker Compose first (PostgreSQL plus the WildCat host). Kubernetes later.
- Tests: port the existing suite to `InProcessTestCluster` as a safety net during the port,
  then replace it feature by feature with tests that use unique grain keys and bounded waits.
- All text a person reads (docs, comments, commit messages, UI strings) is plain ASCII in a
  natural voice; English is the default UI language, translations live in resource files.

## Roadmap

1. Port to Orleans 10 on .NET 10: packages, serialization attributes, hosting with a co-hosted
   gateway, memory streams or broadcast channels instead of SMS, `RegisterGrainTimer`,
   reminders package, tests on `InProcessTestCluster`. Keep behavior identical in this step.
2. Remove PlayFab, CafeBazaar, billing, dead subsystems and card-game leftovers listed in
   `docs/legacy-backend-notes.md` and `docs/unity-sdk-notes.md`.
3. Rename to WildCat.
4. Security fixes before any public deployment: ticket leaked in relayed frames,
   client-controlled inventory grants and scores, missing developer-secret checks, provider
   login bypass, unauthenticated environment creation, client-side MD5 replaced by a server-side
   password hash. Fix the gateway disconnect cleanup and persist Session.
5. PostgreSQL providers, configuration model, Docker Compose, README quick start.
6. Split the gateway into transport, dispatcher and per-feature handlers; pin message ids;
   single protocol source shared with the SDK.
7. Admin REST API and the React panel.
8. Unity SDK rewrite as a UPM package for Unity 6 (asmdef, no UNet types, thread-safe
   transport, reconnection, Task-based results, sample scene).
9. Script sandbox hardening. CI.

## Working rules for this repo

- Use the `orleans` skill at `.agents/skills/orleans/SKILL.md` for anything touching grains,
  hosting, storage, streams or tests. Read it directly if it is not listed in the current
  session's skill catalog. Its ten rules are the review checklist. The reference files retain
  the 2026-09-30 research; verify version-sensitive API details against the selected packages
  and official docs before implementing them. The owner decisions here take precedence over
  provider alternatives in those references.
- Every type crossing a grain call or stored as state gets `[GenerateSerializer]`, `[Id]` and
  `[Alias]` once the port starts. No `.Wait()`/`.Result`/`ConfigureAwait(false)` in grains.
- Grain interface names start with `I` and files match the type name (`IPlayer.cs`, not
  `iPlayer.cs`). Fix legacy names when a file is otherwise being edited, in a separate commit.
- New tests use unique grain keys per test and never wait without a timeout.
- Do not add connection strings or credentials to any checked-in file. `appsettings.json` holds
  structure with empty values; real values come from user secrets or environment variables.
- Before deleting legacy code, check `docs/legacy-backend-notes.md` for the bug list attached
  to it; some "bugs" are behaviors the SDK relies on.

## Writing and handoff

Use ASCII for human-readable text, except content that requires another language or Unicode
fixtures. Use straight quotes and plain hyphens. Write naturally, keep concrete examples, and
avoid filler, repetitive bold lead-ins, and a heading above every short paragraph. Documentation
should explain a concept's job and its consequences, with technical claims checked against
source. Do not compress several conditions into a sentence that needs to be read twice.

After substantive work, give a self-contained recap of the full request: what changed and why,
what was tested and the result, and any unfinished work or limitations. Link the main files and
state whether changes were committed, pushed or published. Ordinary questions need a direct
answer; interrupted work must be described as incomplete.
