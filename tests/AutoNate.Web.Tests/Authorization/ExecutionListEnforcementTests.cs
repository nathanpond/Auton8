using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class ExecutionListEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    // Seeds the CACHE, because that is what the list reads as of #108.
    //
    // It used to fill FlowableStub.Executions, and the comment said "the facts on
    // each summary drive the in-memory selector evaluator" -- true then. The list
    // is now a SQL query with authorization pushed into it, so the same summaries
    // go through the real projection and the facts are the cache row's columns.
    // What these tests assert is unchanged: which runs a grant admits.
    private static async Task SeedExecutionsAsync(AutoNateWebApplicationFactory factory)
    {
        var stranger = Guid.NewGuid().ToString();
        var summaries = new[]
        {
            new WorkflowExecutionSummary
            {
                Id = "pi-admin-1",
                ProcessDefinitionId = "lead:1:abc",
                StartUserId = AdminUserId.ToString(),
                Status = "Running"
            },
            new WorkflowExecutionSummary
            {
                Id = "pi-admin-2",
                ProcessDefinitionId = "deal:1:xyz",
                StartUserId = AdminUserId.ToString(),
                Status = "Complete"
            },
            new WorkflowExecutionSummary
            {
                Id = "pi-other",
                ProcessDefinitionId = "lead:1:abc",
                StartUserId = stranger,
                Status = "Running"
            }
        };

        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider
            .GetRequiredService<AutoNate.Web.Services.Flowable.Cache.FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            summaries.Select(x => new AutoNate.Web.Services.Projections.ChangeEvent<WorkflowExecutionSummary>(
                AutoNate.Web.Services.Projections.ChangeOp.Upsert, x.Id, x, DateTimeOffset.UtcNow)).ToArray(),
            db, CancellationToken.None);
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
    public async Task ListExecutions_NoGrant_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedExecutionsAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetFromJsonAsync<WorkflowExecutionSummary[]>("/api/executions/");

        Assert.NotNull(resp);
        Assert.Empty(resp);
    }

    [Fact]
    public async Task ListExecutions_FullKindGrant_ReturnsAll()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedExecutionsAsync(factory);
        await GrantAsync(factory, Actions.View, "/workflowexecution/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetFromJsonAsync<WorkflowExecutionSummary[]>("/api/executions/");

        Assert.NotNull(resp);
        Assert.Equal(3, resp.Length);
    }

    [Fact]
    public async Task ListExecutions_StartedByUserGrant_FiltersToOwnRuns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedExecutionsAsync(factory);
        await GrantAsync(factory, Actions.View, "/workflowexecution/*[startedby=user]");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetFromJsonAsync<WorkflowExecutionSummary[]>("/api/executions/");

        Assert.NotNull(resp);
        Assert.Equal(2, resp.Length);
        Assert.All(resp, e => Assert.Equal(AdminUserId.ToString(), e.StartUserId));
    }

    [Fact]
    public async Task ListExecutionsPage_NoGrant_ReturnsEmptyItems()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedExecutionsAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetFromJsonAsync<PagedExecutions>("/api/executions/page");

        Assert.NotNull(resp);
        Assert.NotNull(resp.Items);
        Assert.Empty(resp.Items);
        Assert.Equal(0, resp.TotalCount);
    }

    [Fact]
    public async Task ListExecutionsPage_StartedByUserGrant_FiltersToOwnRuns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedExecutionsAsync(factory);
        await GrantAsync(factory, Actions.View, "/workflowexecution/*[startedby=user]");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetFromJsonAsync<PagedExecutions>("/api/executions/page");

        Assert.NotNull(resp);
        Assert.Equal(2, resp.TotalCount);
        Assert.All(resp.Items!, e => Assert.Equal(AdminUserId.ToString(), e.StartUserId));
    }

    private sealed record PagedExecutions(WorkflowExecutionSummary[]? Items, int TotalCount);
}
