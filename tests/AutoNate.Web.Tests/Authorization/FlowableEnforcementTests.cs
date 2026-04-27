using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class FlowableEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task CompleteTask_NonSuperAdminWithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/tasks/some-task-id/complete",
            new { variables = (object?)null });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CompleteTask_AssigneeUserGrant_AllowsWhenTaskAssignedToActor()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        // Seed a stub task assigned to the admin user.
        factory.FlowableStub.TasksByUser[AdminUserId.ToString()] = new List<FlowableTaskSummary>
        {
            new()
            {
                Id = "task-99",
                Assignee = AdminUserId.ToString(),
                ProcessDefinitionId = "lead:1:abc"
            }
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        // Promote the admin via a role grant (admin doesn't have SuperAdmin in this config).
        // Need a SuperAdmin to seed the role; quickest path: temporarily flip the
        // backfill on, but we configured it off. Use the role-store DI instead.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();

            var role = await roleStore.CreateAsync(new CreateRoleInput("TaskCompleter", null), AdminUserId);
            await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.Complete, "/workflowtask/*[assignee=user]", "allow", 0), AdminUserId);
            await assignments.AssignAsync(new CreateRoleAssignmentInput(
                role.Id, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
        }

        var resp = await client.PostAsJsonAsync(
            "/api/tasks/task-99/complete",
            new { variables = (object?)null });

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteExecution_GrantOnProcessKey_Allows()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        factory.FlowableStub.InstancesById["pi-42"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-42",
            ProcessDefinitionId = "lead:1:xyz"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
            var role = await roleStore.CreateAsync(new CreateRoleInput("LeadDeleter", null), AdminUserId);
            await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.Delete, "/workflowexecution/*[processkey=lead]", "allow", 0), AdminUserId);
            await assignments.AssignAsync(new CreateRoleAssignmentInput(
                role.Id, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
        }

        var resp = await client.DeleteAsync("/api/executions/pi-42");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteExecution_GrantOnDifferentProcessKey_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        factory.FlowableStub.InstancesById["pi-77"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-77",
            ProcessDefinitionId = "deal:1:xyz"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();
            var role = await roleStore.CreateAsync(new CreateRoleInput("LeadOnly", null), AdminUserId);
            await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.Delete, "/workflowexecution/*[processkey=lead]", "allow", 0), AdminUserId);
            await assignments.AssignAsync(new CreateRoleAssignmentInput(
                role.Id, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
        }

        var resp = await client.DeleteAsync("/api/executions/pi-77");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteExecution_MultiHopSupervisorGrant_AllowsForSupervisee()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        // Admin (the auto-login user) supervises Alice; the execution we
        // expect to authorize was started by Alice.
        var alice = Guid.NewGuid();
        factory.FlowableStub.InstancesById["pi-mh-allow"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-mh-allow",
            ProcessDefinitionId = "lead:1:abc",
            StartUserId = alice.ToString()
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();

            // Wire admin → alice supervisor edge directly.
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
                {
                    Id = Guid.NewGuid(),
                    EdgeKind = EdgeKinds.Supervisor,
                    FromKind = EntityKinds.User,
                    FromId = AdminUserId.ToString(),
                    ToKind = EntityKinds.User,
                    ToId = alice.ToString(),
                    Data = "{}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = AdminUserId
                });
                await db.SaveChangesAsync();
            }

            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.Delete,
                "/workflowexecution/*[startedby=user[supervisor=user]]",
                "allow", 0), AdminUserId);
        }

        var resp = await client.DeleteAsync("/api/executions/pi-mh-allow");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteExecution_MultiHopSupervisorGrant_DeniesForUnrelatedStarter()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        // Stranger started this execution; admin doesn't supervise them.
        var stranger = Guid.NewGuid();
        factory.FlowableStub.InstancesById["pi-mh-deny"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-mh-deny",
            ProcessDefinitionId = "lead:1:xyz",
            StartUserId = stranger.ToString()
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.Delete,
                "/workflowexecution/*[startedby=user[supervisor=user]]",
                "allow", 0), AdminUserId);
        }

        var resp = await client.DeleteAsync("/api/executions/pi-mh-deny");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
