using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordEdgeShadowTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static async Task<(RecordType A, RecordType B, Models.Records.Record Ar, Models.Records.Record Br, RecordEdgeType ET)>
        SeedAsync(PostgresTestDatabase database, string shortCode = "REL")
    {
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var edgeTypeStore = database.CreateRecordEdgeTypeStore();

        var a = await typeStore.CreateAsync(new CreateRecordTypeInput("AAA" + shortCode, "A", null, null, null), Actor);
        var b = await typeStore.CreateAsync(new CreateRecordTypeInput("BBB" + shortCode, "B", null, null, null), Actor);
        var ar = await recordStore.CreateAsync(new CreateRecordInput(a.Id, "AR", null, null, Empty(), null), Actor);
        var br = await recordStore.CreateAsync(new CreateRecordInput(b.Id, "BR", null, null, Empty(), null), Actor);
        var et = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            shortCode, shortCode, null, true, false, RecordEdgeCardinality.ManyToMany, null, null));

        return (a, b, ar, br, et);
    }

    [Fact]
    public async Task CreateAsync_WritesShadowEntityEdge()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (_, _, ar, br, et) = await SeedAsync(database, "LINK");
        var edgeStore = database.CreateRecordEdgeStore();

        var edge = await edgeStore.CreateAsync(
            new CreateRecordEdgeInput(et.Id, ar.Id, br.Id, Empty()), Actor);

        await using var db = database.CreateDbContext();
        var shadow = await db.EntityEdges.SingleOrDefaultAsync(e => e.Id == edge.Id);

        Assert.NotNull(shadow);
        Assert.Equal("LINK", shadow!.EdgeKind);
        Assert.Equal(EntityKinds.Record, shadow.FromKind);
        Assert.Equal(EntityKinds.Record, shadow.ToKind);
        Assert.Equal(ar.Id.ToString(), shadow.FromId);
        Assert.Equal(br.Id.ToString(), shadow.ToId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBothRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (_, _, ar, br, et) = await SeedAsync(database, "DROP");
        var edgeStore = database.CreateRecordEdgeStore();

        var edge = await edgeStore.CreateAsync(
            new CreateRecordEdgeInput(et.Id, ar.Id, br.Id, Empty()), Actor);

        await edgeStore.DeleteAsync(edge.Id);

        await using var db = database.CreateDbContext();
        Assert.False(await db.RecordEdges.AnyAsync(e => e.Id == edge.Id));
        Assert.False(await db.EntityEdges.AnyAsync(e => e.Id == edge.Id));
    }

    [Fact]
    public async Task ShadowDrift_IsZero_AfterCreateDeleteRoundTrip()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (_, _, ar, br, et) = await SeedAsync(database, "OK");
        var edgeStore = database.CreateRecordEdgeStore();
        var reconciler = database.CreateEdgeReconciler();

        var edge = await edgeStore.CreateAsync(
            new CreateRecordEdgeInput(et.Id, ar.Id, br.Id, Empty()), Actor);

        var drift1 = await reconciler.GetRecordEdgeShadowDriftAsync();
        Assert.Equal(0, drift1.Total);

        await edgeStore.DeleteAsync(edge.Id);
        var drift2 = await reconciler.GetRecordEdgeShadowDriftAsync();
        Assert.Equal(0, drift2.Total);
    }

    [Fact]
    public async Task ShadowDrift_DetectsMissingShadow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (_, _, ar, br, et) = await SeedAsync(database, "ORF");
        var edgeStore = database.CreateRecordEdgeStore();
        var reconciler = database.CreateEdgeReconciler();

        var edge = await edgeStore.CreateAsync(
            new CreateRecordEdgeInput(et.Id, ar.Id, br.Id, Empty()), Actor);

        // Manually delete the shadow to simulate drift, then verify the
        // reconciler notices.
        await using (var db = database.CreateDbContext())
        {
            var shadow = await db.EntityEdges.SingleAsync(e => e.Id == edge.Id);
            db.EntityEdges.Remove(shadow);
            await db.SaveChangesAsync();
        }

        var drift = await reconciler.GetRecordEdgeShadowDriftAsync();
        Assert.Equal(1, drift.MissingShadows);
    }
}
