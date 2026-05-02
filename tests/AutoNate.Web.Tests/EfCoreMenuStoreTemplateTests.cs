using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Menus;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuEntity = AutoNate.Web.Persistence.Scaffolded.Menu;
using PageTemplateEntity = AutoNate.Web.Persistence.Scaffolded.PageTemplate;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreMenuStoreTemplateTests
{
    private static readonly Guid SeedActor = Guid.Empty;

    // The bootstrap SQL seeds the four built-in menus and many route items.
    // For tests that assert against ListPagesAsync output, wipe both tables so
    // the test owns the entire registry surface.
    private static async Task ResetMenusAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM menu_items;");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM menus;");
    }

    [Fact]
    public async Task CreateItemAsync_AcceptsTemplateItemType()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await ResetMenusAsync(database);
        await SeedMenuAsync(database, key: "main", name: "Main");

        var store = database.CreateMenuStore();

        var item = await store.CreateItemAsync("main", new CreateMenuItemInput(
            ParentId: null,
            SortOrder: 0,
            DisplayName: "Manage Users",
            Icon: null,
            ItemType: "template",
            Config: ParseJson("""{"templateKey":"manageUsers"}"""),
            PermissionRequired: null,
            IsVisible: true));

        Assert.Equal("template", item.ItemType);
    }

    [Fact]
    public async Task ListPagesAsync_EmitsTemplateEntriesUsingConfigPath()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await ResetMenusAsync(database);
        await SeedTemplateAsync(database, key: "manageUsers");
        await SeedMenuAsync(database, key: "main", name: "Main");

        var store = database.CreateMenuStore();
        await store.CreateItemAsync("main", new CreateMenuItemInput(
            ParentId: null,
            SortOrder: 0,
            DisplayName: "Manage Users",
            Icon: null,
            ItemType: "template",
            Config: ParseJson("""{"templateKey":"manageUsers","path":"/manage-users"}"""),
            PermissionRequired: null,
            IsVisible: true));

        var pages = await store.ListPagesAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        var entry = Assert.Single(pages);
        Assert.Equal("/manage-users", entry.Path);
        Assert.Equal("template", entry.ContentType);
    }

    [Fact]
    public async Task ListPagesAsync_SkipsTemplateItemsWithoutPath()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await ResetMenusAsync(database);
        await SeedTemplateAsync(database, key: "manageUsers");
        await SeedMenuAsync(database, key: "main", name: "Main");

        // Template menu items now own their own URL — there's no template-level
        // default to fall back to. An item without config.path is unreachable
        // and should be skipped from the page registry.
        var store = database.CreateMenuStore();
        await store.CreateItemAsync("main", new CreateMenuItemInput(
            ParentId: null,
            SortOrder: 0,
            DisplayName: "Manage Users",
            Icon: null,
            ItemType: "template",
            Config: ParseJson("""{"templateKey":"manageUsers"}"""),
            PermissionRequired: null,
            IsVisible: true));

        var pages = await store.ListPagesAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Empty(pages);
    }

    [Fact]
    public async Task GetPageByPathAsync_ResolvesTemplateContentToTemplateKey()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await ResetMenusAsync(database);
        await SeedTemplateAsync(database, key: "manageUsers");
        await SeedMenuAsync(database, key: "main", name: "Main");

        var store = database.CreateMenuStore();
        await store.CreateItemAsync("main", new CreateMenuItemInput(
            ParentId: null,
            SortOrder: 0,
            DisplayName: "Manage Users",
            Icon: null,
            ItemType: "template",
            Config: ParseJson("""{"templateKey":"manageUsers","path":"/manage-users"}"""),
            PermissionRequired: null,
            IsVisible: true));

        var page = await store.GetPageByPathAsync("/manage-users", new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.NotNull(page);
        Assert.Equal("template", page!.ContentType);
        Assert.Equal("manageUsers", page.Content);
        Assert.Equal("/manage-users", page.Path);
    }

    [Fact]
    public async Task ListPagesAsync_SkipsTemplateItemsWithoutTemplateKey()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await ResetMenusAsync(database);
        await SeedMenuAsync(database, key: "main", name: "Main");

        var store = database.CreateMenuStore();
        await store.CreateItemAsync("main", new CreateMenuItemInput(
            ParentId: null,
            SortOrder: 0,
            DisplayName: "Broken",
            Icon: null,
            ItemType: "template",
            Config: ParseJson("""{}"""),
            PermissionRequired: null,
            IsVisible: true));

        var pages = await store.ListPagesAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Empty(pages);
    }

    private static async Task SeedTemplateAsync(
        PostgresTestDatabase database, string key)
    {
        await using var db = database.CreateDbContext();
        db.PageTemplates.Add(new PageTemplateEntity
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = key,
            Description = null,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMenuAsync(
        PostgresTestDatabase database, string key, string name)
    {
        await using var db = database.CreateDbContext();
        db.Menus.Add(new MenuEntity
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = null,
            IsSystem = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = SeedActor,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = SeedActor
        });
        await db.SaveChangesAsync();
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
