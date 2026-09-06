# Deployment

> **Auton8 1.0 requires a fresh database.** Upgrading a 0.x install is not
> supported. The 0.1.0 release notes described an upgrade path; that guidance
> does not carry into 1.0. Releases after 1.0 will carry upgrade paths — the
> `schema_versions` ledger that makes them possible ships in 1.0 for exactly
> that reason, even though 1.0 itself does not use it to migrate anything.

## Running as containers

The released `compose.yml` runs the whole product and is the shortest path to a
working deployment; see the wiki's Installation page. What follows applies
whichever way the application is hosted.

### Configuration a container must supply

Several settings default to `localhost`. That is correct when the application
runs on the host beside the compose stack, and wrong inside a container, where
`localhost` is the container itself. Each of these **fails at startup** rather
than warning, and each has to be pointed at a service name:

| Setting | Why it is not optional |
|---|---|
| `ConnectionStrings__Default` | The application database |
| `ConnectionStrings__Datastores` | A **second** connection string. `DatastoresDatabaseInitializer` carries its own, and missing it fails startup with a Postgres connect error that looks like the first one |
| `Nats__Url` | JetStream. Missing it throws `can not connect uris: nats://127.0.0.1:4222` |
| `Flowable__BaseUrl` | The BPMN engine |

The runtime data root (`Data__Root`, `/data` in the image) needs writeable
persistent storage mounted, and the datastores writer password is generated to
`/data/datastores-writer.secret` on first run if not supplied — move it to your
secret store.

### Dapr ordering

The application **refuses to start without a reachable Dapr sidecar**, and a
sidecar sharing the app's network namespace needs that namespace to exist
first. Neither can be started first, and waiting for the app to become
*healthy* deadlocks. The shipped image resolves this in its entrypoint by
waiting on the sidecar's `/v1.0/healthz/outbound` — deliberately the outbound
variant, because plain `/v1.0/healthz` includes the application's own health
and would deadlock on the thing being started.

Any hand-rolled orchestration needs to solve the same ordering problem.

### Schema initialisation

The application owns its schema end to end. It creates the base tables and
every migration after them on startup, so a deployment only has to provide an
**empty database** — no schema to mount and no ordering to get right. (The
local compose stack does mount two init scripts, but neither builds schema:
they create the database and the Flowable role, both cluster-level things the
application cannot do from a connection to its own database. See below.)

Initialisation is serialised by a Postgres advisory lock, so two hosts starting
against one database is defined behaviour rather than a race. Applied steps are
recorded in `schema_versions`, and the application **refuses to start against a
database written by a newer build**, naming both versions. `GET /api/health/system`
reports the current schema version and applied-step count, so the question
"which version is this database at" is answerable from the admin UI.

### Where workflow scripts execute

A BPMN **script task** does not run inside the Flowable JVM. The engine's own
script behaviour is replaced at parse time by an `ActivityBehaviorFactory` in
the AutoNate Flowable extension, so there is no path by which author code
reaches a JVM script engine. The script and the execution's variables are
POSTed to `AutoNate.Web`, which runs them in the same V8 isolate the pipeline
code nodes use, and returns the variables the script wrote.

This matters because it is what closes GHSA-82rh-gjhw-rg9r. The base image
still ships `nashorn-core`, `groovy` and `flowable-groovy-script-static-engine`;
Nashorn's Java interop is on by default, so before this change a script task
could reach `java.lang.System` and, through it, the JVM and every database the
process could see.

**What a script may reach:** its process variables, through `variables.get(name)`
and `variables.set(name, value)`. Nothing else. There is no `require`, no
`fetch`, no filesystem, no network, no database, and no Java. Only JSON crosses
the sandbox boundary.

**Configuration.** The Flowable runtime needs both of these, or it refuses to
publish a workflow containing a script task:

```
autonate.flowable-events.callback-base-url
autonate.flowable-events.callback-shared-secret
```

**Fail-closed.** If the sandbox cannot be reached, the script task fails and
the job retries. It is never run anywhere else. A fallback to in-JVM execution
would reinstate the vulnerability at exactly the moment the system is degraded,
so there deliberately is not one — and a test asserts its absence.

Script errors (an author's mistake) and an unreachable sandbox are reported
distinctly, so a failing workflow's error surface says which one happened.

JavaScript is the only supported `scriptFormat`. A script task declaring
`groovy` is refused at execution with a message saying so.

### The Flowable database role (fresh installs only)

By default the Flowable engine connects to Postgres as the same bootstrap
superuser the application uses — the role that **owns** `AutoNate` and
`autonate_datastores`. That is more than the engine needs: anything reaching
that datasource reaches application data as its owner.

`infra/postgres/init/02-flowable-role.sql` provisions a restricted
`flowable_app` role that owns the `flowable` database and has `CONNECT`
revoked on the application databases. To use it, set both:

```
AUTONATE_FLOWABLE_DB_USER=flowable_app
AUTONATE_FLOWABLE_DB_PASSWORD=<a real password, not the init script default>
```

The init script ships a placeholder password (`flowable_dev_only_change_me`)
because a role has to be created with one. Change it on any deployment that is
not a laptop:

```sql
ALTER ROLE flowable_app PASSWORD '<real password>';
```

**This protects new deployments only, and the default is deliberately off.**
Two facts make that the honest position rather than a shortcut:

1. `docker-entrypoint-initdb.d` scripts run only against an **empty data
   directory**. An existing cluster never executes this file.
2. Creating the role by hand on an existing cluster is *still* not enough.
   `ALTER DATABASE ... OWNER` does not move ownership of the tables Flowable
   has already created, and `REASSIGN OWNED` is refused for the bootstrap
   role. The engine would own the database but not its own schema, and would
   fail on its next schema upgrade.

So there is no supported switch-over for an existing deployment, and compose
keeps defaulting to the superuser so that pulling this change cannot break one.
**An existing deployment must not set these variables** until a migration that
transfers table ownership exists.

The practical consequence, stated plainly: every deployment created before this
change — and every new deployment whose operator does not set these two
variables — runs the Flowable engine with the same database reach it always
had. The isolation is available, not automatic.

## Configuration

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

- **`AUTONATE_FORCE_LOCAL_SIGNIN`** — the break-glass escape hatch for sign-in configuration. Auton8 lets an administrator disable local (username/password) sign-in once federation is proven, which is what an SSO-only organisation wants; it also means a provider that breaks *after* local is switched off leaves nobody able to get in. Setting this variable to anything but `0` / `false` / `no` / `off` forces local sign-in **on** regardless of what is stored in the database, logs a warning on every startup while it is set, and emits an `auth.signin-methods.forced-local` audit event. It is read from the environment rather than from configuration binding on purpose: it must not be settable by anything living in the database it exists to overrule. **Unset it once the intended sign-in methods work** — while it is set, the site permanently accepts passwords it was configured to refuse.

  Note the asymmetry in how the value is parsed: anything that is not a clear negative counts as on. An operator setting this during an incident has typed something meaning "yes", and a strict parse that rejected their spelling would leave them locked out while believing they had fixed it.

- **`ExternalConnections:AllowedProviderHosts`** — an external connection's `baseUrl` decides where its stored API key is sent, on every chat turn and every search, so the host is allowlisted per connection kind. `api.anthropic.com`, `api.openai.com` and `api.tavily.com` are built in and need no configuration; **an existing connection pointing anywhere else (Azure OpenAI, a corporate gateway, a self-hosted OpenAI-compatible model, Ollama) stops working until its host is added here**, with an error naming this key. Entries are host-only, matched case-insensitively, and a leading `*.` is a subdomain wildcard; `https` is required regardless. The REST **data connector** is deliberately not allowlisted — calling arbitrary third-party APIs is its purpose — and is instead guarded against private, loopback, link-local and cloud-metadata addresses, with `https` required outside `Development`.

### Recommended overrides

These have working defaults but are typically tuned per-environment.

- **`Authorization:DryRun`** — when true (and `Enforcement=full`), write-path denials are logged at WARN but the request is still allowed. Use this as a 24-hour safety window when initially flipping `Enforcement` from `off` → `full` in a busy environment, then turn it off.
- **`Plugins:MaxUploadBytes`** — bound on the plugin upload size. Defaults to 50 MB; lower it if your operators only need small extension surfaces, raise it if you ship plugins with bundled assets.
- **`Plugins:FailFastOnStartup`** — when true the host refuses to start if any enabled plugin fails to load, instead of logging a warning and continuing without it. Production deployments should usually set this to `true` so a broken plugin in a release surfaces immediately rather than silently degrading the app.
- **`Logging:LogLevel:*`** — production deployments typically want `Default=Warning` with `AutoNate=Information`; lift specific namespaces to `Debug` only when investigating an incident.
- **`Nats:Url`** — only needed when the JetStream provisioner runs against an external NATS cluster instead of the Dapr-bundled one. Leave unset (or empty) and the app skips JetStream provisioning entirely.

### Runtime data and writeable paths

- **`/data/`** (the runtime data root, set with `Data__Root`) is auto-created on startup and holds user uploads, persisted plugins, public `/files` assets, and per-plugin scratch state. **The deployment must mount writeable storage here** — typically a persistent volume sized for the plugin and uploads workload. The volume is gitignored (see `.gitignore`); its content is the runtime's source of truth between restarts.
- **`infra/mounts/postgres/data`** etc. are local-only Docker bind-mount paths and **do not exist in production deployments** — production Postgres / Redis / Flowable run as managed services or separately-orchestrated containers with their own volumes.

### Reverse proxy / TLS

- AutoNate.Web does not terminate TLS. Front it with a reverse proxy (nginx, Traefik, an ingress controller, etc.) that handles HTTPS and forwards `X-Forwarded-For` / `X-Forwarded-Proto`. Lock `AllowedHosts` to the proxy's external hostnames; the app trusts the `Host` header for URL generation and CORS-style guards.
- The agent SSE streams (`/api/agent/...`) and the Bus Watcher endpoint hold long-lived connections; configure the proxy with no-buffering and a generous read timeout (≥10 minutes) for those routes.

### Pre-deployment checklist

1. `AllowedHosts` is set to actual hostnames (no `*`).
2. `WorkflowBehaviors:CallbackSharedSecret` is set on AutoNate.Web AND `autonate.flowable-events.callback-shared-secret` matches on the Flowable side.
3. `Authorization:Enabled=true`, `Authorization:Enforcement=full` (the host refuses to start otherwise), and `Authorization:AssignSuperAdminToAllExistingUsers=false` — it now ships false, and turning it on grants SuperAdmin to *every* existing user the first time it runs. It is a migration aid for deployments that predate role assignments, not part of first-run setup.
4. `Bootstrap:AdminUsername` / `Bootstrap:AdminPassword` are set for the very first startup against an empty database, and the password is one you chose. See [First administrator](DEVELOPMENT.md#first-administrator). Nothing is seeded; without these there is no way to sign in. Unset them once the account exists.
5. `ConnectionStrings:Default` and `Flowable:Username`/`:Password` use rotated production credentials, not the dev defaults.
6. The `/data/` mount is writeable and backed by persistent storage.
7. A Dapr sidecar is reachable and `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR` is unset.
8. `AUTONATE_FORCE_LOCAL_SIGNIN` is unset, unless you are deliberately holding the break-glass hatch open. Check the startup log: while it is set, a warning names it on every start.
9. The reverse proxy forwards the right hostnames and forwarded headers, and disables buffering for SSE / streaming endpoints.
