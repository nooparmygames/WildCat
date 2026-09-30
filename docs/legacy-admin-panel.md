# Legacy admin panel: what it did, and the API it expected

This is the rewrite reference for the developer panel. The old panel (an AngularJS 1.x app built
on the commercial "Fuse" theme, Persian RTL, gulp/bower toolchain) is not copied into this repo:
the theme is a paid template and cannot be redistributed, the toolchain is dead, and the code was
already half broken. Only this document survives. Survey date 2026-09-30; updated after roadmap
step 2 dropped billing, which removed the usage dashboard from the contract.

## Implemented in step 7

The new admin API is in `backend/WildCat.Admin`, co-hosted with the silo; `admin-api.md` lists the
final endpoints, configuration and error format. Compared with the legacy contract below: every
route is under `/api/v1` with real HTTP verbs (PUT upserts, DELETE deletes, no `.../delete`
POSTs); auth is `/auth/register`, `/auth/activate`, `/auth/resend-activation`, `/auth/login`,
`/auth/request-password-reset`, `/auth/reset-password` and `/auth/change-password` with plain
passwords; the profile moved to `/me` (with `isAdmin` instead of `type`); game modes are at
`/games/{gameKey}/game-modes` with a `criteria` array; publisher data is at
`/publisher-data/{key}`; `/admin/developers` pages and searches; errors are problem details with
`detail`. Script events gained a DELETE, which empties the script until step 9 can clear it.

## The legacy contract

One important finding: the REST API this panel calls (`/api/v1/...`) does not exist in the backend
in this repo. The backend's gateway (`WildCat.Gateway`) only exposes the game WebSocket at `/ws`.
The HTTP admin API lived in some other project that was never checked in. So the panel rewrite
also needs a new admin HTTP API on the backend side. Treat the endpoint table below as the
contract the old panel expected, not as something that exists.

## What the panel really was

The real product was: auth (register with email activation, login, forgot/reset password), a
games list, a per-game config area with five tabs, a developer-wide key/value page, and an
admin-only developer list. Everything else (invoice, profile, quick panel chat, error pages,
theme switcher) was Fuse demo content.

Side navigation had three items: Home (dashboard), Games, Publisher data. Admins reached the
Developers page through a toolbar link only.

## Pages

### Auth pages

Login: email + password (+ an ignored "remember me"). Flow: POST `/token` with the MD5 of the
password, store the bearer token, GET `/developers` for the profile, and if `type == "admin"`
go to the developers list, otherwise to the dashboard. HTTP 400 showed "username or password is
wrong".

Register: email, password, confirm. POST `/developers` with `{email, passwordHash: md5(pw)}`.
Success said an activation email was sent.

Forgot password: POST `/developers/requestresetpassword` with `{email}`; the response body was a
plain string shown in a dialog.

Reset password at `/page/reset/:code` (singular "page", this URL is emailed by the backend):
POST `/developers/resetpassword/` with `{resetKey, oldPassword?, newPassword: md5(pw), email}`.

Activation at `/page/activation/:code` (also emailed): POST `/developers/activate/:code` on load.

The client-side MD5 is a legacy quirk. The backend hashed again with unsalted SHA1. Since
step 4 the Developer grain stores a salted PBKDF2 hash (`PasswordHasher`) of whatever it
receives, so the new panel sends the plain password and relies on TLS in transit.

### Developers (admin only, `/developers`)

GET `/admin/developers` returned `[{firstName, lastName, email, phoneNumber, key}]`. Table with
full name, email, phone, key, and a link to `/dashboard?developerId=<key>` (the dropped usage
dashboard). Pagination and search were intended but never wired. No client-side route guard;
the server enforced admin access.

### Games (`/games`)

GET `/games` returned an array of games. Columns: name, key, info, edit, delete.

Info dialog: game key, developer key, developer secret, each with a copy button. These are the
SDK credentials a game needs.

Add/edit dialog fields: `name` (required), `description`, `studioName`, `website`,
`genre` (0 Action, 1 Arcade, 2 Strategy, 3 Puzzle, 4 Casual, 5 Sports), `region` (0 Domestic,
1 Foreign), `platform` (free text), `ageRating` (0 All, 1 Children, 2 Teens, 3 Adults).
These map to `WildCat.Interfaces/GameInfo.cs`. Create: POST `/games` (response body is the new
key). Update: POST `/games/:key`. Delete: POST `/games/:key/delete` after a confirm.

### Game config shell (`/games/:id`)

GET `/games/:id` for the header. Tabs: Leaderboards, Items, In-app purchase, Events, Game modes.

Leaderboards: GET `/games/:g/leaderboards` returns `[{name, key, bestScore}]`. Create with
`{name, bestScore}` (best score vs last score). Delete: POST `/games/:g/leaderboards/:name/delete`
(passed the name, not the key).

Items (virtual goods, maps to `WildCat.Interfaces/IItem.cs` and `IPackageContainer.cs`): GET and
POST `/games/:g/items`. Dialog fields: name, description, effect time (min/hour/day), stackable,
maxAllowedToHave, isConsumable (+ automaticallyConsumed, spelled automaticallyConsummed by the old
panel), isRegenerating (+ regenerationAmount, regenerationPeriod), metaData rows `{name, value}`,
price rows `{item, count}` (priced in other items). Only list and create were wired; edit and
delete called API functions that did not exist and the payload had several binding bugs. Confirm
the wire format against the grain interfaces before building this tab.

In-app purchase: CafeBazaar OAuth only. Dropped. Do not rebuild.

Events (server-side script events, maps to `WildCat.Interfaces/IScriptEventCompiler.cs`): GET
`/games/:g/events` returns `[{name, parameters: [{name, type}], script}]`. Dialog: event name
(the key, read-only when editing), an ordered parameter table with types Bool, Byte, Short, Int,
Long, Float, Double, String, ByteArray, and a C# code editor (Ace, csharp mode) for the script.
Upsert with POST `/games/:g/events`. No delete existed.

Game modes (matchmaking, maps to `WildCat.Interfaces/IGameModeManager.cs`): GET
`/games/:g/matchmakings/modes` returns `[{name, minPlayers, maxPlayers, criterias: [
{isMinPlayersEnough, acceptableThreshold, startTimeInSeconds}]}]`. min/max players 2..1000. The
criteria rows form a relaxation schedule: at `startTimeInSeconds` after joining the queue, accept a
match if the skill gap is within `acceptableThreshold`, and optionally start with only the minimum
player count. The first row is fixed at 0 seconds. Upsert with POST, delete with HTTP DELETE
`/games/:g/matchmakings/modes/:name` (the old delete was broken).

Achievements: an empty stub, never enabled. Not a feature.

### Publisher data (`/publisher-data`)

Developer-scoped key/value store (maps to `WildCat.Interfaces/IDeveloperStorage.cs`). GET
`/developers/publisherData` returns `[{key, value}]`. Upsert: POST `/developers/publisherData` with
`{key, value}`. Delete: POST `/developers/publisherData/:key/delete`.

## API conventions the old panel assumed

- Base URL was configured in code (`http://localhost:58080/api/v1`); move it to config.
- Bearer token in `Authorization`. 401 logged the user out; 403 showed a "no permission" page.
- Deletes were POST `.../:id/delete`, except game modes which used a real DELETE. Pick one.
- Create and update were mostly a single upsert POST. Errors carried `{message}`.
- The only role signal was `type` on the GET `/developers` response ("admin" or anything else).

## Rebuild list

Keep: auth flow (with a modern password hash instead of client-side MD5), role-aware shell with
real route guards, admin developer list with pagination, games CRUD plus the credentials dialog,
leaderboards, items, script events, game modes, publisher data. Persian and English UI text (the
old panel defaulted to Persian; ship English as the default for open source and keep the strings in
a translation file).

Drop: usage dashboard (per-game call and message counters from GET `/developers/accounting` and
GET `/admin/developers/:id/accounting`; the billing counters are gone, and usage metrics may come
back later through OpenTelemetry), CafeBazaar IAP tab and its WebSocket/callback endpoints,
achievements stub, invoice and profile demo pages, quick panel, Fuse layout switcher and theming,
stock Fuse error pages.
