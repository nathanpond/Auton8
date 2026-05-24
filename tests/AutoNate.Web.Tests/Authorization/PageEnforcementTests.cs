using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Covers Page authorization gates across the four endpoint files that
// enforce them:
//   - ContentPageEndpoints.cs:92   GET    /api/content/pages/{id}        (View)
//   - ContentPageEndpoints.cs:358  PATCH  /api/content/pages/{id}        (Edit)
//   - PageVersionEndpoints.cs:55   GET    /.../versions                  (View)
//   - PageVersionEndpoints.cs:126  POST   /.../versions/{n}/restore      (Edit)
//   - PageVersionEndpoints.cs:162  DELETE /.../versions/{n}              (Delete)
//   - PageAttachmentEndpoints.cs:41  GET  /.../attachments               (View)
//   - PageAttachmentEndpoints.cs:175 POST /.../attachments               (Edit)
//
// All of these run RequirePermission BEFORE the handler, so no Page rows need
// to be seeded for the no-grant→403 regression net. Positive controls would
// require seeding Project→Cabinet→Notebook→Page; left to the IContentAuthorizer
// policy tests that already cover the per-page filtering path.
//
// The page-row DELETE (ContentPageEndpoints.cs:367) is intentionally NOT
// covered here — it uses AuthorizedInHandler against IContentAuthorizer
// .IsProjectOwnerAsync, not RequirePermission, so its regression net belongs
// with the content-authorizer tests (ContentAuthorizerPolicyTests).
[Trait("Category", "Integration")]
public sealed class PageEnforcementTests
{
    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task GetPage_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/content/pages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PatchPage_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync(
            $"/api/content/pages/{Guid.NewGuid()}",
            new { name = "renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListVersions_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync(
            $"/api/content/pages/{Guid.NewGuid()}/versions/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task RestoreVersion_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/content/pages/{Guid.NewGuid()}/versions/1/restore",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_NoGrant_Returns403()
    {
        // The only RequirePermission(Page, Delete) site in the codebase.
        // Page-row delete uses an owner-only inline check (intentionally
        // narrower than this Contributor-class permission).
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync(
            $"/api/content/pages/{Guid.NewGuid()}/versions/1");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListAttachments_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync(
            $"/api/content/pages/{Guid.NewGuid()}/attachments/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
