using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class EfCoreRecordEdgeStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static async Task<RecordType> SeedTypeAsync(EfCoreRecordTypeStore typeStore, string code) =>
        await typeStore.CreateAsync(new CreateRecordTypeInput(code, $"{code} type", null, null, null), Actor);

    [Fact]
    public async Task CreateAsync_LinksTwoRecords()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var accountType = await SeedTypeAsync(typeStore, "ACC");
        var contactType = await SeedTypeAsync(typeStore, "CON");
        var account = await recordStore.CreateAsync(new CreateRecordInput(accountType.Id, "Acme", null, null, Json("{}"), null), Actor);
        var contact = await recordStore.CreateAsync(new CreateRecordInput(contactType.Id, "Alice", null, null, Json("{}"), null), Actor);

        var edgeType = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "HAS",
            "has contact",
            "is contact of",
            IsDirected: true,
            AllowSelfReference: false,
            Cardinality: RecordEdgeCardinality.OneToMany,
            FromRecordTypeIds: null,
            ToRecordTypeIds: null));

        var edge = await edgeStore.CreateAsync(new CreateRecordEdgeInput(
            edgeType.Id,
            account.Id,
            contact.Id,
            Json("{}")), Actor);

        Assert.NotEqual(Guid.Empty, edge.Id);
        Assert.Equal(account.Id, edge.FromRecordId);
        Assert.Equal(contact.Id, edge.ToRecordId);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateEdge()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "REL", "rel", null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);

        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor));
    }

    [Fact]
    public async Task CreateAsync_UndirectedRejectsReverseDuplicate()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "USR");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "FRI", "friend of", null,
            IsDirected: false,
            AllowSelfReference: false,
            Cardinality: RecordEdgeCardinality.ManyToMany,
            FromRecordTypeIds: null,
            ToRecordTypeIds: null));

        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);

        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, b.Id, a.Id, Json("{}")), Actor));
    }

    [Fact]
    public async Task CreateAsync_RejectsSelfReferenceWhenDisallowed()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "REF", "refers to", null, true, AllowSelfReference: false,
            RecordEdgeCardinality.ManyToMany, null, null));

        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, a.Id, Json("{}")), Actor));
    }

    [Fact]
    public async Task CreateAsync_EnforcesOneToOneCardinality()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);
        var c = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "C", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "MGR", "manages", null, true, false,
            RecordEdgeCardinality.OneToOne, null, null));

        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);

        // Same source again → blocked.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, c.Id, Json("{}")), Actor));

        // Same target again → blocked.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, c.Id, b.Id, Json("{}")), Actor));
    }

    [Fact]
    public async Task CreateAsync_EnforcesManyToOne()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);
        var c = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "C", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "BEL", "belongs to", null, true, false,
            RecordEdgeCardinality.ManyToOne, null, null));

        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);

        // Same source → blocked.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, c.Id, Json("{}")), Actor));

        // Different source, same target → allowed.
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, c.Id, b.Id, Json("{}")), Actor);
    }

    [Fact]
    public async Task CreateAsync_EnforcesAllowedTypePairs()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var accType = await SeedTypeAsync(typeStore, "ACC");
        var conType = await SeedTypeAsync(typeStore, "CON");
        var docType = await SeedTypeAsync(typeStore, "DOC");

        var account = await recordStore.CreateAsync(new CreateRecordInput(accType.Id, "Acme", null, null, Json("{}"), null), Actor);
        var contact = await recordStore.CreateAsync(new CreateRecordInput(conType.Id, "Alice", null, null, Json("{}"), null), Actor);
        var doc = await recordStore.CreateAsync(new CreateRecordInput(docType.Id, "Doc", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "HAS", "has", null, true, false,
            RecordEdgeCardinality.ManyToMany,
            FromRecordTypeIds: new[] { accType.Id },
            ToRecordTypeIds: new[] { conType.Id }));

        // Allowed pair.
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, account.Id, contact.Id, Json("{}")), Actor);

        // Disallowed source type.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, contact.Id, contact.Id, Json("{}")), Actor));

        // Disallowed target type.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, account.Id, doc.Id, Json("{}")), Actor));
    }

    [Fact]
    public async Task ListForRecordAsync_FiltersByDirection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);
        var c = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "C", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "REL", "rel", null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, c.Id, a.Id, Json("{}")), Actor);

        var outgoing = await edgeStore.ListForRecordAsync(a.Id, EdgeDirection.Outgoing, null);
        Assert.Single(outgoing);
        Assert.Equal(b.Id, outgoing[0].ToRecordId);

        var incoming = await edgeStore.ListForRecordAsync(a.Id, EdgeDirection.Incoming, null);
        Assert.Single(incoming);
        Assert.Equal(c.Id, incoming[0].FromRecordId);

        var both = await edgeStore.ListForRecordAsync(a.Id, EdgeDirection.Both, null);
        Assert.Equal(2, both.Count);
    }

    [Fact]
    public async Task TraverseAsync_WalksUpToMaxHops()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);
        var c = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "C", null, null, Json("{}"), null), Actor);
        var d = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "D", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "REL", "rel", null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        // Chain A → B → C → D
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, b.Id, c.Id, Json("{}")), Actor);
        await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, c.Id, d.Id, Json("{}")), Actor);

        var oneHop = await edgeStore.TraverseAsync(new TraverseRequest(
            new[] { a.Id }, null, EdgeDirection.Outgoing, MaxHops: 1));
        // 0 hops (a) + 1 hop (b)
        Assert.Equal(2, oneHop.Count);
        Assert.Contains(oneHop, r => r.RecordId == a.Id && r.Hops == 0);
        Assert.Contains(oneHop, r => r.RecordId == b.Id && r.Hops == 1);

        var threeHops = await edgeStore.TraverseAsync(new TraverseRequest(
            new[] { a.Id }, null, EdgeDirection.Outgoing, MaxHops: 3));
        Assert.Equal(4, threeHops.Count);
        Assert.Contains(threeHops, r => r.RecordId == d.Id && r.Hops == 3);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEdge()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);
        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "REL", "rel", null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        var edge = await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor);

        await edgeStore.DeleteAsync(edge.Id);

        var remaining = await edgeStore.ListForRecordAsync(a.Id, EdgeDirection.Both, null);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task CreateAsync_ValidatesEdgeFieldData()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();
        var edgeStore = database.CreateRecordEdgeStore();

        var t = await SeedTypeAsync(typeStore, "ACC");
        var a = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "A", null, null, Json("{}"), null), Actor);
        var b = await recordStore.CreateAsync(new CreateRecordInput(t.Id, "B", null, null, Json("{}"), null), Actor);

        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "WGT", "weighted", null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        await edgeTypeStore.CreateFieldAsync(et.Id, new CreateRecordEdgeTypeFieldInput(
            "weight", "Weight", "number", Json("{\"variant\":\"decimal\"}"), IsRequired: true, SortOrder: 0));

        // Required edge field missing → reject.
        await Assert.ThrowsAsync<RecordEdgeValidationException>(() =>
            edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{}")), Actor));

        // Provided → success.
        var edge = await edgeStore.CreateAsync(new CreateRecordEdgeInput(et.Id, a.Id, b.Id, Json("{\"weight\":1.5}")), Actor);
        Assert.Equal(1.5, edge.Data.GetProperty("weight").GetDouble());
    }
}
