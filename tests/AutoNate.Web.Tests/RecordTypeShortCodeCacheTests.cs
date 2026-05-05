using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordTypeShortCodeCacheTests
{
    [Fact]
    public async Task TryGetShortCode_ReturnsShortCode_AfterRefresh()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeId = await SeedRecordTypeAsync(database, shortCode: "asset");

        var cache = new RecordTypeShortCodeCache(
            database.CreateDbContextFactory(),
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        Assert.True(cache.TryGetShortCode(typeId, out var shortCode));
        Assert.Equal("asset", shortCode);
    }

    [Fact]
    public async Task TryGetShortCode_ReflectsRename_AfterRefresh()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var typeId = await SeedRecordTypeAsync(database, shortCode: "asset");

        var cache = new RecordTypeShortCodeCache(
            database.CreateDbContextFactory(),
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        await RenameRecordTypeShortCodeAsync(database, typeId, "vehicle");
        await cache.RefreshAsync();

        Assert.True(cache.TryGetShortCode(typeId, out var shortCode));
        Assert.Equal("vehicle", shortCode);
    }

    [Fact]
    public async Task TryGetShortCode_ReturnsFalse_WhenIdUnknown()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var cache = new RecordTypeShortCodeCache(
            database.CreateDbContextFactory(),
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        Assert.False(cache.TryGetShortCode(Guid.NewGuid(), out _));
    }

    private static async Task<Guid> SeedRecordTypeAsync(
        PostgresTestDatabase database,
        string shortCode)
    {
        var factory = database.CreateDbContextFactory();
        await using var dbContext = await factory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var entity = new RecordTypeEntity
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            Name = shortCode,
            Description = null,
            Icon = null,
            Color = null,
            IsSystem = false,
            IsArchived = false,
            NextKeyNumber = 1,
            CreatedAtUtc = now,
            CreatedBy = Guid.Empty,
            UpdatedAtUtc = now,
            UpdatedBy = Guid.Empty
        };
        dbContext.RecordTypes.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    private static async Task RenameRecordTypeShortCodeAsync(
        PostgresTestDatabase database,
        Guid typeId,
        string newShortCode)
    {
        var factory = database.CreateDbContextFactory();
        await using var dbContext = await factory.CreateDbContextAsync();

        var entity = await dbContext.RecordTypes.FirstAsync(rt => rt.Id == typeId);
        entity.ShortCode = newShortCode;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }
}
