# Conventions

One-line summary: the canonical forms for C# (minimal-API endpoint groups, `I*Store` + `EfCore*` stores, snapshot caches, `BackgroundService` loops, audit events, analyzer policy), the React/Mantine SPA (hooks + api clients, `DataTable`, page-context providers, theming vars), the Node sidecars, and git/n8SDLC workflow — every rule shown with a real excerpt and its `path:line`.

> Generated from commit 01f0f174 on 2026-08-31 by /n8-map.

Read this before writing code. Where the codebase is inconsistent, the **dominant pattern is stated as the rule** and the exceptions are named so an executor knows what not to copy.

---

## 1. C# (src/AutoNate.Web, plugins/, tests/)

### 1.1 Build-time analyzer policy

- Four analyzer packs run on every build (`Directory.Build.props:21-60`): NetAnalyzers (`AnalysisLevel=latest`, `AnalysisMode=Recommended`, `EnforceCodeStyleInBuild=true`), `Microsoft.VisualStudio.Threading.Analyzers`, `AsyncFixer`, `SonarAnalyzer.CSharp`.
- Warnings stay warnings: `TreatWarningsAsErrors` is **not** set (`Directory.Build.props:14-16`). A clean historical baseline means any new warning is a visible diff — fix it or suppress it with rationale; never leave a new one.
- Per-rule severity tuning lives **only** in the repo-root `.editorconfig` (`Directory.Build.props:17-19`). Each downgrade has a one-line rationale. Do not add `<NoWarn>` to csproj files.
- Formatting: 4-space indent, LF, UTF-8, trailing whitespace trimmed, final newline (`.editorconfig:13-19`). Tests get `CA1707` off so snake_case test names are legal (`.editorconfig:326-327`).

**Suppress inline only with a rationale comment, and restore immediately after.** Canonical form (`src/AutoNate.Web/Endpoints/StatusAppearanceEndpoints.cs:62-69`):

```csharp
// EF Core translates .ToLower() to SQL LOWER() for Postgres but
// can't translate .ToLower(CultureInfo) — so the locale-flavor
// CA rules don't fit this comparison. Status strings are admin-
// entered ASCII labels; locale-sensitive lowering doesn't matter.
#pragma warning disable CA1304, CA1311
var exists = await db.StatusAppearanceEntries
    .AnyAsync(x => x.Status.ToLower() == status.ToLower(), ct);
#pragma warning restore CA1304, CA1311
```

Attribute form when a whole type is the exception (`tests/AutoNate.E2E.Tests/AutoNateE2ECollection.cs:12`):

```csharp
[SuppressMessage("Naming", "CA1711", Justification = "xUnit collection-definition types are conventionally suffixed 'Collection'.")]
```

Rules already off globally that you should **not** re-suppress locally (read the rationale before fighting them): `CA2007` ConfigureAwait (`.editorconfig:85-87`), `VSTHRD200` Async suffix (`:63-70`), `S108` empty catch — reserved for `catch (OperationCanceledException) {}` (`:144-149`), `CA2016` — `CancellationToken.None` is deliberate in cleanup/audit paths (`:218-222`), `CA1001` — app-lifetime singletons with `SemaphoreSlim` fields (`:268-277`), `CA1862` — `.ToLower() == .ToLower()` inside EF queries (`:187-193`).

### 1.2 Naming and file organisation

| Thing | Rule | Evidence |
|---|---|---|
| Namespace = folder | `AutoNate.Web.Endpoints`, `AutoNate.Web.Services.<Area>`, `AutoNate.Web.Authorization.EndpointFilters` | `src/AutoNate.Web/Endpoints/GroupEndpoints.cs:6`, `src/AutoNate.Web/Services/Records/IRecordCommentStore.cs:3` |
| One endpoint group per file | `<Noun>Endpoints.cs` containing `public static class <Noun>Endpoints` with a single `Map<Noun>Endpoints` extension | `src/AutoNate.Web/Endpoints/` (69 files), `GroupEndpoints.cs:8-10` |
| Store interface + EF impl | `I<Noun>Store` in `Services/<Area>/I<Noun>Store.cs`; implementation `EfCore<Noun>Store` beside it | `Services/Records/IRecordCommentStore.cs`, `Services/Records/EfCoreRecordCommentStore.cs` |
| Domain exceptions | `<Noun>NotFoundException`, `<Noun>ValidationException`, `<Noun>ForbiddenException` declared at the top of the store interface file, `sealed`, message built in ctor | `IRecordCommentStore.cs:5-19` |
| Scaffolded EF entities | `Persistence/Scaffolded/*.cs`; alias them at the top of stores to avoid clashing with domain models | `EfCoreRecordCommentStore.cs:4-5` (`using RecordCommentEntity = AutoNate.Web.Persistence.Scaffolded.RecordComment;`) |
| Request/response DTOs | `public sealed record <Verb><Noun>Request(...)` / `<Noun>Dto(...)` positional records at the top of the endpoint file (or nested in the endpoints class) | `RecordCommentEndpoints.cs:9-29`, `GroupEndpoints.cs:239` |
| Store inputs | `Create<Noun>Input` / `Update<Noun>Input` records in the models layer, distinct from request DTOs | `GroupEndpoints.cs:57-59` maps `CreateGroupRequest` → `CreateGroupInput` |
| Constants | `EntityKinds.X`, `Actions.X` (lowercase string values, no separators); event types `IamEventTypes.GroupCreated` etc. | `.claude/skills/add-permission-gate/SKILL.md` steps 1-2; `GroupEndpoints.cs:23-25` |
| Primary constructors | Use C# 12 primary constructors for DI-injected classes | `EfCoreRecordCommentStore.cs:9-10`, `RecordTypeShortCodeCache.cs:23-26` |
| Sealed by default for concrete classes | `public sealed class EfCore…`, `sealed record` | `EfCoreRecordCommentStore.cs:9`, `AuditEventPublisher.cs:11` |

### 1.3 Endpoint shape (the canonical handler)

Every endpoint file follows this exact skeleton (`src/AutoNate.Web/Endpoints/GroupEndpoints.cs:10-30`):

```csharp
public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
{
    var group = app.MapGroup("/api/admin/groups").RequireAuthorization();

    group.MapGet("/", async (
        HttpContext http,
        bool? includeArchived,
        IGroupStore store,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct) =>
    {
        var groups = await store.ListAuthorizedAsync(http.User, includeArchived ?? false, ct);
        await auditPublisher.PublishAsync(
            IamEventTopic.TopicName,
            IamEventTypes.GroupListViewed,
            IamResourceKinds.Group,
            resource: null,
            details: new { resultCount = groups.Count, includeArchived = includeArchived ?? false },
            ct);
        return Results.Ok(groups);
    }).AuthorizedInHandler("store.ListAuthorizedAsync filters via FilterQueryAsync(Group, View) against the actor's grants");
```

Mutation form — the full chain (`GroupEndpoints.cs:48-74`):

```csharp
group.MapPost("/", async (
    CreateGroupRequest request,
    HttpContext http,
    IGroupStore store,
    IAuditEventPublisher auditPublisher,
    CancellationToken ct) =>
{
    try
    {
        var grp = await store.CreateAsync(
            new CreateGroupInput(request.Name, request.Description),
            http.GetActorId(), ct);
        await auditPublisher.PublishAsync(IamEventTopic.TopicName, IamEventTypes.GroupCreated,
            IamResourceKinds.Group, resource: new { id = grp.Id, name = grp.Name }, details: null, ct);
        return Results.Created($"/api/admin/groups/{grp.Id}", grp);
    }
    catch (GroupValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).DisableAntiforgery()
  .RequireKindPermission(EntityKinds.Group, Actions.Create);
```

Rules, each load-bearing:

1. **Register in `Program.cs`.** `app.Map<Noun>Endpoints();` alongside the others (`src/AutoNate.Web/Program.cs:1421-1440`). Nothing else discovers endpoint groups.
2. **`MapGroup(prefix).RequireAuthorization()` first**, then per-route gating. The group prefix carries route tokens when the resource is nested (`RecordCommentEndpoints.cs:35`: `"/api/records/{recordId:guid}/comments"`).
3. **Every `/api/*` route must carry an explicit auth decision** or `AuthorizationGatePresenceTests` fails the build (`tests/AutoNate.Web.Tests/Authorization/AuthorizationGatePresenceTests.cs:10-22`). Pick exactly one:
   - `.RequirePermission(EntityKinds.X, Actions.Y)` — per-instance; id read from route value `id` by default, pass a third arg for another token name (`Authorization/EndpointFilters/RequirePermissionExtensions.cs:12-24`; `RecordCommentEndpoints.cs` tail uses `"recordId"`). The token name **must** match a `{token}` in the route or the filter throws at request time.
   - `.RequireKindPermission(kind, action)` — bulk/kind-level, no instance id (`RequirePermissionExtensions.cs:37-45`).
   - `.AuthorizedInHandler("reason")` — the handler filters via `FilterQueryAsync`/actor-scoped queries; the reason string is mandatory and should name the mechanism (`Authorization/EndpointFilters/AuthorizationDecisionMetadata.cs:41-50`).
   - `.OpenToAuthenticated("reason")` — any signed-in user; use sparingly (`AuthorizationDecisionMetadata.cs:59-68`).
   - `.AllowAnonymous()` — login/health/public-share only.
4. **Every mutating route (`MapPost/MapPatch/MapPut/MapDelete`) chains `.DisableAntiforgery()`** before the permission call. The SPA posts `application/json`; without it the antiforgery middleware returns 400 (`.claude/skills/add-workflow-execution-action/SKILL.md` "Common slip-ups").
5. **Actor comes from `http.GetActorId()`; never from the body or query.** It returns `Guid.Empty` when unauthenticated so handlers can short-circuit to `Results.Unauthorized()` without catching (`src/AutoNate.Web/Endpoints/HttpContextActorExtensions.cs:7-15`):

   ```csharp
   public static Guid GetActorId(this HttpContext http)
   {
       var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
       return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
   }
   ```
   Pass `http.User` (the `ClaimsPrincipal`) to store methods that filter by grants (`ListAuthorizedAsync`), and the `Guid` to methods that stamp `created_by`/`updated_by`.
6. **`CancellationToken ct` is the last lambda parameter and is forwarded to every store/publisher call.** Stores declare `CancellationToken cancellationToken = default` last (`IRecordCommentStore.cs:23-49`). The only sanctioned `CancellationToken.None` sites are audit publishes inside cleanup/catch paths (15 sites; rationale `.editorconfig:218-222`).
7. **Return shapes:** `Results.Ok(dto)`, `Results.Created($"/api/…/{id}", dto)`, `Results.NoContent()`, `Results.NotFound()`, `Results.Conflict(new { error = "…" })` (`GroupEndpoints.cs:203`). Validation failures are `Results.BadRequest(new { error = ex.Message })` — an anonymous `{ error }` object, **not** `ValidationProblem` (zero usages). `Results.Problem(title:, detail:, statusCode:)` is reserved for upstream-failure translation (2 sites; `src/AutoNate.Web/Endpoints/DataStoreEndpoints.cs:759-768` maps a `PostgresException` to 502 after `LogWarning`).
8. **Map domain exceptions to status codes in the handler, not in middleware:** `catch (XNotFoundException) → NotFound()`, `catch (XValidationException ex) → BadRequest(new { error })` (`GroupEndpoints.cs:98-105`). Stores throw; endpoints translate.
9. **Map entity → DTO with a private static `ToDto` at the bottom of the endpoints class** (`RecordCommentEndpoints.cs` tail: `private static CommentDto ToDto(RecordComment model) => new(...)`).
10. **Paging:** `var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);` and `page.GetValueOrDefault(0)`, then `.Skip(pg * ps).Take(ps)`; put `page`, `pageSize`, `totalCount`, `resultCount` into the audit `details` (`src/AutoNate.Web/Endpoints/CabinetEndpoints.cs:47-59`). Upper bound is 200 for lists, smaller for previews (`DataConnectorEndpoints.cs:139` uses 50).
11. **Audit events on every read and mutation** — see §1.7.

### 1.4 Stores

Canonical store (`src/AutoNate.Web/Services/Records/EfCoreRecordCommentStore.cs:9-38`):

```csharp
public sealed class EfCoreRecordCommentStore(IDbContextFactory<AutoNateDbContext> dbContextFactory)
    : IRecordCommentStore
{
    private const int MaxBodyLength = 10_000;

    public async Task<IReadOnlyList<RecordComment>> ListForRecordAsync(
        Guid recordId, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordComments.AsNoTracking()
            .Where(c => c.RecordId == recordId);
        if (!includeDeleted) { query = query.Where(c => !c.IsDeleted); }
        var rows = await query.OrderByDescending(c => c.CreatedAtUtc).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToModel()).ToList();
    }
```

Rules:

- **Inject `IDbContextFactory<AutoNateDbContext>`, never a scoped `AutoNateDbContext`.** Open with `await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);` per method. Read paths use `.AsNoTracking()`.
- **Return domain models (`Models/…`), not scaffolded entities.** `entity.ToModel()` extension per entity.
- **Validate inside the store and throw the domain exception** (`EfCoreRecordCommentStore.cs:46-54`) — the endpoint translates to HTTP. Trim strings; enforce max lengths as `private const int`.
- **Timestamps are `DateTimeOffset.UtcNow` written as `.UtcDateTime`** into `*_at_utc` columns (`EfCoreRecordCommentStore.cs:64-73`).
- **Raw SQL is interpolated, never concatenated.** Prefer `FromSqlInterpolated($"…{issueId}…")` — EF parameterises the holes (`src/AutoNate.Web/Services/SystemIssues/SystemIssueRemediationDispatcher.cs:83-88`, `Services/Menus/EfCoreMenuStore.cs:507-516`):

  ```csharp
  // FOR UPDATE so we serialise with the loop dispatcher and concurrent
  // /remediate calls. The row count + bound id are dynamic so we use
  // FromSqlInterpolated.
  var row = await dbContext.SystemIssues
      .FromSqlInterpolated($"SELECT * FROM system_issues WHERE id = {issueId} FOR UPDATE")
      .SingleOrDefaultAsync(cancellationToken);
  ```
  The one sanctioned raw-string exception is dynamic column/ORDER BY assembly in `EfCoreRecordStore.ListAuthorizedAsync`, which still passes every user value through an explicit `NpgsqlParameter` array: `.SqlQueryRaw<long>(countSql, parameters.Select(p => p!).ToArray())` (`Services/Records/EfCoreRecordStore.cs:184-187`, `:209-211`). Schema DDL in `Persistence/DatabaseSchemaInitializer.cs` uses `ExecuteSqlRawAsync` with `string.Format` placeholders — double literal braces (`{{`) there (`.claude/skills/add-projection/SKILL.md` step 1).
- **Case-insensitive search uses `EF.Functions.ILike`** (`CabinetEndpoints.cs:42-44`); uniqueness checks use `.ToLower() == .ToLower()` under a `#pragma warning disable CA1304, CA1311` with rationale (§1.1).
- **Postgres unique violations** are caught by SqlState: `private const string PgUniqueViolation = "23505";` (`Services/Datasets/EfCoreDatasetStore.cs:12`).

### 1.5 Snapshot caches (RecordTypeShortCodeCache pattern)

Use this shape for any rarely-changing lookup that is read on a hot path (`src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs:23-79`):

```csharp
public sealed class RecordTypeShortCodeCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<RecordTypeShortCodeCache> logger)
    : IRecordTypeShortCodeResolver
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyDictionary<Guid, string> _byId = new Dictionary<Guid, string>();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await dbContext.RecordTypes.AsNoTracking()
                .Select(rt => new { rt.Id, rt.ShortCode }).ToListAsync(cancellationToken);
            _byId = populated.ToDictionary(r => r.Id, r => r.ShortCode);   // swap whole snapshot
            _logger.LogInformation("Record-type short-code cache refreshed: {Count} entries.", _byId.Count);
        }
        finally { _refreshLock.Release(); }
    }
}
```

- Readers get lock-free `TryGet…` over an immutable dictionary; writers replace the reference under a `SemaphoreSlim(1,1)`. Bursts coalesce on the lock.
- Register as a singleton; pair with an `IHostedService` initializer that (a) refreshes once in `StartAsync` and **does not fail startup** on error (`:97-109`), and (b) subscribes to `HookPoints.AuditEventPublished` with a filter on the relevant event types to re-refresh (`:114-131`). The `_hookHandle` is kept for disposal.
- Do **not** implement `IDisposable` for the `SemaphoreSlim` (`.editorconfig:268-277` explains the parallel-test failure this caused).

### 1.6 BackgroundService loops (PeriodicIssueDetector pattern)

All periodic work inherits this loop discipline (`src/AutoNate.Web/Services/SystemIssues/Detectors/PeriodicIssueDetector.cs:17-108`). Copy it; don't hand-roll `while(true)`:

```csharp
// Capacity 1 so bursts of RequestImmediateScan() calls coalesce into a
// single wake-up.
private readonly SemaphoreSlim _wakeSignal = new(initialCount: 0, maxCount: 1);

public void RequestImmediateScan()
{
    try { _wakeSignal.Release(); }
    catch (SemaphoreFullException) { /* a wake is already pending */ }
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_options.DetectorsEnabled) { logger.LogInformation("Detector {DetectorId} disabled …", DetectorId); return; }
    try { await Task.Delay(InitialStagger(), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
        try { await RunOnceAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Detector {DetectorId} tick failed; retrying after Interval.", DetectorId);
        }
        try { await _wakeSignal.WaitAsync(Interval, stoppingToken); }
        catch (OperationCanceledException) { return; }
    }
}
```

Rules:

- **Master switch from options** (`SystemIssues:DetectorsEnabled`, `Projections:WorkerEnabled`, `FlowableCache:RetentionEnabled`) so tests can silence loops (`tests/AutoNate.Web.Tests/AutoNateWebApplicationFactory.cs:68-83`).
- **Expose `RunOnceAsync` publicly** so tests drive one tick without the loop (`PeriodicIssueDetector.cs:14-16`, `:36`).
- **Exception isolation:** catch everything except cancellation; `LogError` and continue. One bad tick never kills the service.
- **Coalescing wake signal:** `SemaphoreSlim(0, 1)` + `Release()` swallowing `SemaphoreFullException`; the loop waits on `WaitAsync(Interval, token)`. Mutation paths call `RequestImmediateScan()` from request threads without `Task.Run` (`EfCoreMenuStore` after every menu write, per `:38-46`).
- **Startup stagger** (`InitialStagger()` default 5 s, overridable) to avoid thundering herds.
- The `catch (OperationCanceledException) { return; }` / `when (token.IsCancellationRequested)` forms are the only blessed empty catches (`.editorconfig:144-149`).

### 1.7 Audit events

Non-record domains publish through `IAuditEventPublisher` (`src/AutoNate.Web/Services/Events/AuditEventPublisher.cs:19-39`):

```csharp
Task PublishAsync(
    string topicName,      // "iam.events", "site.events", …
    string eventType,      // "iam.group.created" — <domain>.<noun>.<verb>; reads end .viewed / .list.viewed
    string resourceKind,   // "group"
    object? resource,      // tiny: new { id, name }; null for list/search
    object? details,       // resultCount / page / filterHash; never row contents or PII
    CancellationToken cancellationToken = default);
```

- Publish **after** the store call succeeds and **on the 200 path only** — not on 404 (`GroupEndpoints.cs:36-45`). Reads publish `*.viewed` with `resource: new { id, name }`; lists publish `*.list.viewed` with `resource: null` and `details: new { resultCount, … }` (`GroupEndpoints.cs:21-29`).
- Use the domain constant classes (`IamEventTopic.TopicName`, `IamEventTypes.X`, `IamResourceKinds.X`; `ContentEventTopic`, `RecordSchemaEventTopic`, …). Never string-literal a topic in a handler.
- Every new event type is added to `Services/Events/EventCatalog.cs` or it silently won't appear in the SPA Events page; every new topic prefix must be appended to `DesiredStreams[0].Subjects` in `Services/Nats/NatsStreamProvisioner.cs`. Full checklist: `.claude/skills/add-audit-event/SKILL.md`. Record lifecycle events use the typed `IRecordEventPublisher` instead (`.claude/skills/add-record-event-type/SKILL.md`).
- Hot polled routes wrap the publish in a per-user `IMemoryCache` gate (60 s) — see skill step 8.
- Delete handlers snapshot the entity **before** deleting so the event carries a name, not just a UUID (`GroupEndpoints.cs:159-168`).

### 1.8 Logging

- Structured `ILogger<T>` with named placeholders, never string interpolation: `logger.LogError(ex, "Detector {DetectorId} tick failed; retrying after Interval.", DetectorId)`.
- Level usage (counts in `src/AutoNate.Web`): `LogWarning` 113, `LogInformation` 68, `LogError` 63, `LogDebug` 11, `LogTrace`/`LogCritical` 0. Use: **Error** = a unit of work failed and will be retried/skipped (loop ticks, startup refresh); **Warning** = degraded but handled (upstream Postgres error mapped to 502, cache refresh after an event failed); **Information** = lifecycle (cache refreshed, detector disabled); **Debug** = rare tracing.
- Always pass the exception as the first argument when you have one (`RecordTypeShortCodeCache.cs:108`, `:129`).
- `CA1848`/`CA1873` (LoggerMessage delegates) are downgraded to suggestion — plain `LogXxx` calls are the standard (`.editorconfig:81-91`).

### 1.9 Plugins

- Plugin csproj bodies are empty; settings inherit from `plugins/Directory.Build.props:10-24` (`net10.0`, nullable, reference to `AutoNate.Plugin.Abstractions` with `<Private>false</Private>` — **required** for type identity across the collectible `AssemblyLoadContext`). Never reference `AutoNate.Web` from a plugin.
- Layout, manifest, `Configure`/`Cleanup` contract, hooks, per-plugin schema and migrations: `.claude/skills/plugin-creator/SKILL.md`; worked examples `plugins/HelloPlugin/`, `plugins/Auditor/`.

### 1.10 Project skills are part of the convention

Multi-file wiring recipes live in `.claude/skills/`: `add-permission-gate`, `add-workflow-execution-action`, `add-audit-event`, `add-record-event-type`, `add-projection`, `add-page-context-provider`, `plugin-creator`, `mantine-form`, `mantine-combobox`, `mantine-custom-components`. Invoke the matching skill before touching those surfaces, and **fix any drift in the SKILL.md in the same commit** as the feature (auto-memory `feedback_skill_drift.md`).

---

## 2. TypeScript / React SPA (src/AutoNate.Spa)

### 2.1 Toolchain

- Vite + React 19 + TypeScript `strict: true`, `moduleResolution: bundler`, path alias `@/* → ./src/*` (`src/AutoNate.Spa/tsconfig.app.json`). Import app code via `@/…`, never relative `../../`.
- Scripts (`src/AutoNate.Spa/package.json:6-12`): `npm run dev`, `npm run build` (= `tsc -b && vite build`), `npm run type-check`, `npm run lint` (= `eslint src --max-warnings=162 --report-unused-disable-directives`).
- **Warning ratchet:** `--max-warnings=162` is the current count; it may only go **down**. If your change adds warnings, fix them; if it removes some, lower the number in the same commit (999 → 411 in `8674f2fb`; 411 → 164 when #118 escaped 234 JSX entities and dropped 13 unused disable directives). `--report-unused-disable-directives` is part of the script so a directive that stops being needed fails lint instead of lingering, and `eslint-comments/require-description` is an **error**: every `eslint-disable` must carry a `-- reason` saying why it is safe, because a bare `exhaustive-deps` disable is indistinguishable from a stale-closure bug someone silenced (#32).
- ESLint flat config (`src/AutoNate.Spa/eslint.config.js`): `react-hooks/rules-of-hooks` is an **error** (`:59`), `react-hooks/exhaustive-deps` warn (`:60`); jsx-a11y recommended at warn (`:89-97`); `@typescript-eslint/no-explicit-any` off (`:80`); unused vars warn with `_`-prefix escape (`:81`). Real-bug rules stay errors — a lint error blocks; a warning counts against the ratchet.
- No unit-test runner in the SPA (no vitest/jest in `package.json`); SPA behaviour is verified by Playwright E2E (see `Testing.md`).

### 2.2 File naming and layout

| Location | Rule | Evidence |
|---|---|---|
| `src/api/<noun>.ts` | camelCase file; exports plain `async function`s over the shared `api` axios instance; request/response `type`s live beside them | `src/api/users.ts:1-31` |
| `src/hooks/use<Noun>.ts` | React Query hooks; exports `<NOUN>_QUERY_KEY` const + `use<Noun>()` + `use<Verb><Noun>()` mutations | `src/hooks/useUsers.ts:17-46` |
| `src/pages/<area>/<PageName>.tsx` | PascalCase component file, default export, one page per file; sibling `use<Page>PageContext.ts`, `<page>Schemas.ts`, `utils.ts` | `src/pages/admin/Groups.tsx`, `src/pages/manage-users/userSchemas.ts`, `src/pages/notes/useNotesPagePageContext.ts` |
| `src/components/<Name>.tsx` or `src/components/<area>/` | Shared UI; `data-table/DataTable.tsx`, `PageHeader.tsx`, `ConfirmModal.tsx` | `src/components/` listing |
| `src/types/<noun>.ts` | Shared wire types (`LocalUser` in `types/flowable.ts`) | `src/api/users.ts:2` |
| `src/lib/` | Non-React helpers (`siteAppearance.ts`, `blocknote/`, `bpmn/`, `cron/`, `yjs/`) | `src/lib/` listing |
| `src/routes/appRoutes.tsx` | All in-shell routes; `src/router.tsx` only for out-of-shell routes (document editor, public share) | `src/router.tsx:14-52` |
| `src/agent/pageContext/` | Chatbot page-awareness framework (do not edit per page) | `.claude/skills/add-page-context-provider/SKILL.md` |

Heavy pages are lazy: `const PipelineEditor = lazy(() => import("@/pages/admin/pipelines/PipelineEditor"));` wrapped in `<Suspense fallback={null}>` (`src/routes/appRoutes.tsx:19-26`). Every in-shell route is wrapped with `ProtectedRoute` via the `protect()` helper (`appRoutes.tsx:57`).

### 2.3 API clients and React Query hooks

The axios instance (`src/api/client.ts:5-11`) is the only HTTP client — `baseURL: "/"`, `withCredentials: true`, JSON content type. Its interceptor rejects any `text/html` response on `/api` (SPA-fallback guard, `:13-32`) and redirects to login on 401 except for the `/api/auth/me` probe (`:33-47`). Do not create a second axios instance or use `fetch` for `/api`.

Client function shape (`src/api/users.ts:19-22`):

```ts
export async function listUsers(signal?: AbortSignal): Promise<LocalUser[]> {
  const { data } = await api.get<LocalUser[]>("/api/users", { signal });
  return data;
}
```

Hook shape (`src/hooks/useUsers.ts:17-37`):

```ts
export const USERS_QUERY_KEY = ["users"] as const;

export function useUsers() {
  return useQuery<LocalUser[]>({
    queryKey: USERS_QUERY_KEY,
    queryFn: ({ signal }) => listUserDirectory(signal)
  });
}

export function useCreateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateUserRequest) => createUser(request),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_QUERY_KEY })
  });
}
```

- Always thread `signal` from `queryFn` into the client so navigation cancels in-flight requests.
- Parameterised keys are functions returning `as const` tuples: `USER_SUPERVISOR_KEY = (userId) => ["users", userId, "supervisor"] as const` with `enabled: !!userId` (`useUsers.ts:71-79`).
- Mutations invalidate the list key on success; nothing else refetches manually.
- Real-time invalidation: `useInvalidateOnChannels(channels, queryKeys)` subscribes to bus channels and invalidates the given keys (`src/hooks/useInvalidateOnChannels.ts:9-27`). Use it instead of polling.
- Permission gating in the UI uses the batched `usePermissionChecks(checks)` + `permissionKey(check)` pair; build the `checks` array in a `useMemo`, read with `map?.get(permissionKey(...)) ?? false` (`src/hooks/usePermissionChecks.ts:20-40`, `src/pages/manage-users/ManageUsers.tsx:70-75`). Kind-level checks use `id: "*"`.
- Error text for `Alert`s comes from `describeError(err)` — the exported one is `src/pages/workflow-executions/utils.ts`; three private copies exist in `src/components/workflow/*Modal.tsx` (`TaskFormModal.tsx:124`, `GatewayChoiceModal.tsx:83`, `SimpleCompleteTaskModal.tsx:77`). Import the exported one; don't add a fourth.
- QueryClient defaults: `refetchOnWindowFocus: false`, `retry: 1`, `staleTime: 30_000` (`src/main.tsx:24-31`). Don't override per hook without a reason.

### 2.4 Page component structure

Canonical page (`src/pages/admin/Groups.tsx:18-54`):

```tsx
import PageHeader from "@/components/PageHeader";
import { useUsers } from "@/hooks/useUsers";
import { useCreateGroup, useDeleteGroup, useGroups } from "@/hooks/useAdmin";

export default function Groups() {
  const { data: groups = [], isLoading } = useGroups();
  const create = useCreateGroup();
  const [error, setError] = useState<string | null>(null);
  …
  return (
    <>
      <PageHeader
        title="Groups"
        description="Group users together so role assignments and permissions can target many people at once."
      />
      {error && (<Alert color="red" variant="light" mb="md">{error}</Alert>)}
```

- **Hooks first, before any early `return`.** This includes `use<Page>PageContext(...)` and `useDocumentTitle(...)`; a hook below `if (isLoading) return <Loader/>` produces "rendered more hooks than during the previous render" (`.claude/skills/add-page-context-provider/SKILL.md` "Common slip-ups").
- Start the page with `<PageHeader title description actions? />` (`src/components/PageHeader.tsx:15-28`).
- Set the tab title with `useDocumentTitle("Data Stores")` (`src/hooks/useDocumentTitle.ts:12-22`; usage `src/pages/admin/datastores/DataStoresPage.tsx:40`). Only four pages call it today — **new pages must**; retrofit when touching an old one.
- Modal state as a discriminated union: `type ModalState = { kind: "none" } | { kind: "edit"; user: LocalUser } | …` (`src/pages/manage-users/ManageUsers.tsx:36-41`).

### 2.5 UI: Mantine-only

- **Mantine v9 is the sole UI framework.** No Bootstrap, no ColorAdmin, no `--bs-*`, `bi-*`, `form-control`, `panel-*`, `btn btn-*`, `row/col-*`, `mb-3` — they render nothing (auto-memory `project_coloradmin_removed.md`; CLAUDE.md "Mantine v9"). Do not re-add `react-hook-form`, `@tanstack/react-table`, TipTap.
- Look up component props via `docs/mantine/llms.txt` first, then the `mantine` MCP server, then `https://mantine.dev/llms-full.txt` (auto-memory `reference_mantine_docs.md`).
- Icons are FontAwesome: `<i className="fa fa-plus" />` (354 usages; 0 `bi-*`). CSS loaded once in `src/main.tsx:12`.
- **Tooltips: `<Tooltip label="…" withArrow position="bottom">` from `@mantine/core`, keep `aria-label` on the target, and do not also set `title=`** (auto-memory `feedback_use_mantine_tooltip.md`). Exception/backlog: 182 `title="…"` attributes remain in `.tsx` vs 41 files using `<Tooltip>` — convert when you touch them; don't add new `title=`.
- Toasts: `notifications.show({...})` from `@mantine/notifications` (`<Notifications />` mounted in `src/main.tsx:47`).
- Confirmations: `src/components/ConfirmModal.tsx` or `@mantine/modals` (`ModalsProvider` in `main.tsx:44`).

### 2.6 Forms: `@mantine/form` + zod

(`src/pages/manage-users/ManageUsers.tsx:3-4`, `:333-337`; schema `src/pages/manage-users/userSchemas.ts:3-11`)

```ts
import { useForm } from "@mantine/form";
import { zod4Resolver as zodResolver } from "mantine-form-zod-resolver";

export const createUserSchema = z.object({
  username: z.string().trim().min(1, "Username is required").max(150),
  email: z.email("Email must be valid").trim().or(z.literal(""))
});
export type CreateUserForm = z.infer<typeof createUserSchema>;

const form = useForm<CreateUserForm>({
  mode: "controlled",
  initialValues: { username: "", firstName: "", lastName: "", password: "", email: "" },
  validate: zodResolver(createUserSchema)
});
const onSubmit = form.onSubmit(async (values) => { … await create.mutateAsync(values) … });
```

- Schemas + inferred types live in a sibling `<page>Schemas.ts`; use `zod4Resolver` (zod v4 API: `z.email(...)`).
- Bind inputs with `form.getInputProps("field")`; keep `isSubmitting` local state around `mutateAsync`.
- Deeper patterns (nested/array fields, `createFormContext`, uncontrolled mode): invoke the `mantine-form` skill.

### 2.7 Tables: the `DataTable` wrapper

Never import `mantine-datatable` directly in a page — go through `src/components/data-table/DataTable.tsx`. Column shape mirrors the old `ColumnDef` subset (`DataTable.tsx:27-35`):

```ts
export type DataTableColumn<T> = {
  id?: string;
  accessorKey?: keyof T & string;
  accessorFn?: (row: T) => unknown;
  header?: ReactNode | ((ctx: DataTableHeaderContext<T>) => ReactNode);
  cell?: (ctx: DataTableCellContext<T>) => ReactNode;
  enableSorting?: boolean;
  meta?: { wrap?: boolean };
};
```

Props: `mode: "client" | "server" | "auto"`, `loadAll` / `loadPage(req: DataTablePageRequest)`, `queryKey`, filter options (`DataTable.tsx:44-70`). Define `columns` and `pageSizeOptions` at module level or in `useMemo` — an unstable array reference triggers an infinite re-render inside mantine-datatable (`DataTable.tsx:37-42`). The empty-state string (`emptyMessage`) is what E2E tests assert on (`tests/AutoNate.E2E.Tests/PermissionGatingTests.cs:79-84`).

### 2.8 Chatbot page-context providers

For any page whose live state the assistant should see, create `src/pages/<area>/use<Page>PageContext.ts` and call it once at the top of the page (`src/pages/notes/useNotesPagePageContext.ts:1-12`, `:41-45`):

```ts
const PAGE_KEY = "notes";
export function useNotesPagePageContext(options: Options): void {
  const optsRef = useRef(options);
  optsRef.current = options;
  const getSnapshot = useCallback((): PageSnapshot | null => { /* read optsRef.current */ }, []);
  …
  useRegisterPageContext(entry);
}
```

Rules: snapshot ≤ 64 KB, synchronous `getSnapshot`, read state via refs, `pageKey` must match `src/agent/usePageKey.ts`, mutations are in-memory only (never persist from `onPageAction`), opt sensitive inputs out with `data-agent-exclude`. Full recipe: `.claude/skills/add-page-context-provider/SKILL.md`. Existing providers: `pages/{dashboard,notes,query,workflow}/…`, `pages/admin/{datastores,datasets}/…`.

### 2.9 Editors

- Notes/pages: BlockNote `@blocknote/{core,react,mantine} ^0.51` (`useCreateBlockNote`, `BlockNoteView`, `editor.document`). No `@tiptap/*` imports; the `overrides` pin in `package.json` for `@tiptap/core` stays (auto-memory `project_editor_stack.md`).
- Documents: `@eigenpal/docx-editor-react` mounted in `src/components/documents/DocxDocumentEditor.tsx`, Yjs fragment name **`"default"`**, `key={role}` mandatory, button reset scoped under `.ep-root` in `DocxDocumentEditor.css` (auto-memories `project_documents_editor.md`, `feedback_docx_editor_button_reset.md`).
- Collaboration goes through the existing Hocuspocus sidecar with a new `kind:` id prefix — never a second sync server (auto-memory `project_collab_foundation.md`).

### 2.10 CSS and theming

- Global CSS is limited to `src/index.css` and `src/widgets.css`. `widgets.css` is "Mantine-tokens only — no `var(--bs-*)`" (`src/widgets.css:1-5`) and uses `var(--mantine-color-text)`, `var(--mantine-color-dimmed)` (`:16-33`).
- **Theme colours come from CSS vars, never literals.** `applySiteAppearanceToDocument` publishes `--app-header-*`, `--app-top-menu-*`, `--app-sidebar-*` and `--mantine-color-body/text` (`src/lib/siteAppearance.ts:363-387`). Shell chrome reads them with fallbacks (`src/shell/headerStyles.ts:6-10`):

  ```ts
  export const HEADER_BG = "var(--app-top-menu-bg, #20252a)";
  export const HEADER_FG = "var(--app-top-menu-link-color, rgba(255,255,255,0.78))";
  ```
- The Mantine theme object is static and module-level (`src/providers/MantineRoot.tsx:6-17`); brand colour changes flow via `--mantine-color-brand-*` vars. Do not rebuild the theme on state changes (it infinite-looped `ColorInput`).
- Component-scoped CSS files sit beside the component (`DocxDocumentEditor.css`). Prefer Mantine style props (`mb="md"`, `c="dimmed"`) over inline `style` objects; when an inline style is unavoidable it must reference a `--app-*`/`--mantine-*` var. Backlog: 92 inline hex colour literals remain in `.tsx` — don't add to them.
- Scratch output (Playwright snapshots, debug captures) goes under `/temp/` (gitignored, `.gitignore:62`), never the repo root (auto-memory `feedback_temp_files_location.md`).

### 2.11 Deleting TypeScript modules

A grep-only "zero importers" verdict is not evidence (multi-line destructured imports; `WorkflowStudio.tsx` is classified binary by `grep`). The only accepted verification is the trial delete: `mv x.ts x.ts.bak && rm -f src/AutoNate.Spa/tsconfig.*.tsbuildinfo && npx tsc -b --force` (auto-memory `feedback_unused_ts_module_verification.md`). After installing large ESM packages: `rm -rf node_modules/.vite && npm run dev -- --force` (auto-memory `feedback_vite_force_after_heavy_deps.md`).

---

## 3. Node sidecars (services/hocuspocus, services/executor)

- Plain `tsc` builds, ESM (`"type": "module"`), `strict: true`, `target ES2022`, `moduleResolution: Bundler`, output to `dist/` (`services/hocuspocus/tsconfig.json`, `services/executor/tsconfig.json`). Scripts: `build`/`start`/`dev` only (`services/*/package.json`). **No ESLint config** in either service — TypeScript strictness is the lint.
- Intra-service imports use the `.js` extension (`import { createAuthHook } from "./auth.js";`, `services/hocuspocus/src/index.ts:2-4`).
- **Configuration is env-var only, read once at module top**; required vars fail fast (`services/hocuspocus/src/index.ts:6-25`):

  ```ts
  function requireEnv(name: string): string {
    const value = process.env[name];
    if (!value) {
      console.error(`Missing required environment variable: ${name}`);
      process.exit(1);
    }
    return value;
  }
  const port = Number.parseInt(process.env.HOCUSPOCUS_PORT ?? "1234", 10);
  const sharedSecret = requireEnv("YJS_INTERNAL_SHARED_SECRET");
  ```
  Optional vars carry a default (`NATS_URL ?? "nats://localhost:4222"`, `services/executor/src/index.ts:13`).
- Logging is `console.log/error` with a `[service]` prefix (`services/executor/src/index.ts:20`).
- Sidecars own no auth decisions: Hocuspocus calls back to .NET `POST /internal/yjs-auth` with the shared secret; .NET remains the source of truth (`services/hocuspocus/README.md` "What it does"). The executor replies `{ success: false, errorMessage }` rather than disconnecting (`services/executor/src/index.ts:6-11`).
- Each sidecar has its own Dependabot group (`.github/dependabot.yml`).

---

## 4. Git and n8SDLC workflow

### 4.1 Commit messages (observed in `git log`)

- Subject: imperative, ≤ ~72 chars, capitalised, no trailing period. Feature commits state the user-visible outcome (`Add Files-backed datasets: CSV/raw parsers, file/folder scope, schema preview`, `98d99c82`; `Fix two test-suite failures: writer-role catalog race and /api 404 guard`, `bdc72176`). Housekeeping uses a `chore:` / `chore(n8):` prefix (`8674f2fb`, `01f0f174`).
- Body: bullet list of what changed per file/area **and why**; the fix commit `bdc72176` explains the race and the routing-DFA reason. Multi-phase feature commits start the body with `**Shipped Phase N of …**` and reference the plan file (`b1b8b9fc`, `cc80bc78`).
- Trailers on agent-authored commits:
  ```
  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_…
  ```
- Issue linkage: `Refs #N` in the body (`01f0f174`: `Refs #7-#93 (audit findings filed 2026-08-31)`). Never `Fixes`/`Closes` in commits — closing keywords are silent no-ops off the default branch and belong in the PR body (n8-exec `SKILL.md:34`).

### 4.2 Branches and PRs

- Default branch is **`master`** — not `main`; n8SDLC docs say `main`, target the default branch (`.n8/config.yml` `default_branch: master`; auto-memory `project_n8sdlc_workflow.md`).
- Milestone work: one branch per milestone `milestone/m<N>-short-name` off up-to-date default (n8-exec `SKILL.md:23`); one commit per story with `Refs #N`; PR body lists `Closes #N` for every **completed** story only (`SKILL.md:36`). Non-milestone work follows the same shape with a descriptive branch (`n8-proj-mgmt`); Dependabot branches are `dependabot/npm_and_yarn/...`.
- GitHub Issues are the plan; every issue carries exactly one `area:*` label from `.n8/config.yml` (`api, spa, plugins, services, flowable, infra, ci, docs, tests`) plus `sev:*` for audit findings. Issue forms: `.github/ISSUE_TEMPLATE/{epic,story,bug}.yml`.
- Any change that deviates from what planned issues assume (library, provider, architecture, scope, an invariant) gets an `## Ad-hoc — <date>` entry in `.n8/decisions.md` and a `/n8-replan` suggestion to the user (`CLAUDE.md:29-33`). Format is in that file's header.
- Plans predating n8SDLC live in `docs/plans/YYYY-MM-DD-kebab.md` as historical context; don't add new ones unless asked (auto-memory `reference_plan_location.md`).
- Whole-codebase audits run via `/n8-audit` with checklists in `.n8/memory/audit-*.md`; a "dead code" claim needs a trial delete before filing (`.n8/memory/audit-conventions.md`).
- There is no CI workflow (`.github/workflows` does not exist — see issue #79). Until it does, the local gate before merge is `dotnet build AutoNate.sln && (cd src/AutoNate.Spa && npm run lint && npm run build) && dotnet test AutoNate.sln` (`README.md:96-101`).
