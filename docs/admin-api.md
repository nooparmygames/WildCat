# Admin REST API

The HTTP API behind the developer panel, added in roadmap step 7. It lives in
`backend/WildCat.Admin` and runs inside the host process next to the game gateway, calling grains
through the silo's own `IGrainFactory`. The contract started from the endpoint list in
`legacy-admin-panel.md`; this page is the current one. The OpenAPI document at `/openapi/v1.json`
is generated from the code and is the exact reference.

## Hosting

`WildCat.Host/Program.cs` wires it in with four lines around the gateway:

```csharp
builder.Services.AddWildCatAdmin(builder.Configuration);
...
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapWildCatAdmin();
```

`AddWildCatAdmin` also adds a startup filter that, for `/api/v1` requests only, turns unhandled
exceptions and empty error responses into problem details and applies the CORS policy. When the
content root has `wwwroot/panel/index.html` (a Docker image can copy the panel build there),
`MapWildCatAdmin` serves it at `/panel` and answers unknown paths under it with `index.html`, so
the panel's client-side routes survive a reload.

## Configuration (`WildCat:Admin`)

| Key | Meaning |
|---|---|
| `JwtSigningKey` | Base64 HMAC-SHA256 key, at least 32 bytes (`openssl rand -base64 32`). Required outside Development; in Development an empty value means a random key per run and a warning that logins do not survive a restart. |
| `AdminEmails` | Developers with these emails get the admin role. Checked at login and again on every admin request. |
| `CorsOrigins` | Browser origins allowed to call the API cross-origin, such as `http://localhost:5173` (set in `appsettings.Development.json`). The Vite dev server proxies `/api`, so it works without this too. |
| `PanelBaseUrl` | Optional public address of the site serving the panel, such as `https://games.example.com`. When set, emails link to `{PanelBaseUrl}/panel/activate?email=&code=` and `{PanelBaseUrl}/panel/reset-password?email=&code=`; empty means the emails carry only the code. |
| `AuthRequestsPerMinute` | Rate limit for the anonymous `/auth` endpoints per client address (IPv6 per /64), default 10. |

Set real values through user secrets or environment variables (`WildCat__Admin__JwtSigningKey`,
`WildCat__Admin__AdminEmails__0`). With Docker Compose, `compose.yaml` at the repository root
passes `WILDCAT_ADMIN_JWT_SIGNING_KEY` from `.env` to the host as `WildCat__Admin__JwtSigningKey`
and refuses to start while it is empty; quick start B in the README shows how to generate it.
Account emails go through the `IEmailSender` the host registers; without one they are logged.

## Conventions

- JSON with camelCase names. Game keys and developer keys are GUID strings.
- Authentication: `Authorization: Bearer <token>` from `POST /auth/login`. Tokens are HS256 JWTs
  with issuer and audience `wildcat`, valid for eight hours, with claims `sub` (developer key),
  `email` and `role` (`developer` or `admin`).
- Errors are RFC 7807 problem details (`application/problem+json`) with a `detail` meant for
  people. Validation errors are 400 with an `errors` array listing every message. 401 means no or
  a bad token, 403 means not allowed, 404 not found, 409 a name or email that is taken, 429 too
  many `/auth` requests (with `Retry-After`).
- Names: leaderboard, item and game mode names are 1 to 64 letters, digits, `_`, `-` or `.`.
  Script event and parameter names are C# identifiers of up to 64 characters.
- Passwords: 8 to 256 characters, sent in plain text over TLS and hashed on the server (PBKDF2).
- Responses never include password or code hashes; only `/me` returns the developer secret.

## Endpoints

All paths start with `/api/v1`. "Game" routes check that the game belongs to the caller (403
otherwise, 404 for an unknown game); admins may open any game.

| Method and path | Auth | Body | Response |
|---|---|---|---|
| POST `/auth/register` | anonymous, rate limited | `{email, password, firstName?, lastName?}` | 202; 409 if the email is taken (see Not there yet). Emails an activation code. |
| POST `/auth/activate` | anonymous, rate limited | `{email, code}` | 204; 400 for a wrong or expired code (7 days, 5 tries). The code is checked for an account that is already active too, with the same 400 for a wrong code, so the answer does not tell whether an account is active; the code that activated it keeps answering 204 until it expires. |
| POST `/auth/resend-activation` | anonymous, rate limited | `{email}` | always 202; emails a new code to an inactive account |
| POST `/auth/login` | anonymous, rate limited | `{email, password}` | `{token, expiresAt, developer: {key, email, firstName, lastName, isAdmin}}`; 401 for a wrong email or password (same message for both); 403 for an account that is not activated or is disabled |
| POST `/auth/request-password-reset` | anonymous, rate limited | `{email}` | always 202; emails a reset code (1 hour, 5 tries) if the account exists |
| POST `/auth/reset-password` | anonymous, rate limited | `{email, code, newPassword}` | 204; 400 for a wrong or expired code |
| POST `/auth/change-password` | developer | `{oldPassword, newPassword}` | 204; 400 (not 401) if the old password is wrong |
| GET `/me` | developer | | `{key, email, firstName, lastName, isAdmin, secret}` |
| PUT `/me` | developer | `{firstName, lastName}` | the updated `/me` object |
| POST `/me/secret` | developer | | `{secret}`, the new developer secret; the old one stops working |
| GET `/games` | developer | | `[game]` ordered by name |
| POST `/games` | developer | game fields | 201 `{key}` with a `Location` header; 409 for a duplicate name |
| GET `/games/{gameKey}` | game | | `game` |
| PUT `/games/{gameKey}` | game | game fields | the updated `game` |
| DELETE `/games/{gameKey}` | game | | 204 |
| GET `/games/{gameKey}/leaderboards` | game | | `[{name, key, bestScore}]` |
| POST `/games/{gameKey}/leaderboards` | game | `{name, bestScore}` | 201 `{name, key, bestScore}`; 409 for a duplicate |
| DELETE `/games/{gameKey}/leaderboards/{name}` | game | | 204; clears the stored scores |
| GET `/games/{gameKey}/items` | game | | `[item]` |
| PUT `/games/{gameKey}/items/{name}` | game | item fields | `item` (creates or replaces) |
| DELETE `/games/{gameKey}/items/{name}` | game | | 204; inventories keep their stacks |
| GET `/games/{gameKey}/events` | game | | `[{name, parameters: [{name, type}], script}]` |
| PUT `/games/{gameKey}/events/{name}` | game | `{parameters, script}` | the saved event; 400 with the compiler's messages in `errors`. The script is checked against the parameters in the request, so an existing event can change its parameter list. |
| DELETE `/games/{gameKey}/events/{name}` | game | | 204; 404 if missing. Deletes the event's stored script and compiled copy; game clients that still run it get an error, as for an event that never existed. |
| GET `/games/{gameKey}/game-modes` | game | | `[mode]` |
| PUT `/games/{gameKey}/game-modes/{name}` | game | mode fields | `mode` (creates or replaces) |
| DELETE `/games/{gameKey}/game-modes/{name}` | game | | 204 |
| GET `/publisher-data` | developer | | `[{key, value}]` ordered by key |
| PUT `/publisher-data/{key}` | developer | `{value}` | `{key, value}` |
| DELETE `/publisher-data/{key}` | developer | | 204 |
| GET `/admin/developers?search=&page=1&pageSize=25` | admin | | `{items: [{key, email, firstName, lastName, isActivated}], total}`; search matches part of the email, pageSize 1 to 100 |

Shapes:

- game: `{key, name, description, studioName, website, genre, region, platform, ageRating}`.
  `name` is required (up to 100 characters), `website` an absolute http(s) address, `genre` 0
  Action, 1 Arcade, 2 Strategy, 3 Puzzle, 4 Casual, 5 Sports; `region` 0 Domestic, 1 Foreign;
  `ageRating` 0 All, 1 Children, 2 Teens, 3 Adults. The request is the same without `key`.
- item: `{name, description, effectTime, stackable, maxAllowedToHave, isConsumable,
  automaticallyConsumed, isRegenerating, regenerationAmount, regenerationPeriod, metaData:
  {name: value}, price: [{item, count}]}`. `effectTime` and `regenerationPeriod` are TimeSpan
  strings in the invariant `[d.]hh:mm:ss` form, such as `"1.02:03:04"`, from zero to 3650 days;
  left out means zero. Price rows name other items of the same game, each once, with a count of
  at least 1. The request is the same without `name` (it is in the path).
- event parameter types are numbers in `EventParameter.ParamType` order: 0 Bool, 1 Byte, 2 Short,
  3 Int, 4 Long, 5 Float, 6 Double, 7 String, 8 ByteArray. The script must declare a method named
  after the event.
- mode: `{name, minPlayers, maxPlayers, finishGameWithOneDisconnect, criteria:
  [{isMinPlayersEnough, acceptableThreshold, startTimeInSeconds}]}`. Players 2 to 1000, max not
  below min, 1 to 20 criteria rows, the first at 0 seconds and each later row later than the one
  before. `finishGameWithOneDisconnect` is optional in the request (an existing mode keeps its
  value, a new one gets true).

## Not there yet

- Email enumeration through registration: `POST /auth/register` answers 409 for an email that
  already has an account, so anyone can find out whether an address is registered. This is a
  known trade-off for a clear sign-up form; the `/auth` rate limit (`AuthRequestsPerMinute` per
  address) keeps probing slow. The other `/auth` endpoints give the same answer for a known and
  an unknown email.
- Structured compiler diagnostics: a rejected script's `errors` holds one line that summarizes
  the first three errors (each with its id, such as `WC1007` or `CS0103`, and its line and column
  when it has one), not a separate entry per diagnostic.
- Deleting a game, leaderboard or item removes it from the catalog; stored grain state for the
  game's resources stays behind.
- Tokens cannot be revoked before they expire; a password change does not end other sessions.
