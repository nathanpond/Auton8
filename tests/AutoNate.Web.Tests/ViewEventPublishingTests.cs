using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ViewEventPublishingTests
{
    private sealed record RecordTypeSnapshot(Guid Id);
    private sealed record RecordSnapshot(Guid Id, string Key);
    private sealed record GroupSnapshot(Guid Id);
    private sealed record RoleSnapshot(Guid Id);

    [Fact]
    public async Task GetRecordById_publishes_record_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        var rt = await CreateRecordType(client);
        var rec = await CreateRecord(client, rt.Id);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync($"/api/records/{rec.Id}")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == RecordEventTypes.Viewed);
    }

    [Fact]
    public async Task GetRecordsList_publishes_record_list_viewed_with_metadata()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        var rt = await CreateRecordType(client);
        await CreateRecord(client, rt.Id);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync($"/api/records/?recordTypeId={rt.Id}")).EnsureSuccessStatusCode();

        var listed = Assert.Single(factory.RecordedAuditEvents.Events,
            e => e.EventType == RecordEventTypes.ListViewed);
        Assert.NotNull(listed.Details);
        // Spot-check that resultCount is in the payload.
        var resultCountProp = listed.Details!.GetType().GetProperty("resultCount");
        Assert.NotNull(resultCountProp);
    }

    [Fact]
    public async Task SearchRecords_publishes_record_searched_with_filterHash()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        var rt = await CreateRecordType(client);
        factory.RecordedAuditEvents.Clear();

        var resp = await client.PostAsJsonAsync(
            "/api/records/search",
            new SearchRecordsRequest(rt.Id, null, null, false, 0, 25, null));
        resp.EnsureSuccessStatusCode();

        var searched = Assert.Single(factory.RecordedAuditEvents.Events,
            e => e.EventType == RecordEventTypes.Searched);
        var filterHashProp = searched.Details!.GetType().GetProperty("filterHash");
        Assert.NotNull(filterHashProp);
        var hash = (string?)filterHashProp!.GetValue(searched.Details);
        Assert.False(string.IsNullOrEmpty(hash));
    }

    [Fact]
    public async Task GetUserList_publishes_user_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.UserListViewed);
    }

    [Fact]
    public async Task GetGroupDetail_publishes_group_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        var grpResp = await client.PostAsJsonAsync(
            "/api/admin/groups/",
            new GroupEndpoints.CreateGroupRequest("Viewers", null));
        var grp = await grpResp.Content.ReadFromJsonAsync<GroupSnapshot>();
        Assert.NotNull(grp);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync($"/api/admin/groups/{grp!.Id}")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.GroupViewed);
    }

    [Fact]
    public async Task GetRoleAssignments_publishes_role_assignments_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        var roleResp = await client.PostAsJsonAsync(
            "/api/admin/roles/",
            new RoleEndpoints.CreateRoleRequest("Viewer", null));
        var role = await roleResp.Content.ReadFromJsonAsync<RoleSnapshot>();
        Assert.NotNull(role);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync($"/api/admin/roles/{role!.Id}/assignments")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.RoleAssignmentsViewed);
    }

    [Fact]
    public async Task GetWorkflowList_publishes_model_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/workflows")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ModelListViewed);
    }

    [Fact]
    public async Task GetExecutionsList_publishes_execution_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/executions")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ExecutionListViewed);
    }

    [Fact]
    public async Task GetMenuList_publishes_menu_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/admin/menus/")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == SiteEventTypes.MenuListViewed);
    }

    [Fact]
    public async Task GetSiteSettings_admin_publishes_settings_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/admin/site-settings/")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == SiteEventTypes.SettingsListViewed);
    }

    [Fact]
    public async Task GetSiteAppearance_anonymous_does_NOT_publish_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        // Don't prime — hit the public endpoint directly. Anonymous reads
        // are explicitly skipped per the audit-events plan.
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/appearance/")).EnsureSuccessStatusCode();

        Assert.DoesNotContain(factory.RecordedAuditEvents.Events,
            e => e.EventType == SiteEventTypes.AppearanceViewed);
    }

    [Fact]
    public async Task GetNotificationsList_publishes_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/notifications/")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == NotificationEventTypes.ListViewed);
    }

    [Fact]
    public async Task GetUnreadCount_repeatedly_coalesces_to_single_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        // Hit the unread-count endpoint 5 times in quick succession.
        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync("/api/notifications/unread-count")).EnsureSuccessStatusCode();
        }

        // The 60s coalesce window means exactly one event is published.
        var unreadEvents = factory.RecordedAuditEvents.Events
            .Where(e => e.EventType == NotificationEventTypes.UnreadCountViewed)
            .ToArray();
        Assert.Single(unreadEvents);
    }

    [Fact]
    public async Task GetPluginsList_publishes_plugin_list_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/admin/plugins/")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == ApplicationEventTypes.PluginListViewed);
    }

    [Fact]
    public async Task GetRegistry_publishes_registry_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.GetAsync("/api/admin/registry/")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == IamEventTypes.RegistryViewed);
    }

    [Fact]
    public async Task EventCatalog_lists_every_phase_4_event_type()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);

        var resp = await client.GetAsync("/api/event-catalog");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();

        // Spot-check each domain's flagship view event is present.
        Assert.Contains(RecordEventTypes.Viewed, body);
        Assert.Contains(RecordEventTypes.Searched, body);
        Assert.Contains(IamEventTypes.UserListViewed, body);
        Assert.Contains(WorkflowAdminEventTypes.ExecutionListViewed, body);
        Assert.Contains(SiteEventTypes.MenuListViewed, body);
        Assert.Contains(NotificationEventTypes.UnreadCountViewed, body);
        Assert.Contains(ApplicationEventTypes.PluginListViewed, body);
    }

    private static async Task PrimeAuth(HttpClient client) =>
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

    private static async Task<RecordTypeSnapshot> CreateRecordType(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest("vw", "View", null, null, null));
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new RecordTypeSnapshot(dto.GetProperty("id").GetGuid());
    }

    private static async Task<RecordSnapshot> CreateRecord(HttpClient client, Guid recordTypeId)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/records/",
            new
            {
                recordTypeId,
                name = "view test",
                values = new { },
                assigneeIds = Array.Empty<Guid>()
            });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new RecordSnapshot(
            dto.GetProperty("id").GetGuid(),
            dto.GetProperty("key").GetString()!);
    }
}
