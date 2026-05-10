using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreRecordCommentStoreTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static async Task<AutoNate.Web.Models.Records.Record> SeedRecordAsync(PostgresTestDatabase db, string code = "ACC")
    {
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore();
        var type = await typeStore.CreateAsync(new CreateRecordTypeInput(code, $"{code} type", null, null, null), Alice);
        return await recordStore.CreateAsync(new CreateRecordInput(type.Id, "Test", null, null, Empty(), null), Alice);
    }

    [Fact]
    public async Task CreateAsync_PersistsComment()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var comment = await store.CreateAsync(record.Id, "  hello world  ", Alice);

        Assert.Equal("hello world", comment.Body); // trimmed
        Assert.Equal(Alice, comment.AuthorId);
        Assert.Equal(record.Id, comment.RecordId);
        Assert.False(comment.IsDeleted);
        Assert.Equal(comment.CreatedAtUtc, comment.BodyUpdatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_RejectsEmptyBody()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        await Assert.ThrowsAsync<RecordCommentValidationException>(() =>
            store.CreateAsync(record.Id, "   ", Alice));
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownRecord()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateRecordCommentStore();

        await Assert.ThrowsAsync<RecordCommentValidationException>(() =>
            store.CreateAsync(Guid.NewGuid(), "hi", Alice));
    }

    [Fact]
    public async Task EditAsync_WritesRevisionAndBumpsBodyUpdatedAt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var initial = await store.CreateAsync(record.Id, "original", Alice);
        await Task.Delay(10);
        var edited1 = await store.EditAsync(initial.Id, "edited once", Alice);

        Assert.Equal("edited once", edited1.Body);
        Assert.True(edited1.BodyUpdatedAtUtc > edited1.CreatedAtUtc);

        await Task.Delay(10);
        await store.EditAsync(initial.Id, "edited twice", Alice);

        var revisions = await store.ListRevisionsAsync(initial.Id);
        Assert.Equal(2, revisions.Count);

        // Newest first.
        Assert.Equal("edited once", revisions[0].Body);
        Assert.Equal("original", revisions[1].Body);
        Assert.All(revisions, r => Assert.Equal(Alice, r.ReplacedBy));
    }

    [Fact]
    public async Task EditAsync_NoOpDoesNotWriteRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "same", Alice);
        var same = await store.EditAsync(c.Id, "same", Alice);

        var revisions = await store.ListRevisionsAsync(c.Id);
        Assert.Empty(revisions);
        Assert.Equal(c.BodyUpdatedAtUtc, same.BodyUpdatedAtUtc);
    }

    [Fact]
    public async Task ListForRecordAsync_HidesDeletedByDefault()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var alive = await store.CreateAsync(record.Id, "alive", Alice);
        var dead = await store.CreateAsync(record.Id, "dead", Alice);
        await store.SoftDeleteAsync(dead.Id, Alice);

        var visible = await store.ListForRecordAsync(record.Id, includeDeleted: false);
        Assert.Single(visible);
        Assert.Equal(alive.Id, visible[0].Id);

        var all = await store.ListForRecordAsync(record.Id, includeDeleted: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Id == dead.Id && c.IsDeleted);
    }

    [Fact]
    public async Task SoftDeleteAsync_PreservesRevisionsAndStampsAuditFields()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "v1", Alice);
        await store.EditAsync(c.Id, "v2", Alice);
        await store.EditAsync(c.Id, "v3", Alice);

        var deleted = await store.SoftDeleteAsync(c.Id, Alice);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(Alice, deleted.DeletedBy);
        Assert.NotNull(deleted.DeletedAtUtc);

        // Revisions still queryable post-delete.
        var revisions = await store.ListRevisionsAsync(c.Id);
        Assert.Equal(2, revisions.Count);
    }

    [Fact]
    public async Task EditAsync_RejectsEditOnDeletedComment()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "x", Alice);
        await store.SoftDeleteAsync(c.Id, Alice);

        await Assert.ThrowsAsync<RecordCommentValidationException>(() =>
            store.EditAsync(c.Id, "y", Alice));
    }

    [Fact]
    public async Task SoftDeleteAsync_IsIdempotent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "x", Alice);
        var first = await store.SoftDeleteAsync(c.Id, Alice);
        var second = await store.SoftDeleteAsync(c.Id, Alice);

        Assert.Equal(first.DeletedAtUtc, second.DeletedAtUtc);
        Assert.Equal(Alice, second.DeletedBy);
    }

    [Fact]
    public async Task EditAsync_RejectsNonAuthor()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "alice's comment", Alice);

        await Assert.ThrowsAsync<RecordCommentForbiddenException>(() =>
            store.EditAsync(c.Id, "bob trying to edit", Bob));
    }

    [Fact]
    public async Task SoftDeleteAsync_RejectsNonAuthor()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var record = await SeedRecordAsync(database);
        var store = database.CreateRecordCommentStore();

        var c = await store.CreateAsync(record.Id, "alice's comment", Alice);

        await Assert.ThrowsAsync<RecordCommentForbiddenException>(() =>
            store.SoftDeleteAsync(c.Id, Bob));
    }
}
