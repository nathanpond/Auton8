using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordSchemaEventPublishingTests
{
    [Fact]
    public async Task RecordType_lifecycle_publishes_created_updated_archived_restored()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest("task", "Task", null, null, null));
        create.EnsureSuccessStatusCode();
        var rt = (await create.Content.ReadFromJsonAsync<RecordTypeDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/record-types/{rt.Id}",
            new UpdateRecordTypeRequest("Task v2", null, null, null))).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/record-types/{rt.Id}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/record-types/{rt.Id}/restore", null)).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordSchemaEventTypes.RecordTypeCreated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeUpdated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeArchived, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeRestored, types);
    }

    [Fact]
    public async Task RecordTypeField_lifecycle_publishes_created_updated_archived_restored()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        var rt = await CreateRecordType(client, "tsk", "Task");
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            $"/api/record-types/{rt.Id}/fields",
            new CreateFieldRequest("priority", "Priority", "text",
                JsonDocument.Parse("{}").RootElement.Clone(), false, 0));
        create.EnsureSuccessStatusCode();
        var field = (await create.Content.ReadFromJsonAsync<RecordTypeFieldDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/record-types/{rt.Id}/fields/{field.Id}",
            new UpdateFieldRequest("Priority v2",
                JsonDocument.Parse("{}").RootElement.Clone(), false, 1))).EnsureSuccessStatusCode();
        (await client.DeleteAsync(
            $"/api/record-types/{rt.Id}/fields/{field.Id}")).EnsureSuccessStatusCode();
        (await client.PostAsync(
            $"/api/record-types/{rt.Id}/fields/{field.Id}/restore", null)).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordSchemaEventTypes.RecordTypeFieldCreated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeFieldUpdated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeFieldArchived, types);
        Assert.Contains(RecordSchemaEventTypes.RecordTypeFieldRestored, types);
    }

    [Fact]
    public async Task RecordEdgeType_lifecycle_publishes_each_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            "/api/record-edge-types/",
            new CreateEdgeTypeRequest("link", "Link", null, true, false, "many_to_many", null, null));
        create.EnsureSuccessStatusCode();
        var et = (await create.Content.ReadFromJsonAsync<EdgeTypeDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/record-edge-types/{et.Id}",
            new UpdateEdgeTypeRequest("Link v2", null, true, false, "many_to_many", null, null))).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/record-edge-types/{et.Id}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/record-edge-types/{et.Id}/restore", null)).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeCreated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeUpdated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeArchived, types);
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeRestored, types);
    }

    [Fact]
    public async Task RecordEdgeTypeField_lifecycle_publishes_each_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        var et = await CreateEdgeType(client);
        factory.RecordedAuditEvents.Clear();

        var create = await client.PostAsJsonAsync(
            $"/api/record-edge-types/{et.Id}/fields",
            new CreateEdgeFieldRequest("note", "Note", "text",
                JsonDocument.Parse("{}").RootElement.Clone(), false, 0));
        create.EnsureSuccessStatusCode();
        var field = (await create.Content.ReadFromJsonAsync<EdgeTypeFieldDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/record-edge-types/{et.Id}/fields/{field.Id}",
            new UpdateEdgeFieldRequest("Note v2",
                JsonDocument.Parse("{}").RootElement.Clone(), false, 1))).EnsureSuccessStatusCode();
        (await client.DeleteAsync(
            $"/api/record-edge-types/{et.Id}/fields/{field.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeFieldCreated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeFieldUpdated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordEdgeTypeFieldDeleted, types);
    }

    [Fact]
    public async Task RecordComment_lifecycle_publishes_created_edited_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);

        // Need a record to attach comments to.
        var rt = await CreateRecordType(client, "tsk", "Task");
        var recordCreate = await client.PostAsJsonAsync(
            "/api/records/",
            new
            {
                recordTypeId = rt.Id,
                name = "test record",
                values = new { },
                assigneeIds = Array.Empty<Guid>()
            });
        recordCreate.EnsureSuccessStatusCode();
        var rec = (await recordCreate.Content.ReadFromJsonAsync<RecordSnapshot>())!;
        factory.RecordedAuditEvents.Clear();

        var commentCreate = await client.PostAsJsonAsync(
            $"/api/records/{rec.Id}/comments/",
            new { body = "first comment" });
        commentCreate.EnsureSuccessStatusCode();
        var comment = (await commentCreate.Content.ReadFromJsonAsync<CommentSnapshot>())!;

        (await client.PatchAsJsonAsync(
            $"/api/records/{rec.Id}/comments/{comment.Id}",
            new { body = "edited comment" })).EnsureSuccessStatusCode();
        (await client.DeleteAsync(
            $"/api/records/{rec.Id}/comments/{comment.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(RecordSchemaEventTypes.RecordCommentCreated, types);
        Assert.Contains(RecordSchemaEventTypes.RecordCommentEdited, types);
        Assert.Contains(RecordSchemaEventTypes.RecordCommentDeleted, types);
    }

    private static async Task Prime(HttpClient client) =>
        (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();

    private static async Task<RecordTypeDto> CreateRecordType(
        HttpClient client, string shortCode, string name)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest(shortCode, name, null, null, null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RecordTypeDto>())!;
    }

    private static async Task<EdgeTypeDto> CreateEdgeType(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/record-edge-types/",
            new CreateEdgeTypeRequest("link", "Link", null, true, false, "many_to_many", null, null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EdgeTypeDto>())!;
    }

    private sealed record RecordSnapshot(Guid Id);
    private sealed record CommentSnapshot(Guid Id);
}
