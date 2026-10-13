# Run WildCat on a server

This guide runs one WildCat host and PostgreSQL on a Linux server using Docker Compose.
Caddy runs on the same machine and provides HTTPS for the panel and WebSocket gateway.
WildCat is a pre-release; review the open issues in the [backend notes](legacy-backend-notes.md)
before using it for a live game.

## Prepare the server

Install Git, Docker with Compose 2.24.4 or later, Node 22.22 or later, and
[Caddy](https://caddyserver.com/docs/install). Point your domain's DNS records at the server.
Allow inbound ports 80 and 443 for Caddy, and restrict SSH access as appropriate for your host.
PostgreSQL and Orleans ports do not need public access.

Clone the repository, then choose the release tag or commit you intend to deploy:

```bash
git clone https://github.com/nooparmygames/WildCat.git
cd WildCat
git checkout <release-tag-or-commit>
cp .env.example .env
chmod 600 .env
openssl rand -hex 32
openssl rand -base64 32
```

Set `POSTGRES_PASSWORD` in `.env` to the first generated value and
`WILDCAT_ADMIN_JWT_SIGNING_KEY` to the second. Keep the environment set to Production.
Add your SMTP configuration and public address:

```dotenv
ASPNETCORE_ENVIRONMENT=Production
WildCat__Admin__PanelBaseUrl=https://games.example.com
WildCat__Email__SmtpHost=smtp.example.com
WildCat__Email__SmtpPort=587
WildCat__Email__UseSsl=true
WildCat__Email__UserName=YOUR_SMTP_USER
WildCat__Email__Password=YOUR_SMTP_PASSWORD
WildCat__Email__FromAddress=noreply@example.com
```

Replace the example domain and SMTP values. Account activation and password recovery use this
sender. Without SMTP, messages and their codes are written to the server log.

## Build the panel and start the backend

Run from the repository root:

```bash
npm --prefix panel ci
npm --prefix panel run build
docker compose -f compose.yaml -f compose.server.yaml config -q
docker compose -f compose.yaml -f compose.server.yaml up -d --build
docker compose -f compose.yaml -f compose.server.yaml ps
```

The server override mounts `panel/dist` read-only into the host and binds port 5000 to loopback.
Build the panel before starting the container. The base Docker image contains the backend only.
Always include both Compose files when managing this deployment; the base file alone publishes
port 5000 on all interfaces. The override uses Docker's
[`!override` merge rule](https://docs.docker.com/reference/compose-file/merge/#replace-value)
to replace that port mapping.

PostgreSQL initializes its schema on the first start and stores data in the `postgres-data`
volume. To inspect startup errors:

```bash
docker compose -f compose.yaml -f compose.server.yaml logs --tail 100 wildcat
```

## Enable HTTPS

For a standard Caddy service installation, add this site to `/etc/caddy/Caddyfile`:

```caddyfile
games.example.com {
    reverse_proxy 127.0.0.1:5000
}
```

Use your domain, then validate and reload the configuration:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Caddy obtains and renews the certificate when DNS points to the server and ports 80 and 443
are reachable. See [Caddy's reverse proxy guide](https://caddyserver.com/docs/quick-starts/reverse-proxy).

The backend must trust the proxy address to use forwarded client IPs for rate limiting.
With a host-installed proxy connecting through Docker's published port, this is normally the
Docker bridge gateway. Inspect the network:

```bash
docker network inspect wildcat_default --format '{{(index .IPAM.Config 0).Gateway}}'
```

Add that address to `.env` as `ForwardedHeaders__KnownProxies__0`, then run the Compose `up -d`
command again. If you change the Docker network or proxy topology, update the trusted address
to match the proxy hop seen by the backend. Trust only the proxy, not all incoming addresses.

## Create your developer account and game

Open `https://games.example.com/panel/`, register a developer account and activate it using the
email code. Sign in and create a game. Production starts without the sample developer or game.

To give a registered developer access to the developer administration page, add its email as
`WildCat__Admin__AdminEmails__0` in `.env`, recreate the host with the Compose `up -d` command,
and sign in again. An ordinary developer account can manage its own games without this role.

In the Unity settings, enter `wss://games.example.com/ws` and the developer and game keys
shown in the panel. Keep the developer secret on the server. The
[Basic sample](../unity-sdk/Packages/com.noopsoftworks.wildcat/Samples~/Basic/README.md)
explains the client flow and score event setup.

The service addresses are:

- Panel: `https://games.example.com/panel/`
- API reference: `https://games.example.com/openapi/v1.json`
- Game connection: `wss://games.example.com/ws`

## Backups and updates

Create a PostgreSQL backup from the repository root. These commands use a Bash shell:

```bash
umask 077
docker compose -f compose.yaml -f compose.server.yaml exec -T postgres \
  pg_dump -U wildcat -d wildcat -Fc > wildcat.dump
```

Keep backups outside the checkout and copy them off the server. Store `.env` separately with
restricted access. Restore backups into a separate PostgreSQL database to confirm recovery
before relying on them. Do not use `docker compose down -v` on a deployment you want to keep;
it deletes the database volume.

Before updating, back up the database and read the release's protocol and schema changes.
Check out the chosen version, rebuild the panel, and run the two-file Compose `up -d --build`
command. Clients disconnect while the single host restarts. Keep `WildCat:Orleans:ServiceId`
stable so the host continues to address the same stored state.
