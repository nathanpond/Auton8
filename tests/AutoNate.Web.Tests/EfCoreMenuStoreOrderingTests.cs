using System.Text.Json;
using AutoNate.Web.Services.Menus;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuEntity = AutoNate.Web.Persistence.Scaffolded.Menu;

namespace AutoNate.Web.Tests;

/// <summary>
/// A menu built through the UI has a durable order (#509).
/// </summary>
/// <remarks>
/// <para>
/// The create endpoint used to default a missing <c>sortOrder</c> to <c>0</c>,
/// so every item added with "Add top-level item" was stored at the same sort
/// key. The reads ordered by <c>SortOrder</c> alone, which is not a total order
/// once the keys collide, so the displayed order was whatever Postgres returned
/// and was not promised to be stable between reloads.
/// </para>
/// <para>
/// It also fed #503: the editor reindexes to <c>0..n-1</c> and compares against
/// the stored values, so a menu of all-zeros read structurally dirty the moment
/// it was touched, and stayed dirty even if the user dragged the row back.
/// </para>
/// <para>
/// What this does NOT cover: the SPA half, where a successful create or delete
/// used to leave a stale <c>pendingItems</c> that "Save order" would then post.
/// That is React state with no server surface to assert against here.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EfCoreMenuStoreOrderingTests
{
    private static async Task<IMenuStore> FreshMenuAsync(PostgresTestDatabase database, string key)
    {
        await using (var db = database.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM menu_items;");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM menus;");
            db.Set<MenuEntity>().Add(new MenuEntity
            {
                Id = Guid.NewGuid(),
                Key = key,
                Name = key,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return database.CreateMenuStore();
    }

    private static CreateMenuItemInput Item(string displayName, int? sortOrder = null) =>
        new(
            ParentId: null,
            SortOrder: sortOrder,
            DisplayName: displayName,
            Icon: null,
            ItemType: "group",
            Config: JsonDocument.Parse("{}").RootElement,
            PermissionRequired: null,
            IsVisible: true);

    [Fact]
    public async Task Items_created_without_a_sort_order_are_appended_not_stacked_on_zero()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await FreshMenuAsync(database, "main");

        await store.CreateItemAsync("main", Item("first"));
        await store.CreateItemAsync("main", Item("second"));
        await store.CreateItemAsync("main", Item("third"));

        var menu = await store.GetMenuTreeAsync("main");
        var orders = menu!.Items.Select(i => i.SortOrder).ToList();

        // The defect in one assertion: three items all at 0.
        Assert.True(
            orders.Distinct().Count() == orders.Count,
            $"items created without a sortOrder share sort keys ({string.Join(", ", orders)}); "
            + "their relative order is then whatever the database returns (#509).");

        // And they are in creation order, which is what "append" means.
        Assert.Equal(
            new[] { "first", "second", "third" },
            menu.Items.Select(i => i.DisplayName).ToArray());
    }

    [Fact]
    public async Task An_explicit_sort_order_is_still_honoured()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await FreshMenuAsync(database, "main");

        // The complement. "Always append" would satisfy the test above while
        // breaking the tree PUT, which sends dense positions deliberately.
        await store.CreateItemAsync("main", Item("last", sortOrder: 50));
        await store.CreateItemAsync("main", Item("first", sortOrder: 10));

        var menu = await store.GetMenuTreeAsync("main");

        Assert.Equal(
            new[] { "first", "last" },
            menu!.Items.Select(i => i.DisplayName).ToArray());
        Assert.Equal(new[] { 10, 50 }, menu.Items.Select(i => i.SortOrder).ToArray());
    }

    [Fact]
    public async Task Colliding_sort_orders_still_read_back_in_one_stable_order()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await FreshMenuAsync(database, "main");

        // Legacy shape: every item explicitly at 0, which is exactly what rows
        // created before this fix look like. The order must still be decided by
        // something, and decided the same way each time.
        foreach (var name in new[] { "alpha", "bravo", "charlie", "delta" })
        {
            await store.CreateItemAsync("main", Item(name, sortOrder: 0));
        }

        var first = await store.GetMenuTreeAsync("main");
        var second = await store.GetMenuTreeAsync("main");
        var third = await store.GetMenuTreeAsync("main");

        var a = first!.Items.Select(i => i.DisplayName).ToArray();
        var b = second!.Items.Select(i => i.DisplayName).ToArray();
        var c = third!.Items.Select(i => i.DisplayName).ToArray();

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal(4, a.Length);
    }
}
