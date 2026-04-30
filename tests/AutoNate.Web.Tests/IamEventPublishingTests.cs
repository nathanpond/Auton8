using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Authorization;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class IamEventPublishingTests
{
    private sealed record UserDto(long Id, Guid UserId, string Username);
    private sealed record GroupDto(Guid Id, string Name);
    private sealed record RoleDto(Guid Id, string Name);
    private sealed record GrantDto(Guid Id);

    [Fact]
    public async Task PostUsers_publishes_user_created()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest(
                Username: "alice",
                FirstName: "A",
                LastName: "L",
                Password: "p@ssword123",
                Email: "alice@example.com"));
        response.EnsureSuccessStatusCode();

        var created = Assert.Single(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.UserCreated);
        Assert.Equal(IamEventTopic.TopicName, created.Topic);
        Assert.Equal(IamResourceKinds.User, created.ResourceKind);
    }

    [Fact]
    public async Task PutUser_publishes_user_updated()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest("bob", "B", "B", "p@ssword123", "bob@x.com"));
        create.EnsureSuccessStatusCode();
        var bob = await create.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(bob);
        factory.RecordedAuditEvents.Clear();

        var response = await client.PutAsJsonAsync(
            $"/api/users/{bob!.Id}",
            new UserEndpoints.UpdateUserRequest("bob", "Bobby", "B", "bob@x.com"));
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.UserUpdated);
    }

    [Fact]
    public async Task PostUserPassword_publishes_user_password_reset()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest("carol", "C", "C", "p@ssword123", "c@x.com"));
        var carol = await create.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(carol);
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync(
            $"/api/users/{carol!.Id}/password",
            new UserEndpoints.ResetPasswordRequest("newpassword123"));
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.UserPasswordReset);
    }

    [Fact]
    public async Task DeleteUser_publishes_user_deleted_only_on_success()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();
        var create = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest("dave", "D", "D", "p@ssword123", "d@x.com"));
        var dave = await create.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(dave);
        factory.RecordedAuditEvents.Clear();

        var notFound = await client.DeleteAsync($"/api/users/9999999");
        var ok = await client.DeleteAsync($"/api/users/{dave!.Id}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, notFound.StatusCode);
        ok.EnsureSuccessStatusCode();

        var deletes = factory.RecordedAuditEvents.Events
            .Where(e => e.EventType == IamEventTypes.UserDeleted)
            .ToArray();
        Assert.Single(deletes);
    }

    [Fact]
    public async Task PostGroups_publishes_group_created()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/admin/groups")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync(
            "/api/admin/groups",
            new GroupEndpoints.CreateGroupRequest("Engineering", "All engineers"));
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.GroupCreated);
    }

    [Fact]
    public async Task GroupArchiveRestoreDelete_publish_distinct_events()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/admin/groups")).EnsureSuccessStatusCode();
        var create = await client.PostAsJsonAsync(
            "/api/admin/groups",
            new GroupEndpoints.CreateGroupRequest("QA", null));
        var grp = await create.Content.ReadFromJsonAsync<GroupDto>();
        Assert.NotNull(grp);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync($"/api/admin/groups/{grp!.Id}/archive", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/admin/groups/{grp.Id}/restore", null)).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/admin/groups/{grp.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(IamEventTypes.GroupArchived, types);
        Assert.Contains(IamEventTypes.GroupRestored, types);
        Assert.Contains(IamEventTypes.GroupDeleted, types);
    }

    [Fact]
    public async Task GroupMembers_publish_added_then_removed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/admin/groups")).EnsureSuccessStatusCode();
        var grpResp = await client.PostAsJsonAsync(
            "/api/admin/groups",
            new GroupEndpoints.CreateGroupRequest("Members", null));
        var grp = await grpResp.Content.ReadFromJsonAsync<GroupDto>();
        Assert.NotNull(grp);

        var userResp = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest("eve", "E", "E", "p@ssword123", "e@x.com"));
        userResp.EnsureSuccessStatusCode();
        var eve = await userResp.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(eve);
        factory.RecordedAuditEvents.Clear();

        var add = await client.PostAsJsonAsync(
            $"/api/admin/groups/{grp!.Id}/members",
            new GroupEndpoints.AddMemberRequest(eve!.UserId));
        add.EnsureSuccessStatusCode();
        var remove = await client.DeleteAsync($"/api/admin/groups/{grp.Id}/members/{eve.UserId}");
        remove.EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(IamEventTypes.GroupMemberAdded, types);
        Assert.Contains(IamEventTypes.GroupMemberRemoved, types);
    }

    [Fact]
    public async Task RoleLifecycle_publishes_created_updated_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/admin/roles")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            "/api/admin/roles",
            new RoleEndpoints.CreateRoleRequest("Auditor", "Read-only"));
        create.EnsureSuccessStatusCode();
        var role = await create.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);

        (await client.PatchAsJsonAsync(
            $"/api/admin/roles/{role!.Id}",
            new RoleEndpoints.UpdateRoleRequest("Auditor v2", null))).EnsureSuccessStatusCode();

        (await client.DeleteAsync($"/api/admin/roles/{role.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(IamEventTypes.RoleCreated, types);
        Assert.Contains(IamEventTypes.RoleUpdated, types);
        Assert.Contains(IamEventTypes.RoleDeleted, types);
    }

    [Fact]
    public async Task PermissionGrantLifecycle_publishes_created_and_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/admin/grants")).EnsureSuccessStatusCode();
        var roleResp = await client.PostAsJsonAsync(
            "/api/admin/roles",
            new RoleEndpoints.CreateRoleRequest("Reviewer", null));
        var role = await roleResp.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            "/api/admin/grants",
            new PermissionGrantEndpoints.CreateGrantRequest(
                PrincipalKind: "role",
                PrincipalId: role!.Id.ToString(),
                Action: "view",
                SelectorString: "/record/*",
                Effect: "allow",
                Priority: 0));
        create.EnsureSuccessStatusCode();
        var grant = await create.Content.ReadFromJsonAsync<GrantDto>();
        Assert.NotNull(grant);

        var del = await client.DeleteAsync($"/api/admin/grants/{grant!.Id}");
        del.EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(IamEventTypes.PermissionGrantCreated, types);
        Assert.Contains(IamEventTypes.PermissionGrantDeleted, types);
    }
}
