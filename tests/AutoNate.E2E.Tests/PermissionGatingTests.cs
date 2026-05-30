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

        await using var context = await Fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(page, username, password);

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

        await using var limitedContext = await Fixture.NewContextAsync();
        var page = await limitedContext.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(page, username, password);

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

        await using var limitedContext = await Fixture.NewContextAsync();
        var page = await limitedContext.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(page, username, password);

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

        await using var limitedContext = await Fixture.NewContextAsync();
        var page = await limitedContext.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(page, username, password);

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

        await using var limitedContext = await Fixture.NewContextAsync();
        var page = await limitedContext.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(page, username, password);

        await page.GotoAsync("/workflow-executions");

        // After the grant, the SPA's usePermissionChecks resolves
        // canDeleteAll=true and the red Button mounts. Disabled-state is
        // expected when the list is empty (executions.length === 0), but
        // visibility is what proves the gate flipped.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Delete All Executions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
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
}
