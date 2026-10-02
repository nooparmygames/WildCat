# Orleans PostgreSQL scripts

These scripts create the tables, functions and queries the Orleans ADO.NET providers use in
PostgreSQL mode (`WildCat:Orleans:Provider` set to `PostgreSQL`). They are copied unchanged from
the dotnet/orleans repository at tag `v10.3.1`, which matches the Orleans packages in
`backend/Directory.Packages.props`:

| File | Source path in dotnet/orleans | Creates |
|---|---|---|
| `01-main.sql` | `src/AdoNet/Shared/PostgreSQL-Main.sql` | `OrleansQuery`, the query table the other scripts fill |
| `02-clustering.sql` | `src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql` | `OrleansMembershipVersionTable`, `OrleansMembershipTable` |
| `03-persistence.sql` | `src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql` | `OrleansStorage` (grain state for every storage provider) |
| `04-reminders.sql` | `src/AdoNet/Orleans.Reminders.AdoNet/PostgreSQL-Reminders.sql` | `OrleansRemindersTable` |

They must run in this order, once, against an empty database. `compose.yaml` mounts this folder
at `/docker-entrypoint-initdb.d`, and the postgres image runs the `.sql` files there in name order
the first time it initializes its data volume (it skips this README). They do not run again on
later starts, so deleting the volume (`docker compose down -v`) is the way to start over. For a
database you manage yourself, run them in order as the user WildCat connects with, for example:

```bash
for f in deploy/postgres/*.sql; do psql -v ON_ERROR_STOP=1 -h <host> -U wildcat -d wildcat -f "$f"; done
```

When the Orleans packages move to a new version, compare these files with the scripts at the new
tag. A fresh database can take the new scripts as they are; an existing one needs the migration
scripts Orleans ships next to them, if any.

License: MIT, Copyright (c) .NET Foundation, the license of the dotnet/orleans repository:

```
The MIT License (MIT)

Copyright (c) .NET Foundation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
