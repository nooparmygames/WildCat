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

A client SDK for Unity 6.6, the UPM package `com.noopsoftworks.wildcat`, lives in
`unity-sdk/Packages/` (see its [README](unity-sdk/Packages/com.noopsoftworks.wildcat/README.md)).

## Why we are open sourcing WildCat

We originally built this tool with Orleans more than eight years ago. We are open sourcing it
in the hope that it is useful to other developers and gives people a practical example of how
to build distributed, scalable backends for games.

We also want to address the difficulties we have found with existing game backends,
particularly how hard they can be to customize and debug. Our goal is to make adding your own
game logic feel as natural as extending a game engine.

We used AI to help clean up the original code and develop recent features, and we plan to
continue using it as we develop WildCat.

## Status

Pre-release. The backend builds and runs, but the configuration, the wire protocol and the SDK
may change in breaking ways. The [roadmap](docs/developer-experience-roadmap.md) describes the
planned customization tools. The [backend notes](docs/legacy-backend-notes.md) track known
issues; review the open issues before deploying a public server.

The [admin API](docs/admin-api.md) runs under `/api/v1`, with OpenAPI at `/openapi/v1.json`.
The [developer panel](panel/README.md) provides a React interface for managing games and content.

## Getting started

| What you want to do | Start here |
|---|---|
| Run the backend and panel on your machine | [Local setup](#a-from-source-with-memory-providers) |
| Keep local data between restarts | [Docker Compose with PostgreSQL](#b-docker-compose-with-postgresql) |
| Deploy on a Linux server with HTTPS | [Server deployment guide](docs/server-deployment.md) |
| Connect a Unity game | [SDK installation and quick start](unity-sdk/Packages/com.noopsoftworks.wildcat/README.md) |
| Try matchmaking and relay with two clients | [Basic sample](unity-sdk/Packages/com.noopsoftworks.wildcat/Samples~/Basic/README.md) |

## Quick start

Clone the repository and run the commands below from its root:

```bash
git clone https://github.com/nooparmygames/WildCat.git
cd WildCat
```

For a Linux server with HTTPS and persistent storage, follow
[Run WildCat on a server](docs/server-deployment.md).

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
game key. In the Unity SDK that is `await client.ConnectAsync()` followed by
`await client.Session.InitializeAsync(developerKey, gameKey)`; the SDK's Basic sample ships a
settings asset with this URL and these keys. SMTP is not configured, so activation and password
reset codes appear in the host's log.

To open the developer panel locally, install Node 22.22 or later and run these commands in
another terminal from the repository root:

```bash
npm --prefix panel ci
npm --prefix panel run dev
```

Open `http://localhost:5173/panel/`. Register an account and use the activation code in the
host log, then sign in and create a game. To use the seeded `developer@example.com` account,
request a password reset first and read its code from the log. Memory mode loses accounts and
games on restart; use PostgreSQL when you need to keep them.

### B: Docker Compose, with PostgreSQL

You need Docker with Compose 2.24 or later, and OpenSSL (or any other source of 32 random bytes)
for the admin API's signing key.

```bash
cp .env.example .env
openssl rand -base64 32
# edit .env: replace the placeholder POSTGRES_PASSWORD with a password of your own, and set
# WILDCAT_ADMIN_JWT_SIGNING_KEY to the key the line above printed
docker compose up --build
```

`docker compose` refuses to start while the signing key (or the password) is empty and names the
variable. The signing key signs the admin API's login tokens, so anyone who has it can sign in as
any developer: keep it secret and generate a new one per installation. Changing it signs
everybody out.

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
| `WildCat:Admin:JwtSigningKey` | empty | Base64 key, at least 32 bytes, that signs the admin API's login tokens; generate one with `openssl rand -base64 32`. Required outside Development, where the host stops at startup without it; in Development an empty value means a random key per run. With Docker Compose, set `WILDCAT_ADMIN_JWT_SIGNING_KEY` in `.env` instead. The other admin settings are in [docs/admin-api.md](docs/admin-api.md). |
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
| `Gateway:AuthAttemptBurst`, `AuthAttemptsPerMinute` | `5`, `6` | Sign-in attempts (logins, account creation, codes, recovery) one connection may make back to back, and how fast they come back. |
| `Gateway:AuthAttemptsPerAddressBurst`, `AuthAttemptsPerAddressPerMinute` | `30`, `30` | The same for one client address over all its connections, so opening new sockets does not buy more guesses. `0` turns it off. The address comes from `X-Forwarded-For` only for proxies in `ForwardedHeaders:KnownProxies`/`KnownNetworks`, otherwise all clients behind a proxy share one allowance; IPv6 counts per /64 and loopback is not limited. Raise it when many players share an address. |
| `Gateway:OutboundQueueCapacity` | `1024` | Messages waiting to be written to one client; a client that stops reading and fills it is disconnected. |
| `Gateway:OutboundQueueBytes` | `8388608` | The same limit in bytes (8 MiB), so a stalled client cannot hold 1024 large messages. |
| `Gateway:RelayQueueBytes` | `4194304` | Bytes of one client's game messages waiting to be relayed (at most 256 messages); when full, the gateway stops reading that client until there is room. |
| `Gateway:MaxConcurrentRequests` | `8` | Requests of one connection handled at the same time. Relayed game messages are not counted and keep their order. |
| `ForwardedHeaders:KnownProxies` | empty | Proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` headers are trusted, besides loopback. |

## Repository layout

```
backend/                  the .NET solution (WildCat.sln)
  WildCat.Host/           the host: silo, gateway, configuration, Dockerfile
  WildCat.Gateway/        WebSocket gateway library mapped at /ws (docs/gateway.md)
  WildCat.Grains/         grain implementations, email senders, script sandbox
  WildCat.Interfaces/     grain interfaces, state classes, shared helpers
  WildCat.Protocol/       binary wire protocol (netstandard2.1, shared with the Unity SDK)
  WildCat.Tests/          xUnit tests on an in-process Orleans cluster
unity-sdk/                Unity 6 project around the SDK
  Packages/com.noopsoftworks.wildcat/   the SDK as a UPM package (Runtime, Editor, Samples~)
  Tests/                  plain .NET tests of the SDK core against the real gateway
tools/                    sync-protocol.sh (.ps1): copies the protocol into the SDK;
                          unity-package-meta.sh: writes missing Unity .meta files
deploy/postgres/          Orleans PostgreSQL scripts, run when the database is created
docs/                     design notes, see Documentation
compose.yaml              PostgreSQL plus the host
.env.example              template for .env, which compose.yaml reads
.github/workflows/        CI (GitHub Actions), see Continuous integration
```

## Running the tests

Most tests run on an in-process Orleans cluster with memory providers and need only the .NET
SDK. Some legacy tests can wait indefinitely on failure. Set a hang timeout for full runs and
filter by class while iterating:

```bash
dotnet test backend/WildCat.Tests/WildCat.Tests.csproj --blame-hang-timeout 4m
dotnet test backend/WildCat.Tests/WildCat.Tests.csproj --filter "FullyQualifiedName~LeaderBoardTests"
```

The Unity SDK's engine-free core is tested the same way, without Unity: the test project compiles
the package's Runtime/Protocol and Runtime/Core sources and runs them against the gateway on an
in-process cluster, and the CoreCheck project builds the core as Unity would (netstandard2.1, C# 9):

```bash
dotnet test unity-sdk/Tests/WildCat.Sdk.Tests/WildCat.Sdk.Tests.csproj
dotnet build unity-sdk/Tests/WildCat.Sdk.CoreCheck/WildCat.Sdk.CoreCheck.csproj
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

## Continuous integration

GitHub Actions runs [.github/workflows/ci.yml](.github/workflows/ci.yml) on every push and pull
request. A newer push to the same branch or pull request cancels the run still going for the
older one. The jobs:

- `backend`: builds `backend/WildCat.sln` in Release (which includes the Unity SDK test and
  CoreCheck projects) and runs the whole backend test suite and then the Unity SDK tests with
  `--blame-hang-timeout 4m`. A PostgreSQL 17 service container gets the scripts from
  `deploy/postgres` and `WILDCAT_TEST_POSTGRES` points at it, so the PostgreSQL provider tests run
  too. The trx results of both are kept as the `backend-test-results` artifact, also when tests
  fail.
- `compose`: follows quick start B up to `docker compose up`: copies `.env.example` to `.env`,
  sets a password and a generated signing key, and checks `compose.yaml` with
  `docker compose config -q`.
- `panel`: `npm ci`, `npm run lint`, `npm test` and `npm run build` in `panel/` on Node 22.
- `docker`: once `backend` passes, builds the host image from the same build context and
  Dockerfile as `compose.yaml`, without pushing it. It runs for pull requests and for pushes to
  the default branch only, and keeps its layers in the GitHub Actions cache.

The workflow needs no secrets. After editing it, check it with
[actionlint](https://github.com/rhysd/actionlint) from the repository root:

```bash
go install github.com/rhysd/actionlint/cmd/actionlint@latest
"$(go env GOPATH)/bin/actionlint"
```

## Unity sample

The package includes a Basic sample with login, matchmaking, relayed cube positions,
leaderboard submission through a server script, and friend invitations. Import it through
Window > Package Manager > WildCat > Samples > Basic, then follow the
[sample setup guide](unity-sdk/Packages/com.noopsoftworks.wildcat/Samples~/Basic/README.md).
It creates its UI and objects at runtime, so you add its components to an empty scene.
Use two clients for matchmaking and relay; the guide explains how to start them and configure
the score event.

## Documentation

The guides are Markdown files in this repository. Read them on GitHub or in your editor's
Markdown preview; no documentation build is required. There is no DocFX site or DocFX build
command yet. The running host generates the admin API reference at `/openapi/v1.json`.

| Guide | Covers |
|---|---|
| [Server deployment](docs/server-deployment.md) | Linux setup, HTTPS, panel hosting, accounts, backups and updates |
| [Unity SDK quick start](unity-sdk/Packages/com.noopsoftworks.wildcat/README.md) | Installation, first client script and migration from the old SDK |
| [Unity SDK manual](unity-sdk/Packages/com.noopsoftworks.wildcat/Documentation~/index.md) | Feature APIs, threading, reconnection, options and platform limits |
| [Basic sample](unity-sdk/Packages/com.noopsoftworks.wildcat/Samples~/Basic/README.md) | Running two clients and using the sample features |
| [Admin API](docs/admin-api.md) | Authentication, configuration and endpoints |
| [Developer panel](panel/README.md) | Local development, deployment and translations |
| [Wire protocol](docs/wire-protocol.md) | Framing, encodings, message IDs and shared protocol source |
| [Gateway](docs/gateway.md) | Request handling and adding a handler |
| [PostgreSQL setup](deploy/postgres/README.md) | Schema scripts and database initialization |
| [Roadmap](docs/developer-experience-roadmap.md) | Planned serialization, customization and development tools |

For repository work, read [AGENTS.md](AGENTS.md) for architecture decisions and contribution
rules. The [backend notes](docs/legacy-backend-notes.md) retain the grain inventory and known
issues. [SDK notes](docs/unity-sdk-notes.md) and the
[legacy panel survey](docs/legacy-admin-panel.md) provide implementation and migration history.

## License

MIT, see [LICENSE](LICENSE). Copyright (c) 2026 NoOpArmy Softworks. The scripts in
`deploy/postgres` come from Orleans and are MIT licensed, Copyright (c) .NET Foundation.
