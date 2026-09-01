using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 10 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — the multi-user
/// permission-gating layer. Per the plan, this complements (does not
/// duplicate) the API-level enforcement covered exhaustively in
/// <c>AutoNate.Web.Tests/Authorization/</c> by proving the SPA renders its
/// affordance gates correctly when a limited user signs in.
///
/// **Why a fresh user is actually limited:** the
/// <c>superadmin_backfill_v1</c> migration in
/// <c>DatabaseSchemaInitializer</c> runs <em>once</em> at app boot, gated by
/// an <c>auth_seed_state</c> row that records its completion. It hands
/// SuperAdmin to every user present at that moment (the seeded <c>admin</c>),
/// then never runs again. A user created <em>after</em> boot via
/// <c>POST /api/users/</c> bypasses the backfill entirely and starts with
/// zero grants. That's what these tests rely on.
///
/// Each test mints its own fresh user (unique username) and its own admin
/// + limited browser contexts so they stay order-independent on the shared
/// fixture.
/// </summary>
public sealed class PermissionGatingTests : E2ETestBase
{
    public PermissionGatingTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task LimitedUser_LoginSucceeds_WithNoGrants()
    {
        var (_, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        // SignInAsync waits for the post-login URL to settle at /home or
        // /?error=. Confirm it was /home, not the error branch.
        Assert.Matches("/home", page.Url);

        // And /api/auth/me agrees — proves the cookie was issued.
        var response = await page.APIRequest.GetAsync("/api/auth/me");
        var json = await response.JsonAsync();
        Assert.True(json!.Value.GetProperty("authenticated").GetBoolean());
        Assert.Equal(username, json.Value.GetProperty("username").GetString());
    }

    [Fact]
    public async Task LimitedUser_RecordTypesList_RendersEmpty_WithoutGrants()
    {
        // Admin seeds a record type so there's *something* the visibility
        // filter has to hide from the limited user — without this we couldn't
        // distinguish "filter works" from "nothing exists".
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);
        var seededType = await adminSeeder.CreateRecordTypeAsync(
            TestNames.ShortCode(), TestNames.Prefixed("gated"));

        var (_, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/record-types");

        // GET /api/record-types/ is `AuthorizedInHandler("filters via
        // FilterQueryAsync(RecordType, View); empty grants -> empty list")`
        // — so a no-grants limited user sees an empty list, not a 403.
        // RecordTypeList renders the canonical empty-state copy from
        // DataTable's `emptyMessage` prop ("No record types yet…").
        await Assertions.Expect(page.GetByText("No record types yet").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The admin's seeded type must NOT be visible — proves the filter
        // is actually doing work rather than the page being empty by chance.
        await Assertions.Expect(page.GetByText(seededType.ShortCode))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AfterGrantingRecordTypeView_LimitedUser_SeesSeededTypes()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);
        var seededType = await adminSeeder.CreateRecordTypeAsync(
            TestNames.ShortCode(), TestNames.Prefixed("granted"));

        var (userId, username, password) = await MintLimitedUserAsync();

        // Kind-level `recordtype:view` grant on `/recordtype/*` — any record
        // type the filter walks is now visible to this principal.
        await adminSeeder.GrantAsync(
            principalKind: "user",
            principalId: userId,
            action: "view",
            selectorString: "/recordtype/*");

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/record-types");

        // With view granted, the admin-seeded type's short code appears in
        // the list. (Cross-checked via direct API: the diagnostic confirmed
        // GET /api/record-types/ returns the seeded type for the granted
        // user, and the empty-state copy "No record types yet" remains in
        // the DOM via mantine-datatable's emptyMessage slot even when rows
        // exist — so a Not.ToBeVisibleAsync on it would race the table
        // populate. The positive ShortCode visibility check is the reliable
        // proof.)
        await Assertions.Expect(page.GetByText(seededType.ShortCode).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task LimitedUser_WorkflowExecutions_DeleteAllExecutionsButton_Absent()
    {
        var (_, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");

        // The page heading mounts for any authenticated user — the route
        // itself isn't gated by permission, only the contents are.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Executions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The "Delete All Executions" red Button is conditionally rendered
        // behind `canDeleteAll` (WorkflowExecutions.tsx:92-93,408), which
        // resolves from a `workflowexecution:deleteall` kind-level
        // permission check. A user with no grants has `canDeleteAll=false`
        // and the button is omitted from the DOM entirely.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Delete All Executions" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AfterGrantingDeleteAll_LimitedUser_SeesDeleteAllExecutionsButton()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);

        var (userId, username, password) = await MintLimitedUserAsync();

        // Kind-level `workflowexecution:deleteall` — the SPA's permission
        // check passes `id="*"`, which compiles to a /workflowexecution/*
        // selector match in the authorization layer. (See
        // RequireKindPermissionFilter — Backend gate uses id="*" so the SPA
        // mirrors it; per WorkflowExecutions.tsx:85-90.)
        await adminSeeder.GrantAsync(
            principalKind: "user",
            principalId: userId,
            action: "deleteall",
            selectorString: "/workflowexecution/*");

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");

        // After the grant, the SPA's usePermissionChecks resolves
        // canDeleteAll=true and the red Button mounts. Disabled-state is
        // expected when the list is empty (executions.length === 0), but
        // visibility is what proves the gate flipped.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Delete All Executions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // E2E-044. Site Configuration used to render its whole shell for anyone
    // signed in: a user with no grants could deep-link to /admin/config and
    // get the nav, headings and empty tables while every API call behind them
    // returned 403. The backend held, so this was an affordance defect — the
    // page looked broken rather than forbidden.
    [Fact]
    public async Task LimitedUser_DeepLinkingIntoAdminConfig_SeesNoAccessNotTheShell()
    {
        var (_, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/admin/config/general");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "You don't have access to this area" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The shell itself must not mount. "Site Configuration" is the
        // ConfigLayout heading, so its absence is what proves the guard ran
        // before the admin surface rendered rather than alongside it.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Site Configuration" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AfterGrantingSiteConfigView_LimitedUser_ReachesAdminConfig()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);

        var (userId, username, password) = await MintLimitedUserAsync();

        // Same (kind, action) the config endpoints declare server-side, so the
        // guard cannot drift from what the API actually enforces.
        await adminSeeder.GrantAsync(
            principalKind: "user",
            principalId: userId,
            action: "view",
            selectorString: "/siteconfig/*");

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;

        await page.GotoAsync("/admin/config/general");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "General", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "You don't have access to this area" }))
            .Not.ToBeVisibleAsync();
    }

    // E2E-045. RecordDetail rendered its delete action unconditionally, so a
    // user without record:delete was offered a button whose every click ended
    // in a 403 — indistinguishable, from the user's side, from a broken app.
    [Fact]
    public async Task LimitedUser_RecordDetail_DeleteActionAbsentUntilGranted()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);

        var recordType = await adminSeeder.CreateRecordTypeAsync(
            TestNames.ShortCode(), TestNames.Prefixed("gated-type"));
        var record = await adminSeeder.CreateRecordAsync(recordType.Id, TestNames.Prefixed("gated"));

        var (userId, username, password) = await MintLimitedUserAsync();
        // View only — enough to open the record, not to delete it.
        await adminSeeder.GrantAsync(
            principalKind: "user", principalId: userId,
            action: "view", selectorString: "/recordtype/*");
        await adminSeeder.GrantAsync(
            principalKind: "user", principalId: userId,
            action: "view", selectorString: "/record/*");

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;
        await page.GotoAsync($"/record/{record.Key}");

        await Assertions.Expect(page.GetByText(record.Key).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Delete" }))
            .Not.ToBeVisibleAsync();

        // Granting delete makes the affordance appear, which is what proves
        // the absence above was the permission check and not a mis-locator.
        await adminSeeder.GrantAsync(
            principalKind: "user", principalId: userId,
            action: "delete", selectorString: "/record/*");

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Delete" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task LimitedUser_DocumentOverrideGrantAndRevoke_ChangesVisibility()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var admin = adminSession.Page.APIRequest;
        var seeder = new ApiSeeder(admin);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("gated-doc-project"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("gated-doc"));
        var (userId, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;
        Assert.DoesNotContain(document.Title, await ListProjectDocumentTitlesAsync(page.APIRequest, project.Id));

        var grant = await admin.PostAsync($"/api/content/documents/{document.Id}/permissions", new()
        {
            DataObject = new { principalKind = "user", principalId = userId.ToString(), action = "view" }
        });
        Assert.True(grant.Ok, await grant.TextAsync());
        var grantJson = await grant.JsonAsync();
        var grantId = grantJson!.Value.GetProperty("id").GetGuid();
        Assert.Contains(document.Title, await ListProjectDocumentTitlesAsync(page.APIRequest, project.Id));

        var revoke = await admin.DeleteAsync($"/api/content/documents/{document.Id}/permissions/{grantId}");
        Assert.True(revoke.Ok, await revoke.TextAsync());
        Assert.DoesNotContain(document.Title, await ListProjectDocumentTitlesAsync(page.APIRequest, project.Id));
    }

    [Fact]
    public async Task LimitedUser_ProjectMembershipRoleAndRemoval_ChangesVisibility()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var admin = adminSession.Page.APIRequest;
        var project = await new ApiSeeder(admin).CreateProjectAsync(TestNames.Prefixed("member-project"));
        var (userId, username, password) = await MintLimitedUserAsync();

        await using var session = await NewSignedInAsAsync(username, password);
        var page = session.Page;
        Assert.DoesNotContain(project.Name, await ListProjectNamesAsync(page.APIRequest));

        await PutProjectRoleAsync(admin, project.Id, userId, "viewer");
        Assert.Contains(project.Name, await ListProjectNamesAsync(page.APIRequest));
        await PutProjectRoleAsync(admin, project.Id, userId, "contributor");
        var members = await page.APIRequest.GetAsync($"/api/content/projects/{project.Id}/members");
        Assert.True(members.Ok, await members.TextAsync());
        Assert.Contains("contributor", await members.TextAsync());

        var remove = await admin.DeleteAsync($"/api/content/projects/{project.Id}/members/{userId}");
        Assert.True(remove.Ok, await remove.TextAsync());
        Assert.DoesNotContain(project.Name, await ListProjectNamesAsync(page.APIRequest));
    }

    /// <summary>
    /// Creates a limited user via the admin's request context and returns the
    /// new principal id + the username/password the test will use to sign in.
    /// </summary>
    private async Task<(Guid UserId, string Username, string Password)> MintLimitedUserAsync()
    {
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);

        var username = $"e2e_limited_{TestNames.ShortSlug()}";
        const string password = "P@ssword123!";
        var user = await adminSeeder.CreateUserAsync(username, password);
        return (user.UserId, username, password);
    }

    private static async Task<IReadOnlyList<string>> ListProjectDocumentTitlesAsync(
        IAPIRequestContext request, Guid projectId)
    {
        var response = await request.GetAsync(
            $"/api/content/documents/page?projectId={projectId}&atProjectRoot=true");
        Assert.True(response.Ok, await response.TextAsync());
        var json = await response.JsonAsync();
        return json!.Value.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("title").GetString()!)
            .ToList();
    }

    private static async Task PutProjectRoleAsync(
        IAPIRequestContext request, Guid projectId, Guid userId, string role)
    {
        var response = await request.PutAsync($"/api/content/projects/{projectId}/members/{userId}", new()
        {
            DataObject = new { role }
        });
        Assert.True(response.Ok, await response.TextAsync());
    }

    private static async Task<IReadOnlyList<string>> ListProjectNamesAsync(IAPIRequestContext request)
    {
        var response = await request.GetAsync("/api/content/projects/page");
        Assert.True(response.Ok, await response.TextAsync());
        var json = await response.JsonAsync();
        return json!.Value.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .ToList();
    }
}
