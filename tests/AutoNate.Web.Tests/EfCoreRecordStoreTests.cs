using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class EfCoreRecordStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static async Task<RecordType> SeedTypeWithFieldsAsync(
        EfCoreRecordTypeStore typeStore,
        string shortCode,
        params (string FieldKey, string Display, string DataType, string ConfigJson, bool Required)[] fields)
    {
        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput(shortCode, $"{shortCode} type", null, null, null), Actor);

        var order = 0;
        foreach (var (key, display, dataType, cfg, required) in fields)
        {
            await typeStore.CreateFieldAsync(
                type.Id,
                new CreateRecordTypeFieldInput(key, display, dataType, Json(cfg), required, order++),
                Actor);
        }

        return type;
    }

    [Fact]
    public async Task CreateAsync_AllocatesSequentialKeysAndWritesHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();
        var history = database.CreateRecordHistoryStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("status", "Status", "option",
                "{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}",
                Required: false),
            ("priority", "Priority", "number", "{\"variant\":\"integer\"}", Required: false));

        var first = await store.CreateAsync(
            new CreateRecordInput(type.Id, "Acme Corp", Json("{\"status\":\"open\",\"priority\":1}"), null),
            Actor);
        Assert.Equal("ACC-1", first.Key);
        Assert.Equal(1, first.KeyNumber);

        var second = await store.CreateAsync(
            new CreateRecordInput(type.Id, "Globex", Json("{\"status\":\"closed\"}"), null),
            Actor);
        Assert.Equal("ACC-2", second.Key);

        var firstHistory = await history.ListAsync(first.Id, fieldKey: null, take: 100);
        // 1 created + 2 value_changed (status + priority)
        Assert.Equal(3, firstHistory.Count);
        Assert.Contains(firstHistory, h => h.ChangeKind == RecordChangeKinds.Created);
        Assert.Contains(firstHistory, h => h.FieldKey == "status");
        Assert.Contains(firstHistory, h => h.FieldKey == "priority");

        var secondHistory = await history.ListAsync(second.Id, fieldKey: null, take: 100);
        // 1 created + 1 value_changed (only status was provided)
        Assert.Equal(2, secondHistory.Count);
    }

    [Fact]
    public async Task CreateAsync_RejectsRequiredFieldMissing()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "REQ",
            ("priority", "Priority", "text", "{}", Required: true));

        await Assert.ThrowsAsync<RecordValidationException>(() =>
            store.CreateAsync(new CreateRecordInput(type.Id, "No priority", Json("{}"), null), Actor));
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownFieldKey()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("name_field", "Name Field", "text", "{}", Required: false));

        await Assert.ThrowsAsync<RecordValidationException>(() =>
            store.CreateAsync(new CreateRecordInput(type.Id, "Bad", Json("{\"surprise\":\"x\"}"), null), Actor));
    }

    [Fact]
    public async Task UpdateAsync_DiffsValuesAndWritesPerFieldHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();
        var history = database.CreateRecordHistoryStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("status", "Status", "option",
                "{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}",
                Required: false),
            ("priority", "Priority", "number", "{\"variant\":\"integer\"}", Required: false));

        var record = await store.CreateAsync(
            new CreateRecordInput(type.Id, "Acme", Json("{\"status\":\"open\",\"priority\":1}"), null),
            Actor);

        // Patch only status; priority should be untouched and not produce a row.
        var updated = await store.UpdateAsync(record.Id,
            new UpdateRecordInput(Name: null, Values: Json("{\"status\":\"closed\"}"), AssigneeIds: null),
            Actor);

        Assert.Equal("closed", updated.Values.GetProperty("status").GetString());
        Assert.Equal(1, updated.Values.GetProperty("priority").GetInt32());

        var statusHistory = await history.ListAsync(record.Id, fieldKey: "status", take: 100);
        // status: created (value_changed at creation) + 1 value_changed at update
        Assert.Equal(2, statusHistory.Count);
        Assert.Equal("\"closed\"", statusHistory[0].NewValue!.Value.GetRawText());
        Assert.Equal("\"open\"", statusHistory[0].OldValue!.Value.GetRawText());

        // No-op patch: same value as current state writes no history rows.
        await store.UpdateAsync(record.Id,
            new UpdateRecordInput(Name: null, Values: Json("{\"status\":\"closed\"}"), AssigneeIds: null),
            Actor);
        var statusHistoryAfter = await history.ListAsync(record.Id, fieldKey: "status", take: 100);
        Assert.Equal(2, statusHistoryAfter.Count);
    }

    [Fact]
    public async Task UpdateAsync_GroupsAllRowsFromOnePatchUnderOneChangeSet()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();
        var history = database.CreateRecordHistoryStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("status", "Status", "option",
                "{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}",
                Required: false),
            ("priority", "Priority", "number", "{\"variant\":\"integer\"}", Required: false));

        var record = await store.CreateAsync(
            new CreateRecordInput(type.Id, "Acme", Json("{\"status\":\"open\",\"priority\":1}"), null),
            Actor);

        // Single PATCH that touches name + 2 fields = should yield rows that share one change_set_id.
        await store.UpdateAsync(record.Id,
            new UpdateRecordInput(Name: "Acme Renamed",
                Values: Json("{\"status\":\"closed\",\"priority\":5}"),
                AssigneeIds: null),
            Actor);

        var entries = await history.ListAsync(record.Id, fieldKey: null, take: 100);
        var creationSet = entries.Where(e => e.ChangeKind == RecordChangeKinds.Created).Select(e => e.ChangeSetId).Single();
        var creationGroup = entries.Where(e => e.ChangeSetId == creationSet).ToList();
        // created + value_changed for status + value_changed for priority
        Assert.Equal(3, creationGroup.Count);

        var updateGroupIds = entries
            .Where(e => e.ChangeSetId != creationSet)
            .Select(e => e.ChangeSetId)
            .Distinct()
            .ToArray();
        // The entire PATCH (name_changed + two value_changed rows) shares one set.
        Assert.Single(updateGroupIds);

        var updateRows = entries.Where(e => e.ChangeSetId == updateGroupIds[0]).ToList();
        Assert.Contains(updateRows, e => e.ChangeKind == RecordChangeKinds.NameChanged);
        Assert.Equal(2, updateRows.Count(e => e.ChangeKind == RecordChangeKinds.ValueChanged));
    }

    [Fact]
    public async Task UpdateAsync_NameChangeWritesNameChangedRow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();
        var history = database.CreateRecordHistoryStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC");
        var record = await store.CreateAsync(
            new CreateRecordInput(type.Id, "Old", Json("{}"), null),
            Actor);

        await store.UpdateAsync(record.Id,
            new UpdateRecordInput(Name: "New", Values: null, AssigneeIds: null),
            Actor);

        var entries = await history.ListAsync(record.Id, fieldKey: null, take: 100);
        Assert.Contains(entries, e => e.ChangeKind == RecordChangeKinds.NameChanged);
    }

    [Fact]
    public async Task SetArchivedAsync_WritesArchiveRowAndIsIdempotent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();
        var history = database.CreateRecordHistoryStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC");
        var record = await store.CreateAsync(
            new CreateRecordInput(type.Id, "X", Json("{}"), null),
            Actor);

        await store.SetArchivedAsync(record.Id, archived: true, Actor);
        await store.SetArchivedAsync(record.Id, archived: true, Actor); // no-op

        var entries = await history.ListAsync(record.Id, fieldKey: null, take: 100);
        var archiveCount = entries.Count(e => e.ChangeKind == RecordChangeKinds.Archived);
        Assert.Equal(1, archiveCount);
    }

    [Fact]
    public async Task SearchAsync_FiltersByJsonbAndPaginates()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("status", "Status", "option",
                "{\"multi\":false,\"choices\":[{\"value\":\"open\",\"label\":\"Open\"},{\"value\":\"closed\",\"label\":\"Closed\"}]}",
                Required: false),
            ("priority", "Priority", "number", "{\"variant\":\"integer\"}", Required: false));

        for (var i = 0; i < 5; i++)
        {
            await store.CreateAsync(
                new CreateRecordInput(type.Id, $"Open {i}", Json("{\"status\":\"open\",\"priority\":" + i + "}"), null),
                Actor);
        }
        for (var i = 0; i < 3; i++)
        {
            await store.CreateAsync(
                new CreateRecordInput(type.Id, $"Closed {i}", Json("{\"status\":\"closed\",\"priority\":" + i + "}"), null),
                Actor);
        }

        var openOnly = await store.SearchAsync(new RecordSearchInput(
            type.Id,
            Filters: new[] { new RecordFilterClause("status", FilterOperator.Equals, Json("\"open\"")) },
            AssigneeId: null,
            IncludeArchived: false,
            Page: 0,
            PageSize: 10,
            Sort: null));

        Assert.Equal(5, openOnly.TotalCount);
        Assert.Equal(5, openOnly.Records.Count);

        var highPriority = await store.SearchAsync(new RecordSearchInput(
            type.Id,
            Filters: new[]
            {
                new RecordFilterClause("priority", FilterOperator.GreaterThanOrEqual, Json("3"))
            },
            AssigneeId: null,
            IncludeArchived: false,
            Page: 0,
            PageSize: 10,
            Sort: null));
        Assert.Equal(2, highPriority.TotalCount); // only "Open 3" and "Open 4"

        var firstPage = await store.SearchAsync(new RecordSearchInput(
            type.Id, null, null, false, Page: 0, PageSize: 5, Sort: "key_asc"));
        Assert.Equal(8, firstPage.TotalCount);
        Assert.Equal(5, firstPage.Records.Count);
        Assert.Equal("ACC-1", firstPage.Records[0].Key);
        Assert.Equal("ACC-5", firstPage.Records[4].Key);

        var secondPage = await store.SearchAsync(new RecordSearchInput(
            type.Id, null, null, false, Page: 1, PageSize: 5, Sort: "key_asc"));
        Assert.Equal(3, secondPage.Records.Count);
        Assert.Equal("ACC-6", secondPage.Records[0].Key);
    }

    [Fact]
    public async Task SearchAsync_AssigneeFilterWorks()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "TSK");
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await store.CreateAsync(new CreateRecordInput(type.Id, "Alice's", Json("{}"), new[] { alice }), Actor);
        await store.CreateAsync(new CreateRecordInput(type.Id, "Bob's", Json("{}"), new[] { bob }), Actor);
        await store.CreateAsync(new CreateRecordInput(type.Id, "Both", Json("{}"), new[] { alice, bob }), Actor);

        var aliceList = await store.SearchAsync(new RecordSearchInput(
            type.Id, null, AssigneeId: alice, false, 0, 50, null));

        Assert.Equal(2, aliceList.TotalCount);
    }

    [Fact]
    public async Task CreateAsync_KeysAreSequentialUnderConcurrency()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "CON");

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => store.CreateAsync(new CreateRecordInput(type.Id, "Conc", Json("{}"), null), Actor))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var keyNumbers = results.Select(r => r.KeyNumber).OrderBy(n => n).ToArray();
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i).ToArray(), keyNumbers);

        var keys = results.Select(r => r.Key).Distinct().ToArray();
        Assert.Equal(20, keys.Length);
    }

    [Fact]
    public async Task UpdateAsync_RejectsArchivedField()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var store = database.CreateRecordStore();

        var type = await SeedTypeWithFieldsAsync(typeStore, "ACC",
            ("note", "Note", "text", "{}", Required: false));

        var record = await store.CreateAsync(
            new CreateRecordInput(type.Id, "X", Json("{\"note\":\"hello\"}"), null), Actor);

        var fields = await typeStore.ListFieldsAsync(type.Id, includeArchived: false);
        var noteField = fields.Single();
        await typeStore.SetFieldArchivedAsync(type.Id, noteField.Id, archived: true, Actor);

        await Assert.ThrowsAsync<RecordValidationException>(() =>
            store.UpdateAsync(record.Id,
                new UpdateRecordInput(null, Json("{\"note\":\"world\"}"), null), Actor));
    }
}
