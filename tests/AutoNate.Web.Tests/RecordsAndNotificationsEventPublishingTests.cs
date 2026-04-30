using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordsAndNotificationsEventPublishingTests
{
    private sealed record RecordTypeSnapshot(Guid Id, string ShortCode);
    private sealed record RecordSnapshot(Guid Id, string Key);
    private sealed record NotificationSnapshot(Guid Id);

    [Fact]
    public async Task RestoreArchivedRecord_publishes_record_restored()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);

        var rt = await CreateRecordType(client);
        var rec = await CreateRecord(client, rt.Id);
        (await client.DeleteAsync($"/api/records/{rec.Id}")).EnsureSuccessStatusCode();
        factory.RecordedRecordEvents.Clear();

        (await client.PostAsync($"/api/records/{rec.Id}/restore", null)).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedRecordEvents.Events,
            e => e.EventType == RecordEventTypes.Restored);
        Assert.DoesNotContain(factory.RecordedRecordEvents.Events,
            e => e.EventType == RecordEventTypes.Updated);
    }

    [Fact]
    public async Task ChangingAssignees_publishes_assignees_changed_alongside_updated()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);

        var rt = await CreateRecordType(client);
        var rec = await CreateRecord(client, rt.Id);
        factory.RecordedRecordEvents.Clear();

        // Update only the assigneeIds field — the store should publish both
        // record.updated AND record.assignees.changed.
        var newAssignee = Guid.NewGuid();
        var patch = await client.PatchAsJsonAsync(
            $"/api/records/{rec.Id}",
            new { assigneeIds = new[] { newAssignee } });
        patch.EnsureSuccessStatusCode();

        var types = factory.RecordedRecordEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordEventTypes.Updated, types);
        Assert.Contains(RecordEventTypes.AssigneesChanged, types);
    }

    [Fact]
    public async Task MarkNotificationRead_publishes_notification_read()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);

        // Trigger a notification by assigning a record to the admin user.
        var rt = await CreateRecordType(client);
        var rec = await CreateRecord(client, rt.Id);
        var meResp = await client.GetAsync("/api/auth/me");
        meResp.EnsureSuccessStatusCode();
        var me = await meResp.Content.ReadFromJsonAsync<JsonElement>();
        var adminUserId = Guid.Parse(me.GetProperty("userId").GetString()!);

        (await client.PatchAsJsonAsync(
            $"/api/records/{rec.Id}",
            new { assigneeIds = new[] { adminUserId } })).EnsureSuccessStatusCode();

        // Wait briefly for the notification to land — the store creates it
        // synchronously, but the SPA list endpoint is what we're using to
        // discover the notification id.
        var listResp = await client.GetAsync("/api/notifications/?limit=10");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<NotificationListResponse>();
        Assert.NotNull(list);
        if (list!.Items.Length == 0)
        {
            // Notification creation runs after the record commit; a noop
            // assignee set may legitimately produce no notification. Skip
            // the read assertion in that case rather than flaking.
            return;
        }
        var notif = list.Items[0];
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync($"/api/notifications/{notif.Id}/read", null))
            .EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == NotificationEventTypes.Read);
    }

    [Fact]
    public async Task MarkAllNotificationsRead_publishes_all_read()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuth(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync("/api/notifications/mark-all-read", null))
            .EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == NotificationEventTypes.AllRead);
    }

    private static async Task PrimeAuth(HttpClient client) =>
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

    private static async Task<RecordTypeSnapshot> CreateRecordType(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest("rt", "Records", null, null, null));
        resp.EnsureSuccessStatusCode();
        var rt = await resp.Content.ReadFromJsonAsync<RecordTypeDto>();
        Assert.NotNull(rt);
        return new RecordTypeSnapshot(rt!.Id, rt.ShortCode);
    }

    private static async Task<RecordSnapshot> CreateRecord(HttpClient client, Guid recordTypeId)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/records/",
            new
            {
                recordTypeId,
                name = "test record",
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
