# WildCat

WildCat is a backend for online games built on Microsoft Orleans (virtual actors) and .NET 10.
One host process runs the Orleans silo and a WebSocket gateway that game clients talk to over a
compact binary protocol. It covers:

- sessions scoped to a developer (publisher) and one of their games
- player accounts: device login, user name and password, and email with activation and password
  reset codes
- friends with invitations and presence, and direct challenges between players
- matchmaking per game mode with a time-based relaxation schedule, and relay matches: the server
  tracks members and properties and forwards opaque messages between players; it never runs
  the game simulation
- leaderboards, player inventory with items and packages, and per-player cloud data
- server script events: C# snippets a developer attaches to named events, compiled with Roslyn
  and run on the server

A Unity client SDK lives in `unity-sdk/`.

WildCat started in 2019 as a private platform called Apadana (Orleans 3, MongoDB, PlayFab) and is
being cleaned up for an open-source release.

## Status

Pre-release. The backend builds and runs, but the configuration, the wire protocol and the SDK
will still change in breaking ways. The roadmap and the decisions behind it are in
[CLAUDE.md](CLAUDE.md), and the known bugs in
[docs/legacy-backend-notes.md](docs/legacy-backend-notes.md); read the bug list before exposing a
server to the internet.

- Admin REST API (`backend/WildCat.Admin`, under `/api/v1`, OpenAPI at `/openapi/v1.json`): in progress.
- Admin panel (`panel/`, React and TypeScript): in progress.

## Quick start

### A: from source, with memory providers

You need the .NET 10 SDK.

```bash
dotnet run --project backend/WildCat.Host
```

This runs in the Development environment with localhost clustering, memory grain storage and
in-memory reminders, so all state is gone when the process stops. The Development settings seed
one developer and one game with placeholder keys:

| What | Key |
|---|---|
| developer "Dev" (developer@example.com) | `11111111-1111-1111-1111-111111111111` |
| game "SampleGame" | `22222222-2222-2222-2222-222222222222` |

Clients connect to `ws://localhost:5000/ws` and initialize a session with the developer key and
game key. In the Unity SDK that is `ConnectLocally()` followed by
`InitializeSession(developerKey, gameKey)`. SMTP is not configured, so activation and password
reset codes appear in the host's log.

### B: Docker Compose, with PostgreSQL

You need Docker with Compose 2.24 or later.

```bash
cp .env.example .env
# edit .env and replace the placeholder POSTGRES_PASSWORD
docker compose up --build
```

`compose.yaml` starts PostgreSQL 17 and the WildCat host. On its first start PostgreSQL creates the
Orleans tables from [deploy/postgres](deploy/postgres/README.md), and the host keeps cluster
membership, grain state and reminders there, so state survives restarts. Clients connect to
`ws://localhost:5000/ws`; the database port is not published. `docker compose down -v` removes
the containers and the data.

The container runs in the Production environment, which seeds nothing, so there is no developer
or game at first. To try it out, either set `ASPNETCORE_ENVIRONMENT=Development` in `.env` to get
the sample developer and game from option A (their keys are public, so only on your own
machine), or describe your own in `.env`:

```
WildCat__Seed__Developers__0__Key=<new guid>
WildCat__Seed__Developers__0__Name=My Studio
WildCat__Seed__Developers__0__Email=studio@example.com
WildCat__Seed__Developers__0__Games__0__Key=<another new guid>
WildCat__Seed__Developers__0__Games__0__Name=My Game
```

The host speaks plain HTTP. Anywhere but your own machine, put a reverse proxy that terminates
TLS in front of it so clients use `wss://` (passwords travel inside the WebSocket messages), and
list the proxy's address under `ForwardedHeaders:KnownProxies` unless it connects over loopback.

## Configuration

The host reads `appsettings.json`, then `appsettings.{Environment}.json`, then environment
variables; later sources win. The checked-in files hold no secrets, so put connection strings,
passwords and keys in environment variables (in `.env` when you use Docker Compose). An
environment variable name joins the sections with `__`, so `WildCat:Orleans:Provider` is
`WildCat__Orleans__Provider`.

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:WildCat` | empty | PostgreSQL connection string (Npgsql format). Required when the provider is `PostgreSQL`; the host stops at startup without it. |
| `WildCat:Orleans:Provider` | `Memory` | `Memory`: localhost clustering, memory grain storage and in-memory reminders, one silo, nothing survives a restart. `PostgreSQL`: ADO.NET clustering, grain storage (`Default`, `blob`, `PubSubStore`) and reminders in the database above. Streams are memory streams either way. |
| `WildCat:Orleans:ClusterId` | `wildcat-dev` | Silos with the same cluster id and database form one cluster. |
| `WildCat:Orleans:ServiceId` | `wildcat` | Grain state and reminders are stored under this id; keep it stable across deployments. |
| `WildCat:Orleans:SiloPort` | `11111` | Port for silo-to-silo traffic. |
| `WildCat:Orleans:GatewayPort` | `30000` | Orleans client gateway port (not the WebSocket port). |
| `WildCat:Orleans:AdvertisedHost` | empty | Address other silos use to reach this one. When empty in PostgreSQL mode, the host advertises its first non-loopback IPv4 address and listens on all addresses, which is what a container needs. |
| `WildCat:Seed:Developers` | none | Developers to create at startup, each `{ Key, Name, Email, Games: [ { Key, Name } ] }`; every seeded game also gets the leaderboard `l1`. Existing developers and games are skipped. `appsettings.Development.json` seeds the sample from quick start A. |
| `WildCat:Email:SmtpHost`, `SmtpPort`, `UseSsl` | empty, `587`, `true` | SMTP server for activation and password reset codes. Without `SmtpHost` and `FromAddress` the host writes the codes to the log instead of sending them. |
| `WildCat:Email:UserName`, `Password` | empty | SMTP credentials. |
| `WildCat:Email:FromAddress`, `FromName` | empty, `WildCat` | Sender of account emails. |
| `Gateway:MaxMessageBytes` | `1048576` | Largest WebSocket message a client may send; bigger ones close the connection. |
| `Gateway:AllowedOrigins` | empty | Allowed `Origin` headers for WebSocket upgrades; empty accepts any. |
| `ForwardedHeaders:KnownProxies` | empty | Proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` headers are trusted, besides loopback. |

## Repository layout

```
backend/                  the .NET solution (WildCat.sln)
  WildCat.Host/           the host: silo, gateway, configuration, Dockerfile
  WildCat.Gateway/        WebSocket gateway library mapped at /ws
  WildCat.Grains/         grain implementations, email senders, script sandbox
  WildCat.Interfaces/     grain interfaces, state classes, shared helpers
  WildCat.Protocol/       binary wire protocol (netstandard2.0, shared with the Unity SDK)
  WildCat.Tests/          xUnit tests on an in-process Orleans cluster
unity-sdk/                Unity project holding the client SDK in Assets/Backend
deploy/postgres/          Orleans PostgreSQL scripts, run when the database is created
docs/                     design notes, see Documentation
compose.yaml              PostgreSQL plus the host
.env.example              template for .env, which compose.yaml reads
```

## Running the tests

The tests run on an in-process Orleans cluster with memory providers, so they need nothing
installed besides the .NET SDK. They are slow and some wait forever when they fail, so set a hang
timeout for full runs and filter by class while iterating:

```bash
dotnet test backend/WildCat.Tests/WildCat.Tests.csproj --blame-hang-timeout 4m
dotnet test backend/WildCat.Tests/WildCat.Tests.csproj --filter "FullyQualifiedName~LeaderBoardTests"
```

The PostgreSQL integration tests are skipped unless the environment variable
`WILDCAT_TEST_POSTGRES` holds a connection string. A throwaway database in Docker works (change
the host port if 5432 is taken):

```bash
POSTGRES_PASSWORD=pick-a-local-password
docker run -d --name wildcat-postgres -p 127.0.0.1:5432:5432 \
  -e POSTGRES_DB=wildcat -e POSTGRES_USER=wildcat -e POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
  -v "$PWD/deploy/postgres:/docker-entrypoint-initdb.d:ro" postgres:17
export WILDCAT_TEST_POSTGRES="Host=localhost;Port=5432;Database=wildcat;Username=wildcat;Password=$POSTGRES_PASSWORD"
dotnet test backend/WildCat.Tests/WildCat.Tests.csproj --filter "FullyQualifiedName~Postgre"
```

The same database serves the host from source: set `WildCat__Orleans__Provider=PostgreSQL` and
`ConnectionStrings__WildCat` to that connection string before `dotnet run`.
`docker rm -f -v wildcat-postgres` throws it away.

## Documentation

- [CLAUDE.md](CLAUDE.md): decisions, roadmap and working rules for contributors (and for Claude
  Code, which this repository is set up for)
- [docs/legacy-backend-notes.md](docs/legacy-backend-notes.md): grain inventory, main flows and
  the known bug list
- [docs/wire-protocol.md](docs/wire-protocol.md): WebSocket framing and the message table
- [docs/unity-sdk-notes.md](docs/unity-sdk-notes.md): the Unity SDK's API, transport and cleanup
  plan
- [docs/legacy-admin-panel.md](docs/legacy-admin-panel.md): what the old admin panel did and the
  REST contract it expected
- [deploy/postgres/README.md](deploy/postgres/README.md): where the database scripts come from

## License

MIT, see [LICENSE](LICENSE). Copyright (c) 2026 NoOpArmy Softworks. The scripts in
`deploy/postgres` come from Orleans and are MIT licensed, Copyright (c) .NET Foundation.
