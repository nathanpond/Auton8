using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemIssuesMenuInstallTests
{
    [Fact]
    public async Task EnsureAsync_installs_System_Issues_menu_item_with_templateKey_and_path()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Booting the client runs DatabaseSchemaInitializer.EnsureAsync.
        _ = factory.CreateClient();

        await using var db = factory.Database.CreateDbContext();
        var menuItem = await db.MenuItems.AsNoTracking()
            .Where(m => m.DisplayName == "System Issues")
            .SingleOrDefaultAsync();

        Assert.NotNull(menuItem);
        Assert.Equal("template", menuItem.ItemType);
        // Both fields must be present — the SPA nav drops template items
        // whose resolved path is null, so omitting `path` produces a row
        // that's in the DB but invisible in the nav.
        Assert.Contains("\"templateKey\": \"configSystemIssues\"", menuItem.Config);
        Assert.Contains("\"path\": \"/admin/config/system-issues\"", menuItem.Config);

        var markerCount = await db.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM auth_seed_state WHERE key = 'site_config_system_issues_v3'")
            .CountAsync();
        Assert.Equal(1, markerCount);
    }
}
