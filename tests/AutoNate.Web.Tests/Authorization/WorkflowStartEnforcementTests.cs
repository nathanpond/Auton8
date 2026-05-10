using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class WorkflowStartEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task SeedWorkflowModelAsync(
        AutoNateWebApplicationFactory factory, string processKey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        db.WorkflowModels.Add(new WorkflowModelEntity
        {
            Id = Guid.NewGuid(),
            Name = processKey,
            ProcessKey = processKey,
            BpmnXml = "<definitions/>",
            IsDraft = false,
            DraftVersionNumber = 1,
            PublishedVersionNumber = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
    }

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
    public async Task Start_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedWorkflowModelAsync(factory, "lead");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/workflows/lead/start", new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Start_FullKindGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedWorkflowModelAsync(factory, "lead");
        await GrantAsync(factory, Actions.Start, "/workflowmodel/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/workflows/lead/start", new { });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // Note: the WorkflowModel registry declares a `processkey` tag
    // (CoreEntityTypes.cs) but the production wiring registers a
    // PathOnlySelectorCompiler (Program.cs), so `[processkey=...]` predicate
    // selectors aren't currently compiled into the filter. Adding a
    // tag-aware compiler is a separate piece of work; this fix only closes
    // the GUID-vs-processKey mismatch that made wildcard-kind grants
    // misbehave on the start endpoint.
}
