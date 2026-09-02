# Development

Auton8 treats infrastructure as session-scoped and the web app as restartable.
Start the supporting services once at the beginning of a development session,
then stop and restart the app as often as you want without touching
PostgreSQL, Flowable, Redis, or the Dapr control plane.

## First-time setup

1. Install Docker Desktop.
2. Install the Dapr CLI.
3. Install the .NET 10 SDK and Node 24.
4. Copy `.env.example` to `.env` if you want to override the default local
   ports or PostgreSQL credentials.
5. Set a first administrator before the first run — see
   [First administrator](#first-administrator) below. Nothing is seeded, so
   without this there is no account to sign in with.

## Local stack

- `postgres` on `localhost:5432` with persisted data in `infra/mounts/postgres/data`
- `flowable` on `http://localhost:8080/flowable-rest`
- `redis` on `localhost:6379` for local Dapr state and pub/sub, with data in `infra/mounts/redis/data`
- `dapr-placement` on `localhost:50006`
- `dapr-scheduler` on `localhost:50007` with persisted data in `infra/mounts/dapr-scheduler/data`
- `hocuspocus` on `ws://localhost:1234` — Yjs collaboration sidecar (`services/hocuspocus`)
- `executor` — code-transformer / analyzer sandbox (`services/executor`); no ports, it serves `pipeline-code-run.>` over NATS and reports health via a NATS probe
- `dapr-dashboard` on `http://localhost:8081` when you start the `dashboard` profile, reading components from `infra/mounts/dapr-dashboard/components`

## Daily workflow

Start shared infrastructure once per session:

```bash
make infra-up
```

Or use the idempotent guard that starts the stack only when required and waits until it is ready:

```bash
make infra-ensure
```

Or include the Dapr dashboard:

```bash
make infra-up-dashboard
```

The `make infra-up`, `make infra-up-dashboard`, and `make app-dapr` commands prepare the bind-mounted directories under `infra/mounts/` automatically before starting anything.

Run the app directly:

```bash
make app
```

`make app` now ensures the required Docker Compose services are up and ready, then launches `AutoNate.Web` with its Dapr sidecar.
Use this as the default local app-start path.

For Rider, the repo includes shareable run configurations under `.run/`:

- `infra: Local Stack` starts the Docker Compose infrastructure and shows it in Rider's Services window.
- `infra: Ensure Up` runs `infra/ensure-up.sh` so the local stack is started only when needed and waits for readiness before continuing.
- `dapr: AutoNate.Web Sidecar` starts only the local Dapr sidecar for the web app. Run this once before starting a Rider debug session.
- `dapr: AutoNate.Web Sidecar Status` checks that the `autonate-web` sidecar is already running and fails fast with a clear message if it is not.
- `AutoNate.Web: Rider` is the debugger path. It runs `infra: Ensure Up`, then `dapr: AutoNate.Web Sidecar Status`, and then launches the `http` launch profile as a normal Rider .NET debug session.
- `AutoNate.Web: Dapr Run` is the shell-style Rider run config that mirrors `make app-dapr` for terminal parity.

For stable Rider debugging, use this two-step flow:

1. Run `dapr: AutoNate.Web Sidecar`.
2. Start `AutoNate.Web: Rider`.

This avoids Rider shell-script before-launch issues while still giving the app a Dapr sidecar and preserving normal breakpoint debugging. If the sidecar is not running, the Rider app config now fails before launch with an explicit sidecar-status error instead of starting the app without Dapr.

Run the app with a Dapr sidecar:

```bash
make app-dapr
```

`make app-dapr` uses the same infra readiness check before starting the app-scoped Dapr sidecar.
It is now equivalent to `make app` for terminal usage.
Use this mode when you need Dapr pub/sub delivery, including the Bus Watcher page and workflow execution live updates.
The Rider debugger flow intentionally does not use `make app-dapr`, because Rider needs to own the `dotnet` process directly to support normal breakpoint debugging.

Stop only the app when you are done iterating. Leave infrastructure running until you want to end the session.

When ending the session:

```bash
make infra-down
```

If you intentionally want a full reset, including PostgreSQL, Redis, and Dapr scheduler data:

```bash
make infra-reset
```

`make infra-reset` now clears the bind-mounted data directories under `infra/mounts/` and recreates the expected folder structure. It no longer relies on Docker named volumes.

## Build and test

```bash
dotnet build AutoNate.sln                      # analyzers run on every build (see Directory.Build.props / .editorconfig)
cd src/AutoNate.Spa && npm ci && npm run lint && npm run build
cd infra && docker compose -p infra up -d postgres nats nats-init redis   # test suite needs these three
dotnet test AutoNate.sln                       # ~8 min; integration tests hit the compose services
```

Planning and issue workflow are managed with n8SDLC — GitHub Issues and milestones are the plan, `.n8/` holds config, the decision log and harvested audit checklists.

## First administrator

Nothing is seeded. A fresh database has no users, and there is no registration
page or setup wizard — `POST /api/users` requires an authenticated caller, so
the first account has to come from configuration.

Set both of these before the first startup against an empty database:

```bash
export Bootstrap__AdminUsername=youradmin
export Bootstrap__AdminPassword='a password you choose'
```

On startup, if `local_users` is empty and both values are set, the app creates
that one account and grants it SuperAdmin. If either is missing it creates
nothing and logs a warning naming the two settings. If any user already
exists it does nothing at all, so leaving the variables set across restarts is
harmless — and cannot be used to add a second privileged account to a running
install.

Optional: `Bootstrap__AdminEmail` (defaults to `<username>@localhost`) and
`Bootstrap__GrantSuperAdmin=false` if you want the account created without
privilege.

There is deliberately no default password. Earlier versions seeded an `admin`
account whose hash and salt were committed to this repository; if you are
upgrading from one of those, change that password immediately — it is public.

## Local configuration

`src/AutoNate.Web/appsettings.Development.json` defines the stable local endpoints for:

- `ConnectionStrings:Default`
- `Flowable:BaseUrl`
- `Dapr:AppId`
- `Dapr:HttpEndpoint`
- `Dapr:GrpcEndpoint`
- `Dapr:PlacementHostAddress`
- `Dapr:SchedulerHostAddress`
- `Dapr:StateStoreName`
- `Dapr:PubSubName`
- `WorkflowBehaviors:CallbackSharedSecret` — shared secret the Flowable JVM presents on the workflow-behavior callback endpoint. The flowable-extension reads the same value from `autonate.flowable-events.callback-shared-secret` and `autonate.flowable-events.callback-base-url`. The dev appsettings.json carries a placeholder; non-Development environments refuse to start when this is unset.

The checked-in defaults use development-only PostgreSQL credentials. Override secrets locally with environment variables when needed:

```bash
export AUTONATE_POSTGRES_USER='autonate'
export AUTONATE_POSTGRES_PASSWORD='Your_strong_password_here!'
export ConnectionStrings__Default='Host=localhost;Port=5432;Database=AutoNate;Username=autonate;Password=Your_strong_password_here!'
```

## Notes

- The Dapr sidecar is intentionally not part of Docker Compose. It is app-scoped and should start and stop with the app process.
- In Development, `AutoNate.Web` now fails fast when no local Dapr sidecar is reachable. Set `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` only when you intentionally want to bypass event-driven features.
- `infra/ensure-up.sh` remains the reusable terminal entrypoint for local workflows outside Rider. It verifies `postgres`, `redis`, `flowable`, `dapr-placement`, and `dapr-scheduler`, starts them if needed, and waits for readiness before returning.
- `src/AutoNate.Web/Program.cs` also mirrors the `launchSettings.json` local defaults for direct Debug executable launches, so Rider's plain `.NET Project` launcher still uses `Development` and `http://localhost:5108` unless you explicitly override them with environment variables.
- `infra/docker-compose.yml` now includes a health check for `flowable`, so Docker Compose and Rider can surface when that service is actually ready instead of merely started.
- Container mounts are standardized under `infra/mounts/<service>/<purpose>`.
- The `infra/mounts` tree is intentionally not tracked in git, except for placeholder `.gitkeep` files that preserve the service layout.
- `.run/` is intentionally tracked in git. The Rider run configurations under it (`infra: Local Stack`, `dapr: AutoNate.Web Sidecar`, `AutoNate.Web: Rider`, etc.) are shared across the team — change them deliberately.
- `/temp/` (root only) is intentionally not tracked in git. Use it as a scratchpad for debug screenshots, captured logs, and other throwaway artifacts that don't belong in the repo.
- `infra/dapr/components` remains the tracked source of truth for component YAML, and the Makefile mirrors those files into `infra/mounts/dapr-dashboard/components` for the dashboard and local `dapr run` workflow.
- The mounted component files point Dapr to `localhost`, because the sidecar runs on the host while Redis runs in Docker with published ports.
- The Postgres init script creates the `AutoNate` database while `flowable` remains the default database created by the Postgres image.
- The custom Flowable image adds the PostgreSQL JDBC driver so Flowable can use PostgreSQL instead of the in-memory default.
