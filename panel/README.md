# WildCat panel

The developer panel for WildCat: sign up and sign in, manage games and their keys, and edit each
game's leaderboards, items, script events and game modes. It also covers publisher data (key and
value pairs shared by all of a developer's games) and, for admins, the list of registered
developers.

It is a single page app built with Vite, React, React Router and TypeScript. There is no UI
framework; the styles are plain CSS in `src/styles.css` and follow the system light or dark
setting. The script editor uses CodeMirror 6 with the C# mode from `@codemirror/legacy-modes`
and loads only when someone opens it.

## Commands

Node 22.22 or later.

```bash
npm ci            # install exactly what package-lock.json says
npm run dev       # dev server at http://localhost:5173/panel/
npm run build     # type check, then build into dist/
npm run preview   # serve dist/ at http://localhost:4173/panel/
npm run lint      # eslint, no warnings allowed
npm test          # vitest unit tests
```

## Talking to the API

The panel calls the admin API under `/api/v1` on the same origin. In development the Vite dev
server proxies `/api` to the WildCat host at `http://localhost:5000`, so start the host first:

```bash
dotnet run --project backend/WildCat.Host
```

Two settings change this:

- `VITE_API_BASE` (build time): the API base URL the panel calls. Defaults to `/api/v1`.
- `WILDCAT_API_URL` (dev server only): where `/api` is proxied. Defaults to
  `http://localhost:5000`.

Both can be set in the environment or in a `.env.local` file next to this README (the repo
ignores `.env` files).

The sign-in token is kept in `sessionStorage`, so it lasts until the tab is closed. A 401 caused
by a rejected token signs the developer out; a 403 shows a no-permission page.

## Deploying

`npm run build` writes a static site to `dist/` that expects to be served under `/panel/`. The
WildCat host serves it from `wwwroot/panel` in its content root when that folder holds an
`index.html`, falling back to `index.html` for the app's own routes. Links in account emails
point at `/panel/activate?email=...&code=...` and `/panel/reset-password?email=...&code=...`.

## Layout

```
src/
  api/          fetch wrapper (client.ts), endpoint functions and response normalizers (index.ts), types
  auth/         session storage, auth context, route guards
  components/   shell, dialogs, form fields, notices, small shared pieces
  i18n/         the translation helper and en.json
  lib/          validation rules, TimeSpan helpers, data loading hook
  pages/        one folder or file per screen
```

## Translations

Every string a person reads comes from `src/i18n/en.json` through `t('some.key')`. Keys are
type checked against the English file. To add a language, copy `en.json` to, say, `fa.json`,
translate the values, register it in `catalogs` in `src/i18n/index.ts` and call `setLocale`.
Missing keys fall back to English. A right-to-left language such as Persian also needs
`dir="rtl"` on the html element, and the styles have not been checked in that direction yet.
