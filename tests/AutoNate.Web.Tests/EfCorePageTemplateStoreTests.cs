using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Menus;
using Xunit;
using PageTemplateEntity = AutoNate.Web.Persistence.Scaffolded.PageTemplate;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class EfCorePageTemplateStoreTests
{
    [Fact]
    public async Task ListEnabledAsync_ReturnsOnlyEnabledTemplatesOrderedByName()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedTemplatesAsync(database,
            ("zeta", "Zeta", isEnabled: true),
            ("alpha", "Alpha", isEnabled: true),
            ("disabled", "Beta (off)", isEnabled: false));

        var store = database.CreatePageTemplateStore();

        var enabled = await store.ListEnabledAsync();

        Assert.Equal(2, enabled.Count);
        Assert.Equal(new[] { "alpha", "zeta" }, enabled.Select(t => t.Key));
    }

    [Fact]
    public async Task GetByKeyAsync_ReturnsTemplateEvenWhenDisabled()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedTemplatesAsync(database,
            ("offTemplate", "Off", isEnabled: false));

        var store = database.CreatePageTemplateStore();

        var found = await store.GetByKeyAsync("offTemplate");

        Assert.NotNull(found);
        Assert.Equal("offTemplate", found!.Key);
        Assert.False(found.IsEnabled);
    }

    [Fact]
    public async Task GetByKeyAsync_ReturnsNullWhenMissing()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreatePageTemplateStore();

        Assert.Null(await store.GetByKeyAsync("does-not-exist"));
    }

    private static async Task SeedTemplatesAsync(
        PostgresTestDatabase database,
        params (string Key, string Name, bool isEnabled)[] templates)
    {
        await using var db = database.CreateDbContext();
        foreach (var (key, name, isEnabled) in templates)
        {
            db.PageTemplates.Add(new PageTemplateEntity
            {
                Id = Guid.NewGuid(),
                Key = key,
                Name = name,
                Description = null,
                IsEnabled = isEnabled,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }
}
