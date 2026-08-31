# AutoNate Local Development

AutoNate now treats infrastructure as session-scoped and the web app as restartable. Start the supporting services once at the beginning of a development session, then stop and restart the app as often as you want without touching PostgreSQL, Flowable, Redis, or the Dapr control plane.

## Local stack

- `postgres` on `localhost:5432` with persisted data in `infra/mounts/postgres/data`
- `flowable` on `http://localhost:8080/flowable-rest`
- `redis` on `localhost:6379` for local Dapr state and pub/sub, with data in `infra/mounts/redis/data`
- `dapr-placement` on `localhost:50006`
- `dapr-scheduler` on `localhost:50007` with persisted data in `infra/mounts/dapr-scheduler/data`
- `hocuspocus` on `ws://localhost:1234` — Yjs collaboration sidecar (`services/hocuspocus`)
- `executor` — code-transformer / analyzer sandbox (`services/executor`); no ports, it serves `pipeline-code-run.>` over NATS and reports health via a NATS probe
- `dapr-dashboard` on `http://localhost:8081` when you start the `dashboard` profile, reading components from `infra/mounts/dapr-dashboard/components`

## First-time setup

1. Install Docker Desktop.
2. Install the Dapr CLI.
3. Copy `.env.example` to `.env` if you want to override the default local ports or PostgreSQL credentials.

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

## Deployment configuration

The dev defaults under `appsettings.Development.json` are tuned for a single-machine `Development` environment with the local Docker Compose stack. Production deployments must override the keys below — most have safe-but-permissive dev defaults that are wrong for any environment that's reachable from outside the host. Override with environment variables (double-underscore syntax: `Section__Subsection__Key=value`) or an environment-specific `appsettings.<Environment>.json`.

### Required overrides

These either ship with insecure defaults or refuse to start in non-`Development` environments without an explicit value.

- **`AllowedHosts`** — `appsettings.json` ships `"*"` so dev round-trips work. **Lock this down to the comma-separated list of hostnames the app is actually reachable on** (e.g. `"autonate.example.com;internal.autonate.example.com"`). Wildcards behind a misconfigured reverse proxy enable Host-header injection and cache poisoning.
- **`ConnectionStrings:Default`** — points at the local `AutoNate` Postgres database with dev credentials. Replace with the production connection string. Best practice: hold the password in a secret manager and inject as `ConnectionStrings__Default`.
- **`Flowable:BaseUrl` / `Flowable:Username` / `Flowable:Password`** — the dev defaults target `http://localhost:8080/flowable-rest` with the `rest-admin/test` credentials shipped by the local Flowable image. Production must point at a hardened Flowable instance with rotated credentials.
- **`WorkflowBehaviors:CallbackSharedSecret`** — the shared secret the Flowable JVM presents on the workflow-behavior callback endpoint. AutoNate.Web **refuses to start** outside `Development` when this value is unset (`Program.cs` validates it via `IValidateOptions`). Generate a strong random value, populate the same value as `autonate.flowable-events.callback-shared-secret` on the Flowable side, and inject as `WorkflowBehaviors__CallbackSharedSecret`.
- **`Authorization:Enabled`** + **`Authorization:Enforcement`** — these now default to `true` / `"full"` and AutoNate.Web **refuses to start** outside `Development` unless both are set that way (`Authorization/AuthorizationOptionsValidator.cs`, registered with `ValidateOnStart()`), so a deployment cannot end up with grants stored-but-ignored by omitting configuration. `Enforcement` must be exactly one of `off`, `read-only`, `full` in **any** environment — the evaluator compares it with ordinal equality, so `"Full"` or a typo would otherwise read as "not full" and quietly allow every write. To stage a rollout, use `Authorization:DryRun` rather than a lower enforcement level.
- **`Authorization:AssignSuperAdminToAllExistingUsers`** — defaults to `true` so a fresh install can be administered, and logs a startup warning outside `Development` while it stays on. It drives a **one-shot** backfill (`SuperAdminBackfillSql`) that grants the built-in SuperAdmin role to the `local_users` present the first time it runs, then records `superadmin_backfill_v1` in `auth_seed_state` and never runs again — users created afterwards are *not* made SuperAdmin. **After the first admin user has been seeded, flip this to `false`.** Two traps: leaving it on before pointing a deployment at a database that already holds other people's user rows grants all of them SuperAdmin; and setting it to `false` on a *greenfield* install leaves nobody with SuperAdmin at all, which under `Enforcement=full` means nobody can administer the system.
- **Dapr sidecar reachability** — the app fails fast when no Dapr sidecar is reachable. The container deployment must run a sidecar adjacent to the app and the `Dapr:HttpEndpoint` / `:GrpcEndpoint` / `:PlacementHostAddress` / `:SchedulerHostAddress` / `:StateStoreName` / `:PubSubName` keys must point at it. Setting `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` is a development-only escape hatch — it disables event-driven features (audit outbox dispatch, Bus Watcher, workflow execution live updates) and should never appear in production manifests.

### Recommended overrides

These have working defaults but are typically tuned per-environment.

- **`Authorization:DryRun`** — when true (and `Enforcement=full`), write-path denials are logged at WARN but the request is still allowed. Use this as a 24-hour safety window when initially flipping `Enforcement` from `off` → `full` in a busy environment, then turn it off.
- **`Plugins:MaxUploadBytes`** — bound on the plugin upload size. Defaults to 50 MB; lower it if your operators only need small extension surfaces, raise it if you ship plugins with bundled assets.
- **`Plugins:FailFastOnStartup`** — when true the host refuses to start if any enabled plugin fails to load, instead of logging a warning and continuing without it. Production deployments should usually set this to `true` so a broken plugin in a release surfaces immediately rather than silently degrading the app.
- **`Logging:LogLevel:*`** — production deployments typically want `Default=Warning` with `AutoNate=Information`; lift specific namespaces to `Debug` only when investigating an incident.
- **`Nats:Url`** — only needed when the JetStream provisioner runs against an external NATS cluster instead of the Dapr-bundled one. Leave unset (or empty) and the app skips JetStream provisioning entirely.

### Runtime data and writeable paths

- **`/data/`** (the runtime data root, also configurable via `AUTONATE_DATA_ROOT`) is auto-created on startup and holds user uploads, persisted plugins, public `/files` assets, and per-plugin scratch state. **The deployment must mount writeable storage here** — typically a persistent volume sized for the plugin and uploads workload. The volume is gitignored (see `.gitignore`); its content is the runtime's source of truth between restarts.
- **`infra/mounts/postgres/data`** etc. are local-only Docker bind-mount paths and **do not exist in production deployments** — production Postgres / Redis / Flowable run as managed services or separately-orchestrated containers with their own volumes.

### Reverse proxy / TLS

- AutoNate.Web does not terminate TLS. Front it with a reverse proxy (nginx, Traefik, an ingress controller, etc.) that handles HTTPS and forwards `X-Forwarded-For` / `X-Forwarded-Proto`. Lock `AllowedHosts` to the proxy's external hostnames; the app trusts the `Host` header for URL generation and CORS-style guards.
- The agent SSE streams (`/api/agent/...`) and the Bus Watcher endpoint hold long-lived connections; configure the proxy with no-buffering and a generous read timeout (≥10 minutes) for those routes.

### Pre-deployment checklist

1. `AllowedHosts` is set to actual hostnames (no `*`).
2. `WorkflowBehaviors:CallbackSharedSecret` is set on AutoNate.Web AND `autonate.flowable-events.callback-shared-secret` matches on the Flowable side.
3. `Authorization:Enabled=true`, `Authorization:Enforcement=full` (the host refuses to start otherwise), `Authorization:AssignSuperAdminToAllExistingUsers=false` (after seeding the first admin — it warns at startup until then).
4. `ConnectionStrings:Default` and `Flowable:Username`/`:Password` use rotated production credentials, not the dev defaults.
5. The `/data/` mount is writeable and backed by persistent storage.
6. A Dapr sidecar is reachable and `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR` is unset.
7. The reverse proxy forwards the right hostnames and forwarded headers, and disables buffering for SSE / streaming endpoints.

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
