using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests;

// archived-48: the Site Configuration menu seeded a "Features" item pointing at the
// configFeatures template, but SettingGroup.Features has no settings defined,
// so the item led to a form that reads "No settings in this group yet."
//
// The template and its route stay — the group is a declared extension point
// (SiteSettingsRegistry's "adding a new feature flag" instructions name it) —
// but nothing navigates an admin there until it has content.
[Trait("Category", "Integration")]
public sealed class EmptyFeaturesMenuRemovalTests
{
    [Fact]
    public async Task Site_config_menu_does_not_offer_the_empty_features_page()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Booting the client runs DatabaseSchemaInitializer.EnsureAsync.
        _ = factory.CreateClient();

        await using var db = factory.Database.CreateDbContext();

        // Raw SQL because `config` is jsonb — EF would translate a string
        // Contains() into LIKE, which Postgres rejects on that type.
        var featureItems = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM menu_items " +
            "WHERE config->>'templateKey' = 'configFeatures'").SingleAsync();
        Assert.Equal(0, featureItems);

        // The sibling it sat next to is still seeded, so this asserts the one
        // row is gone rather than that the whole menu failed to seed.
        var generalItems = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM menu_items " +
            "WHERE config->>'templateKey' = 'configGeneral'").SingleAsync();
        Assert.Equal(1, generalItems);
    }
}
