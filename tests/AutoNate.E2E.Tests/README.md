# AutoNate.E2E.Tests

End-to-end browser tests using [Microsoft.Playwright](https://playwright.dev/dotnet/)
for .NET. The fixture boots `AutoNate.Web` as a child process on a random
Kestrel port (auto-login disabled, Dapr probe skipped, SpaProxy dormant), so
tests don't depend on `make app` or any external setup.

## Prerequisites

1. **Postgres up** — the app talks to Postgres on `localhost:5432`. The fixture
   uses the same `appsettings.Development.json` connection string the dev app
   uses, so anything that satisfies `make app` works here too:
   ```bash
   make infra-up   # docker-compose up -d
   ```
2. **Playwright browsers installed** — first time only, after building:
   ```bash
   dotnet build tests/AutoNate.E2E.Tests
   dotnet exec \
     --runtimeconfig tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.runtimeconfig.json \
     --depsfile tests/AutoNate.E2E.Tests/bin/Debug/net10.0/AutoNate.E2E.Tests.deps.json \
     tests/AutoNate.E2E.Tests/bin/Debug/net10.0/Microsoft.Playwright.dll install chromium
   ```

## Running

```bash
# Headless (default) — fixture builds the SPA + spawns AutoNate.Web automatically
dotnet test tests/AutoNate.E2E.Tests

# Headed — opens a real browser window
PWDEBUG=1 dotnet test tests/AutoNate.E2E.Tests
```

First run rebuilds the SPA into `wwwroot/` and warms `dotnet build`, so it can
take 30–60s before the first browser action. Subsequent runs are incremental.

## How the fixture works

`AutoNateE2EFixture` (xUnit `IClassFixture`) does the following in
`InitializeAsync`:

1. Walks up from the test bin directory to find `AutoNate.sln` (the repo root).
2. Spawns `dotnet run --project src/AutoNate.Web --no-launch-profile -p:BuildSpa=true`
   with these env vars:
   - `ASPNETCORE_URLS=http://127.0.0.1:0` — Kestrel picks a free port.
   - `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` — skips the dev startup probe.
   - `DevelopmentAutoLogin__Enabled=false` — login form is reachable.
   - `ASPNETCORE_ENVIRONMENT=Development` — keeps HTTPS redirect off. SpaProxy
     is dormant because `--no-launch-profile` leaves
     `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` unset.
3. Streams the child's stdout, parses `Now listening on: http://...`, and
   exposes the bound URL as `BaseUrl`.
4. Launches a Chromium browser via Playwright.

`DisposeAsync` kills the child process tree and closes the browser. Each test
calls `fixture.NewContextAsync()` for an isolated `BrowserContext` (no cookie
leak between tests) preconfigured with the bound `BaseURL`.

## Notes

- These tests are isolated in their own project so `dotnet test
  tests/AutoNate.Web.Tests` (the fast inner loop) doesn't pull in Playwright,
  browser binaries, or app startup.
- Tests share one DB (the dev DB). They only read or perform benign mutations
  (e.g. `last_login_date`). If you need stronger isolation later, the fixture
  has a hook for setting `ConnectionStrings__Default` to a fresh test DB.
