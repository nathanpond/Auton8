using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class EntityEdgeWriterTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement EmptyValues()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CreateRecord_WritesCreatorAndAssigneeEdges()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("EDG", "Edges type", null, null, null), Actor);

        var assigneeA = Guid.NewGuid();
        var assigneeB = Guid.NewGuid();

        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "First", null, null, EmptyValues(),
                new[] { assigneeA, assigneeB }),
            Actor);

        await using var db = database.CreateDbContext();
        var edges = await db.EntityEdges
            .Where(e => e.ToKind == EntityKinds.Record && e.ToId == record.Id.ToString())
            .ToListAsync();

        Assert.Contains(edges, e =>
            e.EdgeKind == EdgeKinds.Creator
            && e.FromKind == EntityKinds.User
            && e.FromId == Actor.ToString());
        Assert.Contains(edges, e =>
            e.EdgeKind == EdgeKinds.Assignee && e.FromId == assigneeA.ToString());
        Assert.Contains(edges, e =>
            e.EdgeKind == EdgeKinds.Assignee && e.FromId == assigneeB.ToString());
        Assert.Equal(3, edges.Count);
    }

    [Fact]
    public async Task UpdateAssignees_AddsAndRemovesEdgesToMatch()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("DIF", "Diff type", null, null, null), Actor);

        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var add = Guid.NewGuid();

        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Diff", null, null, EmptyValues(),
                new[] { keep, drop }),
            Actor);

        await recordStore.UpdateAsync(
            record.Id,
            new UpdateRecordInput(null, Optional<string?>.None, Optional<DateOnly?>.None, null,
                new[] { keep, add }),
            Actor);

        await using var db = database.CreateDbContext();
        var assigneeFromIds = await db.EntityEdges
            .Where(e => e.EdgeKind == EdgeKinds.Assignee
                     && e.ToKind == EntityKinds.Record
                     && e.ToId == record.Id.ToString())
            .Select(e => e.FromId)
            .ToListAsync();

        Assert.Equal(2, assigneeFromIds.Count);
        Assert.Contains(keep.ToString(), assigneeFromIds);
        Assert.Contains(add.ToString(), assigneeFromIds);
        Assert.DoesNotContain(drop.ToString(), assigneeFromIds);
    }

    [Fact]
    public async Task ReconciliationDriftIsZero_AfterCreateAndUpdate()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();
        var reconciler = database.CreateEdgeReconciler();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("REC", "Reconcile", null, null, null), Actor);

        var users = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        for (var i = 0; i < 3; i++)
        {
            var rec = await recordStore.CreateAsync(
                new CreateRecordInput(type.Id, $"Rec {i}", null, null, EmptyValues(),
                    new[] { users[i], users[i + 1] }),
                Actor);

            await recordStore.UpdateAsync(
                rec.Id,
                new UpdateRecordInput(null, Optional<string?>.None, Optional<DateOnly?>.None, null,
                    new[] { users[i + 1], users[i + 2] }),
                Actor);
        }

        var drift = await reconciler.GetRecordEdgeDriftAsync();

        Assert.Equal(0, drift.Total);
    }

    [Fact]
    public async Task SyncUserEdges_NoChange_DoesNothing()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var writer = PostgresTestDatabase.CreateEdgeWriter();
        var typeStore = database.CreateRecordTypeStore();
        var recordStore = database.CreateRecordStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("NOC", "No-change", null, null, null), Actor);
        var user = Guid.NewGuid();
        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Stable", null, null, EmptyValues(),
                new[] { user }),
            Actor);

        await using (var db = database.CreateDbContext())
        {
            await writer.SyncUserEdgesAsync(
                db,
                EdgeKinds.Assignee,
                EntityKinds.Record,
                record.Id.ToString(),
                oldUserIds: new[] { user },
                newUserIds: new[] { user },
                Actor,
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = database.CreateDbContext();
        var assigneeCount = await verifyDb.EntityEdges
            .CountAsync(e => e.EdgeKind == EdgeKinds.Assignee
                          && e.ToId == record.Id.ToString());
        Assert.Equal(1, assigneeCount);
    }
}
