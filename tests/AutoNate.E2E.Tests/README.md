# AutoNate.E2E.Tests

End-to-end browser tests using [Microsoft.Playwright](https://playwright.dev/dotnet/)
for .NET. The fixture (`AutoNateE2EFixture`) boots `AutoNate.Web` as a child
process on a random Kestrel port, against a freshly-created **dedicated
ephemeral Postgres database** (`AutoNate_E2E`) — so destructive flows (delete
a record, delete all executions) are safe, the developer's working `AutoNate`
database is left untouched, and every run starts from a known-good seeded
slate.

## Prerequisites

1. **Postgres up.** The fixture creates `AutoNate_E2E` against the same
   `localhost:5432` Postgres that backs `make app`, so anything that satisfies
   `make app` works here too:
   ```bash
   make infra-up   # docker-compose up -d
   ```
2. **Playwright browsers installed.** First time only; the `make e2e` target
   below handles this for you, or run by hand once after building the test
   project:
   ```bash
   dotnet build tests/AutoNate.E2E.Tests
   dotnet exec \
     --runtimeconfig tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.runtimeconfig.json \
     --depsfile tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.deps.json \
     tests/AutoNate.E2E.Tests/bin/Debug/net10.0/Microsoft.Playwright.dll install chromium
   ```

## Running

```bash
# Recommended: one command that ensures infra + browsers + tests
make e2e

# Or, step-by-step (equivalent):
make infra-ensure
dotnet build tests/AutoNate.E2E.Tests
dotnet test tests/AutoNate.E2E.Tests

# Headed — opens a real browser window
PWDEBUG=1 dotnet test tests/AutoNate.E2E.Tests
```

First run rebuilds the SPA into `wwwroot/` and warms `dotnet build`, so it can
take 30–60s before the first browser action. Subsequent runs are incremental.

## Test database

Each fixture run:

1. Connects to the `postgres` maintenance database.
2. `DROP DATABASE IF EXISTS "AutoNate_E2E" WITH (FORCE);` then
   `CREATE DATABASE "AutoNate_E2E";` — so the run starts clean even if a
   previous run crashed.
3. Replays `infra/postgres/init/02-create-autonate-app-schema.sql` against the
   new database. That script creates the foundational schema (including
   `local_users`) and seeds the **only** built-in account:
   - `admin` / `admin` — super-admin. There is **no `user1`**; the dev seed has
     never included one. To exercise limited-permission behavior, tests create
     a user at runtime via `POST /api/users/` (helpers under `Support/`).
4. Starts `AutoNate.Web` with `ConnectionStrings__Default` overridden to point
   at `AutoNate_E2E`. The app's `DatabaseSchemaInitializer.EnsureAsync`
   finishes the schema (lockout columns, roles, menus, sample project) and the
   SuperAdmin backfill makes the seeded `admin` row a super-admin.

The test database is **left behind** between runs (the next run drops it). To
inspect it after a failure:

```bash
docker exec autonate-postgres psql -U autonate -d AutoNate_E2E -c "\dt"
```

## How the fixture works

`AutoNateE2EFixture` (xUnit collection fixture) does the following in
`InitializeAsync`:

1. Walks up from the test bin directory to find `AutoNate.sln` (the repo root).
2. Bootstraps `AutoNate_E2E` (see above).
3. Spawns `dotnet run --project src/AutoNate.Web --no-launch-profile -p:BuildSpa=true`
   with these env vars:
   - `ASPNETCORE_URLS=http://127.0.0.1:0` — Kestrel picks a free port.
   - `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` — skips the dev startup probe.
   - `DevelopmentAutoLogin__Enabled=false` — login form is reachable.
   - `ASPNETCORE_ENVIRONMENT=Development` — keeps HTTPS redirect off. SpaProxy
     is dormant because `--no-launch-profile` leaves
     `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` unset.
   - `ConnectionStrings__Default=…AutoNate_E2E…` — the ephemeral test DB.
4. Streams the child's stdout, parses `Now listening on: http://...`, and
   exposes the bound URL as `BaseUrl`.
5. Launches a Chromium browser via Playwright.

`DisposeAsync` kills the child process tree and closes the browser. Each test
calls `fixture.NewContextAsync()` for an isolated `BrowserContext` (no cookie
leak between tests) preconfigured with the bound `BaseURL`.

The fixture is wired as a **collection** fixture (not `IClassFixture`) so all
E2E test classes share one app boot — per-class would race three concurrent
`dotnet run -p:BuildSpa=true` invocations on `wwwroot/`.

## Helpers under `Support/`

- **`E2ETestBase`** — abstract base; inheriting classes pick up the collection
  attribute automatically and gain `NewSignedInAsAdminAsync()` which returns a
  disposable `SignedInSession { Context, Page }` already signed in at `/home`.
- **`ApiSeeder(IAPIRequestContext)`** — thin wrapper around the signed-in
  Playwright API request context for creating prerequisite data fast (record
  types, records, users, grants). New methods are added incrementally as test
  phases need them; see `docs/plans/2026-05-29-playwright-e2e-coverage.md`.
- **`TestNames.ShortSlug()` / `Prefixed(prefix)`** — unique-name helpers so
  tests within a single run don't collide on UNIQUE columns.

## Notes

- These tests are isolated in their own project so `dotnet test
  tests/AutoNate.Web.Tests` (the fast inner loop) doesn't pull in Playwright,
  browser binaries, or app startup.
- The dev `autonate` Postgres role is the cluster superuser (see
  `infra/docker-compose.yml`) and therefore has `CREATEDB`. If a future infra
  change demotes it, grant `CREATEDB` to it explicitly or point the bootstrap
  at a maintenance role that has it.
- Running two E2E suites in parallel against the same Postgres is not
  supported (both would target `AutoNate_E2E`). The test runner uses a single
  shared collection fixture, so this only matters if you launch two
  `dotnet test` invocations side-by-side.
