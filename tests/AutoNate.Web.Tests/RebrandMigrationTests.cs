using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// The Auto Nate -> Auton8 data migration.
//
// The four SiteAppearance seed/default copies were renamed in lockstep, but
// all of the backend ones are one-time seeds guarded by IF NOT EXISTS: they do
// not revisit an install that has already run. Without a migration, every
// existing deployment keeps the old name in the header, the login page and the
// browser tab while a fresh one shows the new — the same drift that left the
// accent colour disagreeing between the SPA default and the seed.
//
// The risk in a migration is the opposite one: overwriting branding somebody
// chose. Both directions are pinned here.
public sealed class RebrandMigrationTests
{
    private const string OldCover = "/spa/assets/img/login-bg/login-bg-17.jpg";
    private const string NewCover = "/assets/img/login-bg/space.jpg";

    // Rewind an install to its pre-rename state: old values, and the migration
    // markers cleared so the next boot treats it as unmigrated.
    private static async Task RewindAsync(
        AutoNateWebApplicationFactory factory, string siteName, string logoText)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE site_appearance_settings
            SET site_name = {siteName}, logo_text = {logoText},
                login_cover_image_url = {OldCover};
            """);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM auth_seed_state WHERE key IN ('rebrand_auton8_v1', 'login_cover_url_fix_v1');");
    }

    private static async Task<(string SiteName, string LogoText, string? Cover)> ReadAsync(
        AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var rows = await db.Database.SqlQueryRaw<string>("""
            SELECT (site_name || '|' || logo_text || '|' || COALESCE(login_cover_image_url, '')) AS "Value"
            FROM site_appearance_settings LIMIT 1
            """).ToArrayAsync();
        var parts = rows[0].Split('|');
        return (parts[0], parts[1], parts[2]);
    }

    [Fact]
    public async Task An_install_still_on_the_old_name_is_renamed()
    {
        await using var seeded = await AutoNateWebApplicationFactory.CreateAsync();
        _ = seeded.CreateClient();
        await RewindAsync(seeded, "Auto Nate", "Auto Nate");

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        _ = restarted.CreateClient();

        var (siteName, logoText, cover) = await ReadAsync(restarted);
        Assert.Equal("Auton8", siteName);
        Assert.Equal("Auton8", logoText);
        // The same restart repoints the login cover, which pointed at a /spa
        // path nothing serves.
        Assert.Equal(NewCover, cover);
    }

    [Fact]
    public async Task Branding_an_administrator_chose_is_left_alone()
    {
        await using var seeded = await AutoNateWebApplicationFactory.CreateAsync();
        _ = seeded.CreateClient();
        await RewindAsync(seeded, "Contoso Operations", "Contoso");

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        _ = restarted.CreateClient();

        var (siteName, logoText, _) = await ReadAsync(restarted);
        Assert.Equal("Contoso Operations", siteName);
        Assert.Equal("Contoso", logoText);
    }

    [Fact]
    public async Task Each_column_is_migrated_independently()
    {
        // An install may have customised one and not the other. Renaming both
        // on the strength of either would silently discard a chosen logo.
        await using var seeded = await AutoNateWebApplicationFactory.CreateAsync();
        _ = seeded.CreateClient();
        await RewindAsync(seeded, "Auto Nate", "Contoso");

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        _ = restarted.CreateClient();

        var (siteName, logoText, _) = await ReadAsync(restarted);
        Assert.Equal("Auton8", siteName);
        Assert.Equal("Contoso", logoText);
    }

    [Fact]
    public async Task The_migration_does_not_run_twice()
    {
        // One-shot via auth_seed_state. Guarding on the old value alone would
        // still be correct on each boot, but an administrator who deliberately
        // sets the name back to 'Auto Nate' would have it renamed again by the
        // next restart.
        await using var seeded = await AutoNateWebApplicationFactory.CreateAsync();
        _ = seeded.CreateClient();
        await RewindAsync(seeded, "Auto Nate", "Auto Nate");

        await using var migrated = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        _ = migrated.CreateClient();
        Assert.Equal("Auton8", (await ReadAsync(migrated)).SiteName);

        // Deliberately choose the old name, then restart again.
        using (var scope = migrated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE site_appearance_settings SET site_name = 'Auto Nate';");
        }

        await using var again = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        _ = again.CreateClient();

        Assert.Equal("Auto Nate", (await ReadAsync(again)).SiteName);
    }
}
