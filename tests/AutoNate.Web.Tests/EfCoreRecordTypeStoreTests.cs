using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class EfCoreRecordTypeStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CreateAsync_PersistsRecordTypeAndWritesAudit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        var created = await store.CreateAsync(
            new CreateRecordTypeInput("ACC", "Account", "Customer account", Icon: null, Color: "#336699"),
            Actor);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("ACC", created.ShortCode);
        Assert.Equal("Account", created.Name);
        Assert.False(created.IsArchived);
        Assert.Equal(1, created.NextKeyNumber);

        var loaded = await store.GetAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("ACC", loaded.ShortCode);

        var audit = await store.ListAuditAsync(created.Id, take: 10);
        var entry = Assert.Single(audit);
        Assert.Equal(RecordTypeAuditChangeKinds.TypeCreated, entry.ChangeKind);
        Assert.Equal(Actor, entry.ChangedBy);
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidShortCode()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        await Assert.ThrowsAsync<RecordTypeValidationException>(() =>
            store.CreateAsync(new CreateRecordTypeInput("x", "Bad", null, null, null), Actor));
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateShortCode()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);

        await Assert.ThrowsAsync<RecordTypeValidationException>(() =>
            store.CreateAsync(new CreateRecordTypeInput("acc", "Another", null, null, null), Actor));
    }

    [Fact]
    public async Task ListAsync_HidesArchivedUnlessRequested()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        var live = await store.CreateAsync(new CreateRecordTypeInput("LIV", "Live", null, null, null), Actor);
        var dead = await store.CreateAsync(new CreateRecordTypeInput("DED", "Dead", null, null, null), Actor);
        await store.SetArchivedAsync(dead.Id, archived: true, Actor);

        var visible = await store.ListAsync(includeArchived: false);
        Assert.Single(visible);
        Assert.Equal(live.Id, visible[0].Id);

        var all = await store.ListAsync(includeArchived: true);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task SetArchivedAsync_IsIdempotentAndLogsOnceOnly()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        var created = await store.CreateAsync(new CreateRecordTypeInput("ARC", "Archivable", null, null, null), Actor);

        await store.SetArchivedAsync(created.Id, archived: true, Actor);
        await store.SetArchivedAsync(created.Id, archived: true, Actor); // no-op

        var audit = await store.ListAuditAsync(created.Id, take: 10);
        Assert.Equal(2, audit.Count); // created + archived, no duplicate archived
        Assert.Equal(RecordTypeAuditChangeKinds.TypeArchived, audit[0].ChangeKind);
    }

    [Fact]
    public async Task CreateFieldAsync_PersistsNormalizedConfig()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();
        var type = await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);

        var field = await store.CreateFieldAsync(type.Id,
            new CreateRecordTypeFieldInput(
                FieldKey: "status",
                DisplayName: "Status",
                DataType: "option",
                Config: Json("{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}"),
                IsRequired: true,
                SortOrder: 0),
            Actor);

        Assert.Equal("status", field.FieldKey);
        Assert.Equal("option", field.DataType);
        Assert.True(field.IsRequired);

        var choices = field.Config.GetProperty("choices");
        Assert.Equal(2, choices.GetArrayLength());

        var list = await store.ListFieldsAsync(type.Id, includeArchived: false);
        Assert.Single(list);
    }

    [Fact]
    public async Task CreateFieldAsync_RejectsDuplicateFieldKey()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();
        var type = await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);

        await store.CreateFieldAsync(type.Id,
            new CreateRecordTypeFieldInput("priority", "Priority", "number", Json("{}"), false, 0),
            Actor);

        await Assert.ThrowsAsync<RecordTypeValidationException>(() =>
            store.CreateFieldAsync(type.Id,
                new CreateRecordTypeFieldInput("priority", "Priority 2", "text", Json("{}"), false, 0),
                Actor));
    }

    [Fact]
    public async Task CreateFieldAsync_RejectsInvalidFieldKey()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();
        var type = await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);

        await Assert.ThrowsAsync<RecordTypeValidationException>(() =>
            store.CreateFieldAsync(type.Id,
                new CreateRecordTypeFieldInput("Not Valid", "Bad", "text", Json("{}"), false, 0),
                Actor));
    }

    [Fact]
    public async Task CreateFieldAsync_RejectsUnknownRecordType()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();

        await Assert.ThrowsAsync<RecordTypeNotFoundException>(() =>
            store.CreateFieldAsync(Guid.NewGuid(),
                new CreateRecordTypeFieldInput("status", "Status", "text", Json("{}"), false, 0),
                Actor));
    }

    [Fact]
    public async Task UpdateFieldAsync_LogsOnlyForActualChanges()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();
        var type = await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);

        var field = await store.CreateFieldAsync(type.Id,
            new CreateRecordTypeFieldInput("status", "Status", "text", Json("{}"), false, 0),
            Actor);

        var updated = await store.UpdateFieldAsync(type.Id, field.Id,
            new UpdateRecordTypeFieldInput("Status Updated", Json("{\"maxLength\":100}"), true, 3),
            Actor);

        Assert.Equal("Status Updated", updated.DisplayName);
        Assert.True(updated.IsRequired);
        Assert.Equal(3, updated.SortOrder);
        Assert.Equal(100, updated.Config.GetProperty("maxLength").GetInt32());

        var audit = await store.ListAuditAsync(type.Id, take: 20);
        // type_created, field_added, field_renamed, field_required_changed, field_reordered, field_config_changed
        Assert.Equal(6, audit.Count);
        Assert.Contains(audit, a => a.ChangeKind == RecordTypeAuditChangeKinds.FieldRenamed);
        Assert.Contains(audit, a => a.ChangeKind == RecordTypeAuditChangeKinds.FieldConfigChanged);
    }

    [Fact]
    public async Task SetFieldArchivedAsync_HidesFieldUnlessRequested()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordTypeStore();
        var type = await store.CreateAsync(new CreateRecordTypeInput("ACC", "Account", null, null, null), Actor);
        var field = await store.CreateFieldAsync(type.Id,
            new CreateRecordTypeFieldInput("note", "Note", "text", Json("{}"), false, 0),
            Actor);

        await store.SetFieldArchivedAsync(type.Id, field.Id, archived: true, Actor);

        Assert.Empty(await store.ListFieldsAsync(type.Id, includeArchived: false));
        Assert.Single(await store.ListFieldsAsync(type.Id, includeArchived: true));
    }
}
