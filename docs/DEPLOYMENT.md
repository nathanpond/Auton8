# Deployment

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
8. The reverse proxy forwards the right hostnames and forwarded headers, and disables buffering for SSE / streaming endpoints.
