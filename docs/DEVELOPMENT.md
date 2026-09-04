# Development

Auton8 treats infrastructure as session-scoped and the web app as restartable.
Start the supporting services once at the beginning of a development session,
then stop and restart the app as often as you want without touching
PostgreSQL, Flowable, Redis, or the Dapr control plane.

## First-time setup

1. Install the prerequisites. The authoritative list, with the minimum version
   of each and how to install it, is [`infra/prerequisites`](../infra/prerequisites)
   — deliberately one file rather than a version restated here, in the Makefile
   and in the script, which is how three copies come to disagree.
2. Run `make preflight`. It checks every prerequisite for presence *and*
   version, and checks that the ports the stack publishes are free. It reports
   everything wrong in one pass, so a machine is fixed once rather than once per
   missing tool:

   ```bash
   make preflight
   ```

   `make infra-up`, `make infra-ensure` and `make app` all run it first and
   refuse to start if it fails, so you do not have to remember it.
3. Copy `.env.example` to `.env` if you want to override the default local
   ports or PostgreSQL credentials.
4. Set a first administrator before the first run — see
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
- `keycloak` on `http://keycloak:8082` when you start the `keycloak` profile — a real OIDC and SAML identity provider for the federated sign-in work. See [Keycloak](#keycloak-a-real-identity-provider) below; the hostname is not a typo.

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

### Pinned build inputs

Every container image is pinned by content digest and every project's NuGet graph
is locked, so rebuilding a commit resolves what it resolved the first time. Both
have a refresh path — pinning without one just makes upgrades painful:

```bash
make lockfiles          # after changing any PackageReference
```

CI restores in locked mode, so a changed `PackageReference` without a regenerated
lock file fails the build rather than quietly resolving a different graph.

To move an image, resolve the new digest and replace it, keeping the tag as the
trailing comment:

```bash
docker buildx imagetools inspect postgres:16-alpine | awk '/^Digest:/{print $2; exit}'
```

`PinnedImageTests` fails if any image reference loses its digest. One pin is
load-bearing beyond reproducibility: `infra/flowable/Dockerfile` must move
together with `<flowable.version>` in `flowable-extension/pom.xml`, or a compiled
extension ends up on an engine it was not built for.

Planning and issue workflow are managed with n8SDLC — GitHub Issues and milestones are the plan, `.n8/` holds config, the decision log and harvested audit checklists.

## Keycloak: a real identity provider

Federated sign-in (OIDC and SAML) is developed against a real Keycloak rather
than a stub, because a stub written from the same reading of the specifications
that produced the code agrees with the code by construction. It is a
**development and testing dependency, not a product component** — Auton8
federates to whatever identity provider an organisation already has, and does
not ship one.

It runs under its own compose profile, off by default, so nobody pays for a JVM
they are not using.

### One-time setup

**1.** Set admin credentials in `.env`. There is deliberately no default that
works:

```bash
echo "AUTONATE_KEYCLOAK_ADMIN_USER=kcadmin" >> .env
echo "AUTONATE_KEYCLOAK_ADMIN_PASSWORD=$(openssl rand -base64 18)" >> .env
```

**2.** Add one line to `/etc/hosts`:

```bash
echo '127.0.0.1 keycloak' | sudo tee -a /etc/hosts
```

`make keycloak-up` checks both and refuses with instructions if either is
missing, so you do not have to remember this.

### Why `keycloak:8082` and not `localhost`

**This is the detail that eats an afternoon if it is wrong.** OIDC discovery
pins an *issuer*, and every library validates tokens against it. Keycloak's
issuer must therefore match the URL the browser is redirected to **and** the URL
Auton8 validates against — and those sit in three different network positions:

| Who | How they reach Keycloak |
|---|---|
| The browser | the published port on the host |
| Auton8 as a host process (`make app`) | the published port on the host |
| Auton8 as a container (`make app-container`) | the compose network |

`localhost:8082` is correct for the first two and wrong for the third: inside
the app's own container, `localhost` is the app. Any URL that differs by network
position produces an issuer mismatch, which surfaces deep inside an OIDC library
rather than as "cannot connect".

So there is **one** URL, `http://keycloak:8082`, made to resolve everywhere:

- on the compose network, `keycloak` is the service's own name;
- on the host, the `/etc/hosts` line above points it at the published port.

The port is the same **inside and outside** the container, both read from
`AUTONATE_KEYCLOAK_PORT`. If they differed, the issuer would differ by network
position again — which is why they are one variable and not two. The port still
binds to `127.0.0.1` like every other service, so this arrangement needs no
exception to project invariant 5.

### Running it

```bash
make keycloak-up      # start it (checks the two preconditions first)
make keycloak-logs    # follow its log
make keycloak-down    # stop just Keycloak
```

Admin console: <http://keycloak:8082/admin/>, with the credentials from `.env`.

### What is seeded

From `infra/keycloak/realm-export.json`, imported at every start. There is
**no data volume on purpose**: each start re-imports from the file, so the realm
cannot drift away from its checked-in export. A realm that has drifted is worse
than no export, because the next person trusts the file.

| | |
|---|---|
| Realm | `auton8` |
| OIDC client | `auton8-oidc` — public client, PKCE (S256) required |
| SAML client | `http://localhost:5108/api/auth/saml/keycloak/metadata` (the SP entity ID Auton8 publishes) |
| Users | `alice` / `alice` (in `engineering`), `bob` / `bob` (in `sales` and `engineering`) |
| Groups | `engineering`, `sales` |

Group membership is exposed **both** as an OIDC `groups` claim and as a SAML
`groups` attribute, because claim mapping has to work identically through both
and a realm that only did one would leave half of it untestable.

The user passwords and the SAML client are fixture values and are committed on
purpose — they exist only inside a loopback-bound container that is rebuilt from
this file on every start, and a developer has to be able to type them into a
login form. The **admin** password is the one that is not committed: it is the
credential that grants control of the identity provider, and it comes from
`.env` with no working fallback. The OIDC client needs no secret at all, because
it is a public client using PKCE.

### Configuring Auton8 against it

In **Site Configuration → Identity Providers**, add a provider:

**OIDC**

| Field | Value |
|---|---|
| Kind | OIDC |
| Slug | `keycloak` |
| Authority | `http://keycloak:8082/realms/auton8` |
| Client ID | `auton8-oidc` |
| Secret | *leave blank* — it is a public client |

**SAML**

| Field | Value |
|---|---|
| Kind | SAML 2.0 |
| Slug | `keycloak` |
| IdP entity ID | `http://keycloak:8082/realms/auton8` |
| Metadata URL | `http://keycloak:8082/realms/auton8/protocol/saml/descriptor` |

The SAML client in the realm is registered under Auton8's own SP entity ID,
which is the metadata URL Auton8 publishes for that slug —
`http://localhost:5108/api/auth/saml/keycloak/metadata`. If you run Auton8 on a
different port, that entity ID changes and the realm export needs the same edit.

Enable the provider, sign out, and the login page offers it.

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
