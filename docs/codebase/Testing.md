# Testing

One-line summary: how the three test projects are organised (`AutoNate.Web.Tests` xUnit integration suite against a per-test Postgres DB, `AutoNate.E2E.Tests` Playwright against a spawned host, `AutoNate.Web.Tests.SamplePlugin` staging), the exact commands and infra they need, the canonical enforcement / gate-presence / endpoint / store / E2E test shapes to copy, and where the known gaps are tracked.

> Generated from commit 01f0f174 on 2026-08-31 by /n8-map.

---

## 1. Layout

```
tests/
  AutoNate.Web.Tests/                  # xUnit; 1,261 [Fact]/[Theory] attributes (12 Theory); 115 classes tagged Integration
    AutoNateWebApplicationFactory.cs   # WebApplicationFactory<Program> + per-test Postgres DB + stubs
    PostgresTestDatabase.cs            # creates autonate_test_<guid>, replays infra SQL, store factories
    StubFlowableClient.cs              # IFlowableClient double (records Calls, canned responses)
    StubHttpMessageHandler.cs
    RecordingAuditEventPublisher.cs    # IAuditEventPublisher double (captures envelopes)
    RecordingRecordEventPublisher.cs   # IRecordEventPublisher double
    EmptyRecordTypeShortCodeResolver.cs
    <Noun>EndpointsTests.cs            # HTTP-level tests per endpoint group (RecordCommentEndpointsTests.cs …)
    EfCore<Noun>StoreTests.cs          # store-level tests against PostgresTestDatabase
    <Domain>EventPublishingTests.cs    # audit-event assertions per domain
    Authorization/                     # *EnforcementTests.cs (22 files), AuthorizationGatePresenceTests.cs, authorizer/selector unit tests
    Datasets/ Hooks/ Plugins/ Query/ Storage/ Workflow/
  AutoNate.E2E.Tests/                  # Playwright .NET; 136 [Fact]s across 28 spec files
    AutoNateE2EFixture.cs              # boots AutoNate.Web child process + AutoNate_E2E DB + Chromium
    AutoNateE2ECollection.cs           # single shared collection
    Support/E2ETestBase.cs             # NewSignedInAsAdminAsync() -> SignedInSession
    Support/ConsoleErrorGuard.cs       # fails the test on console.error / pageerror
    Support/ApiSeeder.cs               # seed data via the signed-in API request context
    Support/TestNames.cs               # ShortSlug() / Prefixed() / ShortCode()
    <Feature>Tests.cs                  # one spec file per feature area
  AutoNate.Web.Tests.SamplePlugin/     # tiny IAutoNatePlugin staged into test-plugins/SamplePlugin for PluginLoaderTests
```

Naming rules:

- Test class = `<Subject>Tests` (`RecordCommentEndpointsTests`, `EfCoreRecordCommentStoreTests`); permission tests = `<Surface>EnforcementTests` under `Authorization/`.
- Test method = `Method_Scenario_Outcome` in PascalCase (`ListComments_NoGrant_Returns403`, `tests/AutoNate.Web.Tests/Authorization/RecordCommentEnforcementTests.cs:20`) **or** snake_case sentence style (`GetRecordsList_publishes_record_list_viewed_with_metadata`, `ViewEventPublishingTests.cs:40`). Both are sanctioned — `CA1707`/`S2701` are off for `tests/**` (`.editorconfig:322-327`, `:252-254`). Match the file you are editing.
- Every integration class carries `[Trait("Category", "Integration")]` (`RecordCommentEnforcementTests.cs:9`). There is no `Unit` trait; pure unit tests simply omit the attribute.
- Test files are `sealed class`, no base class, no `[Collection]` (zero collection definitions in `AutoNate.Web.Tests`) — isolation comes from a fresh factory + fresh database per test, so xUnit parallelism is safe.

---

## 2. Running the suites

### 2.1 Infra prerequisite (Docker Desktop up)

```bash
cd infra && docker compose -p infra up -d postgres nats nats-init redis
```

`-p infra` is mandatory — `infra/ensure-up.sh` pins that project name (auto-memory `reference_test_suite_infra.md`). Without Postgres on `localhost:5432` roughly 680 tests fail in ~1 s with connection-refused noise; without NATS on 4222 another ~360 fail. `make infra-ensure` / `make app` also work but bring up Flowable + Dapr too.

### 2.2 Backend suite

```bash
dotnet test AutoNate.sln                              # ~8 min, all projects incl. E2E
dotnet test tests/AutoNate.Web.Tests                  # backend only (the inner loop)
dotnet test tests/AutoNate.Web.Tests --filter "FullyQualifiedName~RecordCommentEnforcementTests"
```

(`README.md:96-101`.) The suite is green under parallel load as of commit `bdc72176`. Never run the whole suite as a "does it compile" check — `dotnet build AutoNate.sln` does that with analyzers.

Environment overrides:

- `AUTONATE_POSTGRES_PASSWORD` — password for the `autonate` role; default `Your_password123!` (`tests/AutoNate.Web.Tests/PostgresTestDatabase.cs:31-32`).
- `AUTONATE_POSTGRES_PORT` — honoured by the **E2E** fixture only (`tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs:36-38`, `:240`). `PostgresTestDatabase.ConnectionString` hardcodes `Port=5432` (`PostgresTestDatabase.cs:41-42`).
- `AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true` is set by the factory itself (`AutoNateWebApplicationFactory.cs:24-25`).

### 2.3 E2E suite

```bash
make e2e                                  # infra-ensure + build + Playwright chromium install + dotnet test --no-build
# or
make infra-ensure && make e2e-install && dotnet build tests/AutoNate.E2E.Tests && dotnet test tests/AutoNate.E2E.Tests
PWDEBUG=1 dotnet test tests/AutoNate.E2E.Tests   # headed browser
```

Always run `make e2e-install` (or `pwsh tests/AutoNate.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`) before a bare `dotnet test`: `Microsoft.Playwright` pins an exact browser build (1.50.0 → `chromium_headless_shell-1155`), and a cache populated by `@playwright/mcp` or `playwright-cli` has newer builds only — the symptom is every test failing instantly with `Executable doesn't exist at …/chromium_headless_shell-1155/…`.

The fixture (`AutoNateE2EFixture.StartAppAsync`) probes `GET /` after `Now listening` and throws with the app's stdout/stderr tail if the SPA shell (`<div id="root">`) isn't served — so a host-side regression surfaces as one clear fixture error, not N identical 30 s sign-in timeouts (#132). The host-side guard for the same thing is `tests/AutoNate.Web.Tests/SpaRootFallbackTests.cs`.

(`Makefile:91-100`, `tests/AutoNate.E2E.Tests/README.md` "Running".) First run rebuilds the SPA into `wwwroot/` (30–60 s). Do not run two E2E invocations concurrently — both target the `AutoNate_E2E` database.

---

## 3. Backend fixtures

### 3.1 `AutoNateWebApplicationFactory`

`tests/AutoNate.Web.Tests/AutoNateWebApplicationFactory.cs:28-33`:

```csharp
public static async Task<AutoNateWebApplicationFactory> CreateAsync(
    IReadOnlyDictionary<string, string?>? extraConfig = null)
{
    var database = await PostgresTestDatabase.CreateAsync();
    return new AutoNateWebApplicationFactory(database, extraConfig);
}
```

What it does for you (`:50-111`):

- Fresh database per factory; disposed with the factory (`await using var factory = …` is mandatory).
- `Environment = Development`, `DevelopmentAutoLogin:Enabled=true` as `admin` — the first request (conventionally `await client.GetAsync("/api/auth/me");`) signs the client in.
- **Authorization is off by default** (`Authorization:Enabled=false`, `Enforcement=off`, `AssignSuperAdminToAllExistingUsers=false`). Tests that need enforcement pass `extraConfig` (§5).
- All background loops silenced: `SystemIssues:DetectorsEnabled=false`, `SystemIssues:RemediationEnabled=false`, `Projections:WorkerEnabled=false`, `FlowableCache:RetentionEnabled=false` (`:68-83`). Drive detectors/projections with their public `RunOnceAsync`/`ApplyAsync` instead.
- Services replaced: `IFlowableClient → StubFlowableClient`, `IAuditEventPublisher → RecordingAuditEventPublisher`, `IRecordEventPublisher → RecordingRecordEventPublisher` (`:97-111`). Reach them via `factory.FlowableStub`, `factory.RecordedAuditEvents`, `factory.RecordedRecordEvents` (`:37-43`).
- Real services are reachable for seeding: `await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<IRecordStore>()` (`Authorization/RecordEditEnforcementTests.cs:36-47`).

### 3.2 `PostgresTestDatabase`

Creates `autonate_test_<guid>`, replays `infra/postgres/init/01-*.sql` + `02-*.sql` (linked into the test csproj, `AutoNate.Web.Tests.csproj` `<Content Include=…>`), and exposes **store factories** for store-level tests (`PostgresTestDatabase.cs:51-120`): `CreateLocalUserStore()`, `CreateWorkflowStore()`, `CreateRecordTypeStore()`, `CreateRecordStore(authorizationEnabled, enforcement, eventPublisher, notificationStore)`, `CreateRecordCommentStore()`, `CreateRoleStore()`, `CreatePermissionGrantStore()`, `CreateRoleAssignmentStore()`, … Add a `Create<Noun>Store()` here when you add a store; wire real collaborators with `NullLogger<T>.Instance` and `Options.Create(...)`.

### 3.3 `StubFlowableClient`

`tests/AutoNate.Web.Tests/StubFlowableClient.cs:12-19`: records every call as a string in `Calls` (`"Deploy:{processKey}"`), and exposes seedable dictionaries (`InstancesById`, `TasksByUser`, `ProcessDefinitionsByKey`) that authorization handlers consult. Assert on `factory.FlowableStub.Calls` for "endpoint reached the client with the right id".

### 3.4 Recording publishers

`RecordingAuditEventPublisher.Events` / `.Clear()` (`tests/AutoNate.Web.Tests/RecordingAuditEventPublisher.cs:9-25`). Standard assertion (`ViewEventPublishingTests.cs:31-36`, `:47-56`):

```csharp
factory.RecordedAuditEvents.Clear();
(await client.GetAsync($"/api/records/{rec.Id}")).EnsureSuccessStatusCode();
Assert.Contains(factory.RecordedAuditEvents.Events, e => e.EventType == RecordEventTypes.Viewed);

var listed = Assert.Single(factory.RecordedAuditEvents.Events, e => e.EventType == RecordEventTypes.ListViewed);
Assert.NotNull(listed.Details);
```

`Clear()` after seeding so the assertion sees only the request under test.

---

## 4. Test shapes to copy

### 4.1 Endpoint test (HTTP round trip)

`tests/AutoNate.Web.Tests/RecordCommentEndpointsTests.cs:10-40` + nested fixture `:157-199`:

```csharp
[Trait("Category", "Integration")]
public sealed class RecordCommentEndpointsTests
{
    [Fact]
    public async Task CreateComment_RoundTripsAndAppearsInList()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var createResponse = await fixture.Client.PostAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/",
            new CreateCommentRequest("Hello"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(created);
        Assert.Equal("Hello", created.Body);
```

```csharp
    private sealed class TestFixture : IAsyncDisposable
    {
        public static async Task<TestFixture> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            var client = factory.CreateClient();
            (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();   // primes auto-login
            var recordTypeResponse = await client.PostAsJsonAsync("/api/record-types/",
                new CreateRecordTypeRequest("task", "Task", null, null, null));
            …
            return new TestFixture(factory, client, record!.Id);
        }
```

Rules: reuse the endpoint file's own request/DTO records (`using AutoNate.Web.Endpoints;`) so wire-shape changes break the test at compile time; seed prerequisites through the API in a private nested `TestFixture`; assert status code **and** body; add a `*EventPublishingTests` fact for every new event type (§3.4).

### 4.2 Store test

`tests/AutoNate.Web.Tests/EfCoreRecordCommentStoreTests.cs:29-52`:

```csharp
[Fact]
public async Task CreateAsync_PersistsComment()
{
    await using var database = await PostgresTestDatabase.CreateAsync();
    var record = await SeedRecordAsync(database);
    var store = database.CreateRecordCommentStore();

    var comment = await store.CreateAsync(record.Id, "  hello world  ", Alice);

    Assert.Equal("hello world", comment.Body); // trimmed
    Assert.Equal(Alice, comment.AuthorId);
}

[Fact]
public async Task CreateAsync_RejectsEmptyBody()
{
    …
    await Assert.ThrowsAsync<RecordCommentValidationException>(() =>
        store.CreateAsync(record.Id, "   ", Alice));
}
```

Rules: no web host — `PostgresTestDatabase` + store factory only; well-known actor GUIDs as `private static readonly Guid Alice = Guid.Parse("11111111-…")` (the seeded admin id); one behaviour per fact (persist, trim, reject, not-found); validation asserts the typed exception.

### 4.3 Enforcement test — no-grant → 403 plus a positive control

Every `RequirePermission`/`RequireKindPermission` gate gets a pair: the **no-grant 403** (proves the gate exists and is on the right kind/action) and the **with-grant 200/204** (proves the gate is not over-tight). `tests/AutoNate.Web.Tests/Authorization/RecordEditEnforcementTests.cs:12-28`, `:49-58`, `:109-124`:

```csharp
// Covers `RequirePermission(EntityKinds.Record, Actions.Edit)` on
// PATCH /api/records/{id} (RecordEndpoints.cs:261) …
// Authoring regression net: if either endpoint's gate ever swaps to a wider
// action (e.g. View) or a different kind, the no-grant→403 case will flip.
[Trait("Category", "Integration")]
public sealed class RecordEditEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string action, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task Patch_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync($"/api/records/{recordId}", new { name = "renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_WithRecordEditGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Edit, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync($"/api/records/{recordId}", new { name = "renamed" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
```

Why this works: with `AssignSuperAdminToAllExistingUsers=false` the auto-logged-in `admin` has **zero grants**, so the same principal is the "no-grant user"; `GrantAsync` adds exactly one `(kind, action, selector)` grant to that user. Seed through real stores in a scope (bypasses the gates), then hit the endpoint with the `HttpClient`. Enforcement modes: `off | read-only | full` (`src/AutoNate.Web/Authorization/AuthorizationOptions.cs:19-24`).

Store-level enforcement (`FilterQueryAsync` paths) is tested against `PostgresTestDatabase` with a hand-built `ClaimsPrincipal` and `CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly)`; the grant is expressed as a selector string like `"/record/*[assignee=user]"` (`Authorization/RecordEnforcementTests.cs:24-31`, `:72-95`). Cover: no grant → empty, super-admin → all, scoped grant → subset, wildcard → all, deny overrides allow.

### 4.4 Gate-presence test (already exists — you make it pass)

`tests/AutoNate.Web.Tests/Authorization/AuthorizationGatePresenceTests.cs:33-72` enumerates `EndpointDataSource` and fails if any `/api/*` route lacks `IAllowAnonymous`, `RequirePermissionMetadata`, or `AuthorizationDecisionMetadata`:

```csharp
foreach (var endpoint in endpoints)
{
    if (endpoint is not RouteEndpoint route) continue;
    if (!IsAuditedSurface(route.RoutePattern.RawText ?? "(unknown)")) continue;
    if (!HasExplicitAuthDecision(route))
        problems.Add($"{method,-6} {pattern} -- requires sign-in but no auth decision metadata. " +
                     "Pick one: RequirePermission/RequireKindPermission, AuthorizedInHandler(reason), or OpenToAuthenticated(reason).");
}
Assert.True(problems.Count == 0, $"{problems.Count} endpoint(s) failed the gate check.");
```

It proves a gate **exists**, not that it is the right one — that is what §4.3 is for (issue #87). It also is why the `/api` 404 guard must stay middleware, not a route (`bdc72176`; auto-memory `reference_test_suite_infra.md`).

### 4.5 Detector / background-loop test

Do not start the `BackgroundService`. Construct the detector (or resolve it from `factory.Services`) and call `RunOnceAsync(ct)`; assert on the issue store. To test "mutation wakes the detector", assert `IsImmediateScanRequested` after the mutation (`src/AutoNate.Web/Services/SystemIssues/Detectors/PeriodicIssueDetector.cs:53-58`). Examples: `MisconfiguredMenuItemDetectorTests.cs`, `Phase5DetectorTests.cs`.

---

## 5. E2E (Playwright .NET)

### 5.1 Fixture and base class

- `AutoNateE2EFixture` (`tests/AutoNate.E2E.Tests/AutoNateE2EFixture.cs:24-75`) drops/recreates `AutoNate_E2E`, replays `02-create-autonate-app-schema.sql`, spawns `dotnet run --project src/AutoNate.Web --no-launch-profile -p:BuildSpa=true` with `DevelopmentAutoLogin__Enabled=false`, parses the bound URL, launches Chromium (headless unless `PWDEBUG=1`).
- One shared **collection** fixture for all spec files (`AutoNateE2ECollection.cs:11-16`); per-class fixtures would race three `dotnet run -p:BuildSpa=true` on `wwwroot/`.
- Inherit `E2ETestBase` — it carries the `[Collection]` attribute and `NewSignedInAsAdminAsync()` (`Support/E2ETestBase.cs:14-41`):

```csharp
public sealed class RecordTypeTests : E2ETestBase
{
    public RecordTypeTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task RecordTypeList_CreateViaModal_AddsRowToTable()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/record-types");
        await page.GetByRole(AriaRole.Button, new() { Name = "New record type" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var shortCode = TestNames.ShortCode();
        await modal.GetByLabel("Short code").FillAsync(shortCode);
        await modal.GetByLabel("Name").FillAsync(TestNames.Prefixed("inline"));
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(shortCode).First).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
```
(`tests/AutoNate.E2E.Tests/RecordTypeTests.cs:17-49`.)

### 5.2 Rules

- **Always `await using var session = await NewSignedInAsAdminAsync();`** — it installs `ConsoleErrorGuard` before sign-in and fails the test on dispose if any non-allowlisted `console.error` or `pageerror` fired (`Support/ConsoleErrorGuard.cs:25-47`, `:76-80`). Don't bypass it with a raw `Fixture.NewContextAsync()` unless you need a second (limited) user — and then still sign in via `AutoNateE2EFixture.SignInAsync(page, username, password)` (issue #93 tracks one bypass).
- Default allowlist is only `"Failed to load resource"` and `"authentication-failed"`; use `session.ConsoleErrors.Allow("substring")` per test for an intentional error path. Do not grow the default list (`ConsoleErrorGuard.cs:27-34`).
- **Selectors: `GetByRole` / `GetByLabel` / `GetByText` first; CSS/attribute locators only when Mantine gives no accessible handle** (e.g. `input[autocomplete='current-password']` because the eye-icon button shares the `Password` label — `AutoNateE2EFixture.cs:96-101`). Mantine inputs have auto-generated ids — never `#username`. Issue #92 tracks the remaining raw locators.
- **Unique names per test**: `TestNames.ShortSlug()`, `TestNames.Prefixed("asset")` → `e2e-asset-3f8c1e29`, `TestNames.ShortCode()` → `E` + 5 hex (satisfies `^[A-Z][A-Z0-9]{1,7}$`) (`Support/TestNames.cs:17-32`). Assert on the unique value, not on a column header or placeholder that repeats.
- **Seed via `ApiSeeder(page.APIRequest)`** when the test's subject is the UI on top of data, not the create flow itself (`Support/ApiSeeder.cs:17-27`; `RecordTypeTests.cs:56-58`).
- Only `admin`/`admin` is seeded. Limited users are minted per test through the API: `adminSeeder.CreateUserAsync(username, password)` — users created after boot get zero grants because the SuperAdmin backfill runs once (`PermissionGatingTests.cs:15-27`, `:240-249`).
- Explicit timeouts on expectations (`Timeout = 10_000`, 15 s for first paint after login). No `Task.Delay` sleeps (issue #89 tracks the one that exists).
- Skipped facts must carry a `Skip = "Blocked: …"` reason describing the product defect (`DocumentEditorTests.cs:39`, `AdminOperationsTests.cs:111`) — and should link an issue (#88).
- Playwright scratch output (`browser_snapshot` files, screenshots) goes under `/temp/`.

---

## 6. Writing tests for new work

**New endpoint group** — three files minimum:
1. `tests/AutoNate.Web.Tests/<Noun>EndpointsTests.cs` — round-trip every verb, 404 on unknown id, 400 shape `{ error }` on validation (§4.1).
2. `tests/AutoNate.Web.Tests/Authorization/<Noun>EnforcementTests.cs` — for each `RequirePermission`/`RequireKindPermission`: `X_NoGrant_Returns403` + `X_With<Action>Grant_Returns200` (§4.3). For `AuthorizedInHandler` routes: no grant → empty list, grant → seeded row visible.
3. Audit assertions — either in the endpoint test or `<Domain>EventPublishingTests.cs`: `Assert.Single(factory.RecordedAuditEvents.Events, e => e.EventType == …)` with `resource`/`details` shape (§3.4).
`AuthorizationGatePresenceTests` will fail automatically if you forget the auth decision on any route.

**New store** — `tests/AutoNate.Web.Tests/EfCore<Noun>StoreTests.cs` using `PostgresTestDatabase.Create<Noun>Store()` (add the factory method to `PostgresTestDatabase.cs`); cover create/trim/validate/not-found/soft-delete; if the store publishes events, inject `RecordingRecordEventPublisher` / a fake and assert the envelope (`.claude/skills/add-record-event-type/SKILL.md` step 5).

**New detector / projection** — construct directly, call `RunOnceAsync` / `ApplyAsync`, assert on the store; assert `IsImmediateScanRequested` for wake paths (§4.5). Projection framework tests: `ProjectionFrameworkTests.cs`, `ProjectionFrameworkPhase*Tests.cs`.

**New SPA page** — there is no SPA unit runner; add `tests/AutoNate.E2E.Tests/<Feature>Tests.cs : E2ETestBase` with: page renders its `PageHeader` title (`GetByRole(AriaRole.Heading, new() { Name = … })`), the primary create flow via the UI, and one `PermissionGatingTests`-style fact if the page has gated affordances. If the page exposes a chatbot provider, verify manually per `.claude/skills/add-page-context-provider/SKILL.md` step 9.

**New plugin surface** — `tests/AutoNate.Web.Tests/Plugins/*` load `test-plugins/SamplePlugin/` (staged by the `StageSamplePluginForTests` target in `AutoNate.Web.Tests.csproj`); extend `tests/AutoNate.Web.Tests.SamplePlugin/SamplePlugin.cs` when a new host hook needs exercising.

---

## 7. Known gaps

Tracked as GitHub issues filed 2026-08-31 by `/n8-audit`; do not re-litigate here, go to the issue:

- #79 — no CI (`area:ci`)
- #80, #81, #82, #83, #90, #91 — untested endpoint groups (Yjs/shared-secret filters, DataStore uploads, dataset preview-file-source, notes/pages writes, document bindings/comments, role-assignment / permission-override)
- #87 — 15 `EntityKinds` with no no-grant→403 enforcement test
- #84, #85, #86, #88, #89, #92, #93 — E2E suite quality (neutralised assertion, blocked journeys, skips without issues, sleep, raw CSS locators, guard bypass)

Fixing any of these means following §4/§5 shapes above; when you do, close the issue from the PR body (`Closes #N`).
