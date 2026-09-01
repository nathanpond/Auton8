# Stack

AutoNate is a net10.0 ASP.NET Core minimal-API host (`src/AutoNate.Web`) serving a React 19 + Vite 8 + Mantine 9 SPA (`src/AutoNate.Spa`), with a collectible-ALC plugin SDK (`src/AutoNate.Plugin.Abstractions`, `plugins/`), two Node 24 sidecars (`services/hocuspocus`, `services/executor`), a Spring Boot 4 / Flowable 8 JVM extension (`flowable-extension/`), and a docker-compose local stack (`infra/`) driven by `Makefile` + `.run/` Rider configs.

> Generated from commit 01f0f174 on 2026-08-31 by /n8-map.

## 1. Component inventory

| Component | Path | Language / runtime | Framework (exact) | Listens on |
|---|---|---|---|---|
| Web host | `src/AutoNate.Web/` | C# / net10.0 | ASP.NET Core minimal APIs, EF Core 9 + Npgsql 9 | `http://localhost:5108` (`Properties/launchSettings.json`, mirrored at `Program.cs:72-75` for Debug) |
| SPA | `src/AutoNate.Spa/` | TypeScript 6 / Node 24 | React 19.2, Vite 8, Mantine 9.1 | `http://localhost:5173` (`vite.config.ts` `strictPort: true`) |
| Plugin SDK | `src/AutoNate.Plugin.Abstractions/` | C# / net10.0 | classlib; Dapper 2.1.66 + Npgsql 9.0.3 | n/a |
| Sample plugins | `plugins/HelloPlugin`, `plugins/Auditor` | C# / net10.0 | classlib → `dist/<Name>.zip` | n/a |
| Yjs sync sidecar | `services/hocuspocus/` | TypeScript 7 / Node 24-alpine | `@hocuspocus/server` ^4.6, `pg` ^8.23, `@blocknote/server-util` ^0.54 | `ws://localhost:1234` |
| Code executor sidecar | `services/executor/` | TypeScript 7 / Node 24-alpine | `nats` ^2.29, `isolated-vm` ^7, `pyodide` ^314 (CPython-versioned; Python 3.14) | none (NATS subscriber) |
| Flowable extension | `flowable-extension/` | Java 21 / Maven | Spring Boot 4.0.2, Spring 7.0.3, Flowable 8.0.0, GraalJS 24.1.2, JUnit 5.13.4 | baked into `flowable/flowable-rest:latest` image on `:8080` |
| Unit/integration tests | `tests/AutoNate.Web.Tests/` | C# xunit 2.9.0 | `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, coverlet 6.0.2 | needs Postgres `:5432` |
| Test plugin | `tests/AutoNate.Web.Tests.SamplePlugin/` | C# | staged to `bin/.../test-plugins/SamplePlugin/` by `AutoNate.Web.Tests.csproj` `StageSamplePluginForTests` | n/a |
| E2E | `tests/AutoNate.E2E.Tests/` | C# xunit + `Microsoft.Playwright` 1.50.0 | spawns `dotnet run -p:BuildSpa=true` on a random port | needs Postgres |

Node is pinned to 24 (Active LTS) by `.nvmrc` and `engines.node` in every `package.json`, and both sidecar Dockerfiles use `node:24-alpine` (Dependabot tracks the base image). Observed local toolchain (no `global.json`): `dotnet` 10.0.201, `node` v24.15.0, `npm` 11.12.1, Dapr CLI 1.17.1 / runtime 1.17.5. Compose pins the daemon images: `daprio/daprd:1.17.5`, `daprio/placement:1.17.5`, `daprio/scheduler:1.17.5`, `postgres:16-alpine`, `redis:7.4-alpine`, `nats:2.12-alpine`, `natsio/nats-box:0.16.0`, `daprio/dashboard:0.15.0`, `maven:3.9.9-eclipse-temurin-21` (`infra/docker-compose.yml`, `infra/flowable/Dockerfile`).

`dotnet-tools.json` pins `dotnet-ef` 10.0.6 as a local tool, but the schema is **not** managed by EF migrations (see §9 and `Integrations.md` → Postgres).

## 2. Backend (`src/AutoNate.Web`)

### NuGet packages (`src/AutoNate.Web/AutoNate.Web.csproj`)

| Package | Version | Used for |
|---|---|---|
| `Dapr.AspNetCore` / `Dapr.Client` / `Dapr.Messaging` | 1.17.9 | Only `Dapr.Messaging` is consumed in code (`AddDaprPubSubClient`, `Program.cs:247`; `DaprPublishSubscribeClient` in `Services/Signals/DaprStreamingSubscriber.cs`). Publishing goes over raw HTTP to the sidecar, not `DaprClient`. |
| `NATS.Client.JetStream` | 2.5.10 | `Services/Nats/NatsStreamProvisioner.cs`, `Services/Nats/INatsConnectionProvider.cs`, `Services/SystemHealth/SystemHealthService.cs` |
| `Npgsql` / `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.0.3 / 9.0.0 | `Persistence/AutoNateDbContext*.cs`; raw `NpgsqlConnection` in initializers/provisioners |
| `Microsoft.EntityFrameworkCore.Design` | 9.0.0 (PrivateAssets) | scaffolding only |
| `Microsoft.AspNetCore.SpaProxy` | 10.0.7 | spawns `npm run dev` in Development (see §4) |
| `DuckDB.NET.Data.Full` | 1.4.1 | cold-tier Parquet queries (`Services/Flowable/Cache/ColdTier/`) |
| `CsvHelper` 33.0.1, `ClosedXML` 0.105.0, `Parquet.Net` 5.2.0, `Snappier` 1.3.1 | | CSV/XLSX ingest + Parquet archive |
| `Markdig` | 0.37.0 | Markdown → BlockNote converter (`Services/Agent/...MarkdownToBlockNoteConverter`) |

`Program.Partial.cs` declares `public partial class Program;` so `WebApplicationFactory<Program>` works from tests; `InternalsVisibleTo("AutoNate.Web.Tests")` is set in the csproj.

### Analyzers (`Directory.Build.props` at repo root, tuned in `.editorconfig`)

Every csproj under `src/`, `tests/`, `plugins/` inherits:

```xml
<AnalysisLevel>latest</AnalysisLevel>
<AnalysisMode>Recommended</AnalysisMode>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers" Version="17.14.15" />
<PackageReference Include="AsyncFixer" Version="2.1.0" />
<PackageReference Include="SonarAnalyzer.CSharp" Version="10.25.0.139117" />
```

- Warnings are **not** errors (`TreatWarningsAsErrors` unset). Treat a new warning as a diff to fix, not a build break.
- Per-rule severities live only in `.editorconfig` — add a `dotnet_diagnostic.<ID>.severity = …` line with a one-line rationale; do not add `#pragma` or `[SuppressMessage]` in code. Rules already turned down: S1135, S125, S1192, S3358, S3776, S101, VSTHRD200, CA1848, CA2007, CA1873, CA1859, S1144, S3459, CA1716, CA1720, S927, S3267, S1075, CS8619, S108, S4136, S1066, CA1805, S1694, CA1068, CA1725, CA1862, CA1852, S3260.
- Keep VSTHRD002/100/103/110 clean — they are the rules the comment block cites as the reason the pack exists.
- Style: 4-space, LF, UTF-8, trailing whitespace trimmed, final newline (`.editorconfig` `[*.{cs,csx}]`).

### Kestrel / body limits

`Program.cs:923-928` sets `FormOptions.MultipartBodyLengthLimit` and `Kestrel MaxRequestBodySize` to 1 GiB. Per-feature caps are then applied in options (`Plugins:MaxUploadBytes` 50 MB, `ContentAttachments:MaxBytes` 25 MB, `DocumentImports:MaxBytes` 25 MB).

## 3. SPA (`src/AutoNate.Spa`)

`package.json` scripts:

| Script | Command | Notes |
|---|---|---|
| `dev` | `vite` | port 5173; proxies `/api`, `/account`, `/dapr`, `/bus-watcher`, `/files` and WS `/ws/bus-watcher`, `/ws/agent-model-default` to `ASPNETCORE_URL ?? http://localhost:5108` (`vite.config.ts`) |
| `build` | `tsc -b && vite build` | output `dist/` with sourcemaps; MSBuild copies to `wwwroot/` when `BuildSpa=true` |
| `type-check` | `tsc -b --noEmit` | |
| `lint` | `eslint src --max-warnings=110 --report-unused-disable-directives` | the warning budget is a ratchet — new code must not add warnings, and the number comes down when warnings are removed |
| `fetch:drawio` | `node scripts/fetch-drawio.mjs` | vendors the drawio webapp into `public/drawio/` (~2.8k files, excluded from Vite's watcher) |

Key deps (caret ranges, see `package.json` for the full list): `react`/`react-dom` ^19.2.5, `@mantine/*` ^9.1.1, `mantine-datatable` ^8.3.13, `@mantine/form` + `mantine-form-zod-resolver` ^1.3.0 + `zod` ^4.3.6, `@tanstack/react-query` ^5.100, `react-router-dom` ^7.14, `axios` ^1.15 (single instance at `src/api/client.ts` with `baseURL: "/"`), `@blocknote/*` ^0.51, `@eigenpal/docx-editor-*` 1.0.3 (exact), `yjs` ^13.6.30 + `@hocuspocus/provider` ^4, `@xyflow/react` ^12, `@excalidraw/excalidraw` ^0.18, `@uiw/react-codemirror` ^4.25, `recharts` ^3.8, `@fortawesome/fontawesome-free` ^7.2 (the only icon set — no `bi-*`). `overrides` pins `@tiptap/core` to 3.23.4 to avoid duplicate cores.

Dev deps: `typescript` ^6.0.3, `vite` ^8.0.10, `@vitejs/plugin-react` ^6, `eslint` ^9.39 flat config (`eslint.config.js`) with `typescript-eslint` ^8.60, `eslint-plugin-react` ^7.37, `eslint-plugin-react-hooks` ^7.1 (only `rules-of-hooks`=error, `exhaustive-deps`=warn — the v7 extra analyses are deliberately off), `eslint-plugin-jsx-a11y` ^6.10 (all at warn).

`tsconfig.app.json`: `strict: true`, `target/lib ES2022`, `moduleResolution: bundler`, `jsx: react-jsx`, `noUnusedLocals/Parameters: false`, path alias `@/* → ./src/*` (mirrored in `vite.config.ts` `resolve.alias`). Source tree: `src/{agent,api,components,hooks,lib,menus,pages,preferences,providers,routes,shell,types,widgets}`, `router.tsx`, `main.tsx`.

The BPMN modeler bundle is vendored by the **root** `package.json` (`bpmn-js` ^18.15, `bpmn-js-create-append-anything` ^1.2, `esbuild` ^0.28): `npm run vendor:bpmn` at repo root runs `scripts/vendor-bpmn.mjs` → `src/AutoNate.Spa/public/vendor/bpmn-js/bpmn-modeler.development.js`. The MSBuild `BuildReactSpa` target re-runs it when that file is missing.

## 4. How the SPA is served

- **Debug (`dotnet run`, Rider)**: `AutoNate.Web.csproj` sets `BuildSpa=false`; `launchSettings.json` sets `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Microsoft.AspNetCore.SpaProxy`, so the host spawns `npm run dev` (`SpaProxyLaunchCommand`) and redirects the browser to `http://localhost:5173/` (`SpaProxyServerUrl`). Vite proxies API/WS calls back to 5108. `wwwroot/` does not exist, so `Program.cs:1513` skips `MapStaticAssets`/`MapFallbackToFile`.
- **Release / publish / E2E**: `BuildSpa=true` (default for `Configuration=Release`, or `-p:BuildSpa=true`) runs `npm ci` (if `node_modules` missing) + `npm run build`, wipes `wwwroot/`, copies `dist/` in, and purges the static-web-assets caches (`AutoNate.Web.csproj` target `BuildReactSpa`). `wwwroot/` is 100% generated and gitignored.
- Runtime-mutable public files are served from `{Data:Root}/wwwroot` at `Data:PublicUrlPrefix` (default `/files`) via `UseStaticFiles` (`Program.cs:1494-1503`); `/assets` is reserved for Vite output.
- `/api/*` never falls through to `index.html`: unmatched `/api` paths get an uncacheable 404 from middleware at `Program.cs:1549-1558` (keep it middleware — a `MapFallback` route breaks body-less POSTs, see the comment there and `tests/AutoNate.Web.Tests/ApiNotFoundGuardTests.cs`).

## 5. Sidecars (`services/`)

Both are ESM TypeScript (`"type": "module"`), `tsc` → `dist/index.js`, scripts `build` / `start` / `dev` (`tsc --watch & node --watch dist/index.js`), multi-stage `node:22-alpine` Dockerfiles that install prod deps, compile, and copy `dist/` + `node_modules_prod/` into the runtime stage. `dist/` is gitignored.

- `services/hocuspocus/src/{index,auth,persistence,webhook,materializers,noteEmbedStub}.ts` — built and run by compose (`infra/docker-compose.yml` service `hocuspocus`, `restart: unless-stopped`). `infra/ensure-up.sh` hashes `Dockerfile`, `package*.json`, `tsconfig.json`, `src/**` into `infra/mounts/hocuspocus/.build-input-hash` and rebuilds the image when it changes — edit TypeScript, re-run `make infra-ensure`, done.
- `services/executor/src/{index,jsRunner,pythonRunner,wire}.ts` — **not in compose or the Makefile**. Run by hand: `cd services/executor && npm install && npm run build && NATS_URL=nats://localhost:4222 npm start`. Without it, code-transformer pipeline nodes fail after 30 s (`Services/Pipelines/Execution/JetStreamCodeNodeRunner.cs:26`).

## 6. Flowable extension (`flowable-extension/`)

`pom.xml` builds `autonate-flowable-events-1.0.0-SNAPSHOT.jar` (Java 21, `maven-compiler-plugin` 3.14.1, `maven-surefire-plugin` 3.5.4). All Spring/Flowable deps are `provided`; only `jackson-databind`/`jackson-datatype-jsr310` 2.20.2 and `org.graalvm.js:js-scriptengine` 24.1.2 ship in the jar. It is registered through `src/main/resources/META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports` → `com.autonate.flowableevents.FlowableExecutionEventAutoConfiguration`.

Build/test: `cd flowable-extension && mvn --batch-mode test package` (exactly what `infra/flowable/Dockerfile` runs in its build stage before copying the jar into `flowable/flowable-rest:latest` at `/app/WEB-INF/lib/autonate-flowable-events.jar`). You never run it standalone — `infra/ensure-up.sh` rebuilds the image whenever `infra/flowable/Dockerfile`, `pom.xml`, or `src/**` change (stamp file `infra/mounts/flowable/.build-input-hash`). Tests live in `src/test/java/com/autonate/flowableevents/*Tests.java` (JUnit 5).

## 7. Plugins (`plugins/`)

`plugins/Directory.Build.props` (inherits the root props) gives every plugin csproj `net10.0`, `CopyLocalLockFileAssemblies=true`, and a `Private=false` ProjectReference to `AutoNate.Plugin.Abstractions`; a plugin csproj is otherwise empty (`plugins/HelloPlugin/HelloPlugin.csproj`). `plugins/Directory.Build.targets` zips the bin output to `plugins/<Name>/dist/<Name>.zip` after every build, excluding `*.pdb`, `AutoNate.Plugin.Abstractions.dll`, `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, `Microsoft.Extensions.Logging.Abstractions.dll`, `Npgsql.dll`, `Dapper.dll` — the exact set `src/AutoNate.Web/Plugins/PluginAssemblyLoadContext.cs:16-27` `SharedAssemblies` redirects to the host ALC. Optional conventions picked up by the props: `plugin.json` (manifest, `Plugins/PluginManifest.cs`), `migrations/*.sql`, `PageTemplates/*.template` + `.png`. Use the `plugin-creator` skill for new plugins.

## 8. Build / run / test — exact commands

| Task | Command | Where |
|---|---|---|
| Build everything (.NET) | `dotnet build AutoNate.sln` | analyzers run every build |
| Build + lint SPA | `cd src/AutoNate.Spa && npm ci && npm run lint && npm run build` | |
| Start infra (first time / new session) | `make infra-up` (or `make infra-ensure` — idempotent, waits for readiness) | `Makefile` → `infra/docker-compose.yml`; `ensure-up.sh` pins compose project `-p infra` |
| Start infra + Dapr dashboard | `make infra-up-dashboard` | dashboard at `http://localhost:8081` |
| Run the app (default) | `make app` (= `make app-dapr`) | `dapr run --app-id autonate-web --app-port 5108 --dapr-http-port 3500 --dapr-grpc-port 50001 --placement-host-address 127.0.0.1:50006 --scheduler-host-address 127.0.0.1:50007 --resources-path infra/mounts/dapr-dashboard/components -- dotnet run --project ./src/AutoNate.Web --launch-profile http` |
| Run without Dapr (degraded) | `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true dotnet run --project src/AutoNate.Web --launch-profile http` | skips the probe at `Program.cs:1022`; audit outbox, Bus Watcher, workflow live updates are dead |
| Stop infra | `make infra-down`; full reset incl. data: `make infra-reset` (rm -rf `infra/mounts/*/data`) | |
| Logs / status | `make infra-logs`, `make infra-ps` | |
| Unit + integration tests | `cd infra && docker compose -p infra up -d postgres nats nats-init redis` then `dotnet test AutoNate.sln` | ~8 min; each test class creates `autonate_test_<guid>` on `localhost:5432` (`tests/AutoNate.Web.Tests/PostgresTestDatabase.cs`) |
| One test project | `dotnet test tests/AutoNate.Web.Tests` | fast loop; no Playwright |
| E2E | `make e2e` (= `infra-ensure` + `e2e-install` + `dotnet test tests/AutoNate.E2E.Tests --no-build`) | `PWDEBUG=1` for headed; fixture recreates DB `AutoNate_E2E` |
| Flowable extension only | `cd flowable-extension && mvn --batch-mode test package` | normally via `make infra-ensure` |
| Sidecar (hocuspocus) | via compose; manual: `cd services/hocuspocus && npm install && npm run build && npm start` with env from `services/hocuspocus/README.md` | |
| Sidecar (executor) | `cd services/executor && npm install && npm run build && NATS_URL=nats://localhost:4222 npm start` | manual only |
| Plugin zip | `dotnet build plugins/HelloPlugin/HelloPlugin.csproj` → `plugins/HelloPlugin/dist/HelloPlugin.zip` | upload at `/admin/plugins` |
| Vendored BPMN bundle | `npm run vendor:bpmn` (repo root) | |

### Rider run configurations (`.run/`, tracked)

| Config | Type | Runs |
|---|---|---|
| `infra: Local Stack` | docker-compose | `infra/docker-compose.yml` in the Services window |
| `infra: Ensure Up` | shell | `make infra-ensure` |
| `dapr: AutoNate.Web Sidecar` | shell | `./infra/start-autonate-web-sidecar.sh` — starts `daprd` on the host (pid/log under `$TMPDIR/autonate-web-daprd.{pid,log}`), resources from `infra/mounts/dapr-dashboard/components` |
| `dapr: AutoNate.Web Sidecar Status` | shell | `make rider-sidecar-status` → `infra/check-autonate-web-sidecar.sh` (curls `:3500/v1.0/metadata`, expects `"id":"autonate-web"`) |
| `AutoNate.Web: Rider` | .NET launch profile `http` | before-launch: `infra: Ensure Up` (enabled) then Build. The `dapr: AutoNate.Web Sidecar` before-launch task is present but `enabled="false"` in `.run/AutoNate.Web_ Rider.run.xml:887` — start the sidecar config yourself first, or the app throws at `Program.cs:1032`. |
| `AutoNate.Web: Dapr Run` | shell | `make app-dapr` in a terminal (no breakpoints) |

Debugger flow: run `dapr: AutoNate.Web Sidecar`, then `AutoNate.Web: Rider`. `make rider-sidecar-stop` / `make rider-sidecar-restart` manage the host-side daprd.

## 9. Required local services and ports

Started by `infra/docker-compose.yml` (compose project **`infra`**; `infra/ensure-up.sh` `REQUIRED_SERVICES`): `postgres` 5432, `flowable` 8080 (`/flowable-rest`), `flowable-dapr` (daprd bound to flowable's netns, HTTP 3500 inside), `redis` 6379, `nats` 4222 (+ monitor 8222), `nats-init` (one-shot `infra/scripts/bootstrap-jetstream.sh`), `dapr-placement` 50006, `dapr-scheduler` 50007, `hocuspocus` 1234. Optional profile `dashboard`: `dapr-dashboard` 8081. The app's own Dapr sidecar (`autonate-web`, HTTP 3500 / gRPC 50001) runs on the host, never in compose.

Bind mounts live under `infra/mounts/<service>/<purpose>` (gitignored except `.gitkeep`). `infra/dapr/components/{pubsub,statestore}.yaml` are the source of truth; `make infra-prepare` / `ensure-up.sh` copy them to `infra/mounts/dapr-dashboard/components/` and rewrite `nats://localhost:4222` → `nats://host.docker.internal:4222` for `infra/mounts/flowable-dapr/components/pubsub.yaml`.

Postgres bootstrap: `infra/postgres/init/01-create-autonate-db.sql` (`CREATE DATABASE "AutoNate"`) and `02-create-autonate-app-schema.sql` (1100 lines, `\c "AutoNate"`, base tables + the seeded `admin`/`admin` user) run once on an empty `infra/mounts/postgres/data`. Everything after that is applied on every app boot by `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs` (3827 lines of idempotent `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE … ADD COLUMN IF NOT EXISTS` SQL, executed in order from `EnsureAsync` at line 3746). There are no EF migrations and no schema-version table — to change schema, append a new `private const string …Sql` block and a matching `ExecuteSqlRawAsync` line at the end of `EnsureAsync`, and mirror any base-table change into `02-create-autonate-app-schema.sql` only if the E2E/test seed needs it.

## 10. Configuration keys that matter

Options classes bind via `builder.Services.AddOptions<T>().BindConfiguration(T.SectionName)` in `Program.cs`; add new keys the same way (with `.Validate(...).ValidateOnStart()` when production must fail closed — pattern at `Program.cs:776-781`). Dev values are in `src/AutoNate.Web/appsettings.Development.json`; production overrides use env `Section__Key`.

| Key | Purpose | Read by |
|---|---|---|
| `ConnectionStrings:Default` | primary `AutoNate` DB; dev string carries Npgsql keepalive/pruning tuning | `Program.cs:265-280` `AddDbContextFactory`; `PostgresTestDatabase` / E2E override it |
| `ConnectionStrings:Datastores` | second DB `autonate_datastores` for SqlType data stores; absent ⇒ feature disabled (Info log) | `Services/DataStores/Sql/DatastoresDatabaseInitializer.cs` |
| `DataStores:Sql:WriterRole` / `WriterRolePassword` | shared ingest role; password auto-generated to `{Data:Root}/datastores-writer.secret` if unset | `Services/DataStores/Sql/DatastoresDatabaseOptions.cs` |
| `Flowable:BaseUrl` / `Username` / `Password` | Flowable REST base + Basic auth (dev `rest-admin`/`test`) | `Configuration/InfrastructureOptions.cs`; `Services/Flowable/FlowableClient.cs:1534` |
| `Dapr:AppId`, `HttpEndpoint`, `GrpcEndpoint`, `PlacementHostAddress`, `SchedulerHostAddress`, `StateStoreName`, `PubSubName` | sidecar addresses + component names | `Configuration/InfrastructureOptions.cs`; `Services/Dapr/DaprSidecarProbe.cs`; every `/v1.0/publish` caller; `SystemHealthService` |
| `Nats:Url` | direct NATS URL; empty ⇒ skip JetStream provisioning and code-node execution | `Configuration/NatsOptions.cs`; `Services/Nats/*` |
| `WorkflowBehaviors:CallbackSharedSecret` | `X-AutoNate-Internal-Token` the Flowable JVM presents on `POST /api/workflow-behaviors/{key}/execute`; **required outside Development** | `Program.cs:776-781` validation; `Endpoints/SharedSecretEndpointFilter.cs` |
| `YjsServer:InternalSharedSecret` / `HocuspocusWsUrl` / `TicketTtlSeconds` | Hocuspocus shared secret (**required outside Development**), WS URL handed to browsers, ticket TTL (60 s) | `Program.cs:790-795`; `Services/Yjs/YjsServerOptions.cs`; `Endpoints/YjsEndpoints.cs` |
| `AllowedHosts` | `appsettings.json` ships `""`, dev `"*"`; **non-Development refuses to start** unless set to real host names | `Program.cs:803-814` |
| `TrustedProxy:Enabled` / `KnownProxies` / `KnownNetworks` / `ForwardLimit` | opt-in `X-Forwarded-*` trust; required behind a TLS terminator or cookies (`SecurePolicy=Always` outside dev) never emit | `Configuration/TrustedProxyOptions.cs`; `Program.cs:1078-1115` |
| `Authorization:Enabled` / `Enforcement` (`off`/`read-only`/`full`) / `AssignSuperAdminToAllExistingUsers` / `DryRun` | permission engine switches; code defaults are `false`/`off`/`true`/`false`; dev sets `true`/`full`/`true` | `Authorization/AuthorizationOptions.cs` |
| `DevelopmentAutoLogin:Enabled` / `Username` | see §12 | `Configuration/DevelopmentAutoLoginOptions.cs`; `Program.cs:1120-1243` |
| `Plugins:Folder` / `MaxUploadBytes` (50 MB) / `FailFastOnStartup` | plugin extraction root override, upload cap, boot policy | `Plugins/PluginOptions.cs`; `Plugins/PluginHostedService.cs` |
| `Data:Root` (default `data`, relative to content root ⇒ `src/AutoNate.Web/data/`) / `Data:PublicUrlPrefix` (`/files`) | runtime data tree: `wwwroot/`, `plugins/`, `uploads/`, `repositories/`, `tmp/`, `datastores/` (all auto-created) | `Storage/DataOptions.cs`, `Storage/DataPaths.cs`. Note: the README's `AUTONATE_DATA_ROOT` env var does not exist in code — use `Data__Root`. |
| `ContentAttachments:RootPath` (`data/content-attachments`) / `MaxBytes` / `AllowedContentTypes`; `DocumentImports:RootPath` / `MaxBytes` | content-hierarchy binary storage | `Services/Content/ContentAttachmentOptions.cs`, `DocumentImportOptions.cs` (bound with string literals at `Program.cs:377,381`) |
| `AuditOutbox:Enabled` (true) / `PollInterval` 2 s / `BatchSize` 100 / `BaseBackoff` 5 s / `MaxBackoff` 10 m / `MaxAttempts` 50 | durable audit-event outbox vs direct publish | `Services/Events/AuditOutboxDispatcher.cs` |
| `Projections:MaxBatchSize` / `MaxBatchWindow` / `BaseRetryDelay` / `MaxRetryDelay` / `MaxAttempts` / `WorkerEnabled` | projection framework worker | `Services/Projections/ProjectionOptions.cs` |
| `FlowableCache:*PollInterval`, `*PageSize`, `ReadThroughFreshness`, `RetentionEnabled`, `DefaultRetentionDays` 2555, `RetentionSweepInterval`, `ColdTier:{Enabled,Root,ArchiveAfterDays,…}` | Flowable projection polling + retention + Parquet/DuckDB cold tier | `Services/Flowable/Cache/FlowableCacheOptions.cs`, `ColdTier/ColdTierOptions.cs` |
| `RecordActivityRollup:PollInterval` / `RecentDayWindow` / `CurrentProjectionVersion` | internal aggregate projection | `Services/Records/Rollups/RecordActivityRollupOptions.cs` |
| `Agent:MaxIterations` 25 / `ToolTimeoutSeconds` 30 / `DefaultMaxTokens` 4096 | chatbot loop bounds | `Services/Agent/Loop/AgentOptions.cs` |
| `SystemIssues:DetectorsEnabled` / `RemediationEnabled` / `Remediation*` and `SystemIssues:Detectors:<Name>:*` | self-healing platform | `Services/SystemIssues/SystemIssueOptions.cs`, `Detectors/*.cs` |
| `ContentVersioning:SessionGapMinutes` 30 | version coalescing | `Services/Content/ContentVersioningOptions.cs` |
| `Logging:LogLevel:*` | dev demotes `DaprAuditEventPublisher`, `BusWatcher`, `DaprStreamingSubscriber`, EF command/connection, `System.Net.Http.HttpClient` to Warning; dev console formatter is multi-line | `appsettings*.json` |

## 11. Environment variables

| Variable | Effect | Read at |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS` | if unset in a Debug build, forced to `Development` / `http://localhost:5108` so Rider's plain launcher matches `launchSettings.json` | `Program.cs:63-76` |
| `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Microsoft.AspNetCore.SpaProxy` | enables the Vite spawn/redirect (set by the `http` launch profile; E2E omits it via `--no-launch-profile`) | SpaProxy |
| `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` | skip the Development-only sidecar probe | `Program.cs:1022-1037`; set by `tests/AutoNate.Web.Tests/AutoNateWebApplicationFactory.cs:25` and the E2E fixture |
| `ASPNETCORE_URL` | Vite proxy target override (defaults to `http://localhost:5108`) | `src/AutoNate.Spa/vite.config.ts` |
| `AUTONATE_POSTGRES_USER` / `AUTONATE_POSTGRES_PASSWORD` (`autonate` / `Your_password123!`) / `AUTONATE_POSTGRES_PORT` / `AUTONATE_FLOWABLE_PORT` / `AUTONATE_REDIS_PORT` / `AUTONATE_DAPR_DASHBOARD_PORT` / `AUTONATE_HOCUSPOCUS_PORT` / `AUTONATE_FLOWABLE_TZ` | compose interpolation; copy `.env.example` → `.env` (gitignored) to override | `infra/docker-compose.yml`, `infra/ensure-up.sh` |
| `AUTONATE_BEHAVIOR_CALLBACK_BASE_URL` (`http://host.docker.internal:5108`) / `AUTONATE_BEHAVIOR_CALLBACK_SECRET` | injected into the Flowable container as `AUTONATE_FLOWABLE_EVENTS_CALLBACK_BASE_URL` / `_SHARED_SECRET` (Spring relaxed binding of `autonate.flowable-events.*`); secret must equal `WorkflowBehaviors:CallbackSharedSecret` | compose `flowable.environment` |
| `AUTONATE_WEB_URL` / `YJS_INTERNAL_SHARED_SECRET` / `HOCUSPOCUS_PORT` / `POSTGRES_*` | hocuspocus sidecar; secret must equal `YjsServer:InternalSharedSecret` | `services/hocuspocus/src/index.ts:7-25` |
| `NATS_URL` | executor sidecar (default `nats://localhost:4222`) | `services/executor/src/index.ts:13` |
| `AUTONATE_INFRA_WAIT_TIMEOUT_SECONDS` (120), `AUTONATE_DAPR_SIDECAR_WAIT_TIMEOUT_SECONDS` (20), `AUTONATE_NATS_PORT`, `AUTONATE_DAPR_PLACEMENT_PORT`, `AUTONATE_DAPR_SCHEDULER_PORT` | infra script knobs | `infra/ensure-up.sh`, `infra/start-autonate-web-sidecar.sh` |
| `PWDEBUG=1` | headed Playwright | `tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs:73` |
| `AUTONATE_POSTGRES_PASSWORD` | also read by tests for the `autonate_test_*` connection string | `tests/AutoNate.Web.Tests/PostgresTestDatabase.cs:31` |

Only three `Environment.GetEnvironmentVariable` calls exist in the host (`Program.cs:67,72,1024`); everything else goes through `IConfiguration`.

## 12. Dev auto-login

`Program.cs:1120-1243` registers a middleware **only when `IsDevelopment()`**. With `DevelopmentAutoLogin:Enabled=true` and `Username=<existing local user>`, every non-POST request (except `/account/logout`) that isn't already a manual login gets a cookie principal built by `BuildPrincipal(user, "development_auto_login")` (claims `auth_source`, `dev_auto_login=true`, `dev_auto_login_username`). A manual login (`auth_source=manual`) is never overridden; changing the configured username or disabling the option signs the auto-login cookie out on the next request (`IOptionsMonitor`, hot-reloadable). Dev default is `Enabled: false` (`appsettings.Development.json`); the integration-test factory turns it on as `admin` (`AutoNateWebApplicationFactory.cs:55-57`), the E2E fixture turns it off so the login form is testable. The only seeded account is `admin`/`admin` (super-admin via `Authorization:AssignSuperAdminToAllExistingUsers`); create other users through `POST /api/users/`.

Auth model for reference: cookie scheme, `SameSite=Strict`, 8 h sliding, `/api/*` gets 401/403 instead of redirects (`Program.cs:87-137`); `POST /account/login` is the only endpoint that requires an antiforgery token (`Program.cs:1304-1402`), every other SPA mutation calls `.DisableAntiforgery()`, and server-to-server routes use `SharedSecretEndpointFilter` / `YjsInternalSecretEndpointFilter` (read the CSRF block at `Program.cs:151-194` before adding an endpoint).

## 13. Scratch and generated-file conventions

- `/temp/` (repo root, gitignored) is the scratch area for Playwright snapshots, debug screenshots, captured logs, throwaway exports. `.mcp.json` points the Playwright MCP `--output-dir` there; `.playwright-mcp/` is also ignored. Anything worth keeping goes to `docs/`.
- Root-level `/*.png` is ignored (dev screenshots); PNGs under `src/`, `public/`, tests are tracked.
- Never commit: `src/AutoNate.Web/wwwroot/`, `src/AutoNate.Spa/dist/`, `services/*/dist/`, `plugins/*/dist/`, `*.tsbuildinfo`, `infra/mounts/**`, `/data/`, `src/AutoNate.Web/data/`, `.env`, `*.DotSettings.user`.
- Plans: `docs/plans/YYYY-MM-DD-kebab.md`. Mantine offline reference: `docs/mantine/llms.txt`; `mantine` and `playwright` MCP servers in `.mcp.json`.
- `.n8/config.yml`: stack `dotnet`, default branch `master`, wiki opted out (docs live in `docs/`), issue areas `api|spa|plugins|services|flowable|infra|ci|docs|tests`. `.github/` has Dependabot (weekly grouped minor/patch for nuget, npm ×4, maven, actions) and issue templates only — there are no CI workflows.
