using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class WorkflowOverrideEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task UpdateVariables_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-vars"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-vars",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PutAsJsonAsync(
            "/api/executions/pi-vars/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "x", Value = 1, Type = "integer" }
            }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AddVariables_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-add"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-add",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-add/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "newX", Value = 1, Type = "integer" }
            }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AddVariables_WithOverrideGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-add-ok"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-add-ok",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "VariableAdder",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-add-ok/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "newY", Value = 2, Type = "integer" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains("AddVariables:pi-add-ok", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task ForceCompleteTask_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-fc"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-fc",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-fc/tasks/task-99/force-complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateVariables_WithOverrideGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-ok"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-ok",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "ExecutionOverrider",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PutAsJsonAsync(
            "/api/executions/pi-ok/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "x", Value = 1, Type = "integer" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains("UpdateVariables:pi-ok", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task ForceCompleteTask_WithOverrideGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-ok2"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-ok2",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "ForceCompleter",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-ok2/tasks/task-7/force-complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains("CompleteTask:task-7", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task ReassignTask_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-rs"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-rs",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-rs/tasks/task-99/reassign",
            new ExecutionEndpoints.ReassignTaskRequest("alice"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ReassignTask_WithOverrideGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-rs-ok"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-rs-ok",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "Reassigner",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-rs-ok/tasks/task-7/reassign",
            new ExecutionEndpoints.ReassignTaskRequest("alice"));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains("UpdateTaskAssignee:task-7:alice", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task UpdateTaskDueDate_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-dd"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-dd",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-dd/tasks/task-99/due-date",
            new ExecutionEndpoints.UpdateTaskDueDateRequest(DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTaskDueDate_WithOverrideGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-dd-ok"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-dd-ok",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "DueDateSetter",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-dd-ok/tasks/task-7/due-date",
            new ExecutionEndpoints.UpdateTaskDueDateRequest(
                new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero)));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains(factory.FlowableStub.Calls, c => c.StartsWith("UpdateTaskDueDate:task-7:"));
    }

    [Fact]
    public async Task MoveExecutionState_WithoutGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-mv"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-mv",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-mv/move-state",
            new ExecutionEndpoints.MoveExecutionStateRequest("userTask_review"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MoveExecutionState_WithOverrideGrantOnly_StillReturns403()
    {
        // Sanity check: movestate is intentionally separate from override so
        // admins can grant the lighter overrides without unlocking state moves.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-mv-ovr"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-mv-ovr",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "OverrideOnly",
            "/workflowexecution/*", Actions.Override);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-mv-ovr/move-state",
            new ExecutionEndpoints.MoveExecutionStateRequest("userTask_review"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MoveExecutionState_WithMoveStateGrant_Returns204()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-mv-ok"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-mv-ok",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "ExecutionMover",
            "/workflowexecution/*", Actions.MoveState);

        var resp = await client.PostAsJsonAsync(
            "/api/executions/pi-mv-ok/move-state",
            new ExecutionEndpoints.MoveExecutionStateRequest("userTask_review"));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Contains("MoveExecutionState:pi-mv-ok:userTask_review", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task UpdateVariables_GrantOnProcessKey_AllowsForMatchingProcess()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-lead"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-lead",
            ProcessDefinitionId = "lead:1:abc"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedRoleAndGrantAsync(factory, "LeadOverrider",
            "/workflowexecution/*[processkey=lead]", Actions.Override);

        var resp = await client.PutAsJsonAsync(
            "/api/executions/pi-lead/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "x", Value = 1 }
            }));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateVariables_GrantOnDifferentProcessKey_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfigNoBackfill());
        factory.FlowableStub.InstancesById["pi-deal"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-deal",
            ProcessDefinitionId = "deal:1:xyz"
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        // Grant only matches lead, but the instance is a deal.
        await SeedRoleAndGrantAsync(factory, "LeadOnlyOverrider",
            "/workflowexecution/*[processkey=lead]", Actions.Override);

        var resp = await client.PutAsJsonAsync(
            "/api/executions/pi-deal/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "x", Value = 1 }
            }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private static async Task SeedRoleAndGrantAsync(
        AutoNateWebApplicationFactory factory,
        string roleName,
        string selector,
        string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var assignments = scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>();

        var role = await roleStore.CreateAsync(new CreateRoleInput(roleName, null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.Role, role.Id.ToString(), action, selector, "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
    }
}
