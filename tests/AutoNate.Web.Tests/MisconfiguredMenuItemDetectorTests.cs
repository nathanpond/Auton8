using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class MisconfiguredMenuItemDetectorTests
{
    [Fact]
    public async Task Template_item_missing_path_opens_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        // Seed 'home' so the templateKey is recognised — isolates the
        // missing-path scan from the unknown-templateKey scan.
        await SeedTemplateAsync(db, "home");
        var menuItemId = await SeedMenuItemAsync(db, menuId,
            itemType: "template",
            config: """{"templateKey":"home"}""",
            displayName: "Broken Template");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(MisconfiguredMenuItemDetector.FingerprintTemplateMissingPath + menuItemId, issue.Fingerprint);
        Assert.Equal(SystemIssueCategories.DataIntegrity, issue.Category);
        Assert.Equal(SystemIssueSeverities.Warning, issue.Severity);
        Assert.Contains("Broken Template", issue.Title);
    }

    [Fact]
    public async Task Template_item_missing_template_key_opens_a_distinct_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        var menuItemId = await SeedMenuItemAsync(db, menuId,
            itemType: "template",
            config: """{"path":"/foo"}""",
            displayName: "Keyless Template");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(MisconfiguredMenuItemDetector.FingerprintTemplateMissingTemplateKey + menuItemId, issue.Fingerprint);
    }

    [Fact]
    public async Task Template_item_with_unknown_template_key_opens_an_issue_naming_the_key()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        var menuItemId = await SeedMenuItemAsync(db, menuId,
            itemType: "template",
            config: """{"templateKey":"doesNotExist","path":"/x"}""",
            displayName: "Bad Key");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(MisconfiguredMenuItemDetector.FingerprintTemplateUnknownKey + menuItemId, issue.Fingerprint);
        Assert.Contains("doesNotExist", issue.Title);
    }

    [Fact]
    public async Task Route_item_missing_both_path_and_alias_opens_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        var menuItemId = await SeedMenuItemAsync(db, menuId,
            itemType: "route",
            config: "{}",
            displayName: "Pathless Route");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(MisconfiguredMenuItemDetector.FingerprintRouteMissingPath + menuItemId, issue.Fingerprint);
    }

    [Fact]
    public async Task Well_formed_template_and_route_items_produce_no_issues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        // The bootstrap doesn't seed page_templates rows (the production
        // seed is in DatabaseSchemaInitializer.PageTemplatesSeedSql); seed
        // 'home' here so the well-formed template item passes the
        // unknown-key scan.
        await SeedTemplateAsync(db, "home");
        await SeedMenuItemAsync(db, menuId, "template", """{"templateKey":"home","path":"/home"}""", "Home");
        await SeedMenuItemAsync(db, menuId, "route", """{"path":"/foo"}""", "Foo Route");
        await SeedMenuItemAsync(db, menuId, "route", """{"aliasPath":"/bar","path":"/foo"}""", "Bar Alias");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Operator_fixing_the_row_resolves_the_open_issue_on_next_tick()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        await SeedTemplateAsync(db, "home"); // valid templateKey post-fix
        var menuItemId = await SeedMenuItemAsync(db, menuId,
            itemType: "template",
            config: """{"templateKey":"home"}""",
            displayName: "Initially Broken");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);

        // Operator patches in the missing path.
        await using (var ctx = db.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE menu_items
                SET config = '{{""templateKey"":""home"",""path"":""/home""}}'::jsonb
                WHERE id = {menuItemId}");
        }
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
    }

    [Fact]
    public async Task Menu_store_create_item_triggers_a_real_time_detector_run()
    {
        // The user-facing contract: when the operator (or an installer) saves
        // a menu_item that the SPA can't render, the issue surfaces almost
        // immediately — not after the 30-min periodic sweep. The store
        // injects the detector and fires it after every mutation; this test
        // exercises that wiring through the store, not by calling the
        // detector directly.
        await using var db = await PostgresTestDatabase.CreateAsync();
        await ClearMenuItemsAsync(db);
        await SeedTemplateAsync(db, "home");

        var store = new EfCoreMenuStore(
            db.CreateDbContextFactory(),
            db.CreateAuthorizer(enabled: false),
            menuMisconfigurationDetector: BuildDetector(db));

        var menuKey = await GetSeededMenuKeyAsync(db);
        var created = await store.CreateItemAsync(menuKey,
            new CreateMenuItemInput(
                ParentId: null,
                SortOrder: 999,
                DisplayName: "Broken via store",
                Icon: null,
                ItemType: "template",
                Config: JsonDocument.Parse("""{"templateKey":"home"}""").RootElement,
                PermissionRequired: null,
                IsVisible: true));

        // Fire-and-forget: give the background tick time to land.
        await WaitForIssueAsync(db,
            MisconfiguredMenuItemDetector.FingerprintTemplateMissingPath + created.Id,
            TimeSpan.FromSeconds(5));

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(MisconfiguredMenuItemDetector.FingerprintTemplateMissingPath + created.Id, issue.Fingerprint);
    }

    private static MisconfiguredMenuItemDetector BuildDetector(PostgresTestDatabase db)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new MisconfiguredMenuItemDetector(
            db.CreateDbContextFactory(), store, store,
            Options.Create(new MisconfiguredMenuItemDetectorOptions { BatchSize = 100 }),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<MisconfiguredMenuItemDetector>.Instance);
    }

    private static async Task<string> GetSeededMenuKeyAsync(PostgresTestDatabase db)
    {
        await using var ctx = db.CreateDbContext();
        return await ctx.Menus.AsNoTracking().Select(m => m.Key).FirstAsync();
    }

    private static async Task WaitForIssueAsync(PostgresTestDatabase db, string fingerprint, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var ctx = db.CreateDbContext();
            if (await ctx.SystemIssues.AsNoTracking().AnyAsync(i => i.Fingerprint == fingerprint))
                return;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"Issue with fingerprint '{fingerprint}' did not appear within {timeout.TotalSeconds}s.");
    }

    [Fact]
    public async Task Spa_render_failure_endpoint_records_an_issue_when_the_row_is_actually_broken()
    {
        // The end-to-end render-time loop: the SPA POSTs /menu-render-failure
        // with the offending menu_item id; the backend re-validates server-
        // side and opens the same fingerprint the periodic detector would.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var dbFactory = factory.Services.GetRequiredService<
            IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        var menuItemId = await SeedBrokenTemplateAsync(dbFactory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login
        var resp = await client.PostAsJsonAsync(
            "/api/system-issues/menu-render-failure",
            new { menuItemId });
        resp.EnsureSuccessStatusCode();

        await using var read = await dbFactory.CreateDbContextAsync();
        var issue = await read.SystemIssues.AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.Fingerprint == MisconfiguredMenuItemDetector.FingerprintTemplateMissingPath + menuItemId);
        Assert.NotNull(issue);
        // JSONB normalises whitespace so substring match isn't reliable —
        // parse and check the field directly.
        using var facts = JsonDocument.Parse(issue!.FactsJson);
        Assert.Equal("spa_render_report", facts.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Spa_render_failure_endpoint_is_a_noop_when_the_row_actually_looks_fine()
    {
        // Server-side re-validation guard: a SPA can't spoof an issue by
        // POSTing the id of a well-formed row. (Stale render against a
        // since-fixed row lands here too — same no-op outcome.)
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var dbFactory = factory.Services.GetRequiredService<
            IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        var menuItemId = await SeedWellFormedTemplateAsync(dbFactory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");
        var resp = await client.PostAsJsonAsync(
            "/api/system-issues/menu-render-failure",
            new { menuItemId });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"issuesOpened\":0", body);

        await using var read = await dbFactory.CreateDbContextAsync();
        Assert.Empty(await read.SystemIssues.AsNoTracking()
            .Where(i => i.RelatedEntityId == menuItemId.ToString())
            .ToListAsync());
    }

    private static async Task<Guid> SeedBrokenTemplateAsync(
        IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext> dbFactory)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        var menuId = await ctx.Menus.AsNoTracking().Select(m => m.Id).FirstAsync();
        var id = Guid.NewGuid();
        // Note the bootstrap seeds page_templates via DatabaseSchemaInitializer
        // so 'home' exists in the WebApplicationFactory startup path. Insert
        // a template item missing the path field — exactly the bug shape.
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
                icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
            VALUES ({id}, {menuId}, NULL, 999, 'Spa-reported broken', NULL,
                'template', '{{""templateKey"":""home""}}'::jsonb, TRUE, FALSE, NOW(), NOW())");
        return id;
    }

    private static async Task<Guid> SeedWellFormedTemplateAsync(
        IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext> dbFactory)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        var menuId = await ctx.Menus.AsNoTracking().Select(m => m.Id).FirstAsync();
        var id = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
                icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
            VALUES ({id}, {menuId}, NULL, 999, 'Spa-reported well-formed', NULL,
                'template', '{{""templateKey"":""home"",""path"":""/well-formed""}}'::jsonb,
                TRUE, FALSE, NOW(), NOW())");
        return id;
    }

    [Fact]
    public async Task Repeated_runs_dedup_to_one_open_issue_with_bumped_count()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var menuId = await GetSeededMenuIdAsync(db);
        await ClearMenuItemsAsync(db);
        await SeedTemplateAsync(db, "home"); // isolate to the missing-path class
        await SeedMenuItemAsync(db, menuId, "template", """{"templateKey":"home"}""", "Repeat");

        var detector = CreateDetector(db);
        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(3, issue.OccurrenceCount);
    }

    private static MisconfiguredMenuItemDetector CreateDetector(PostgresTestDatabase db)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new MisconfiguredMenuItemDetector(
            db.CreateDbContextFactory(), store, store,
            Options.Create(new MisconfiguredMenuItemDetectorOptions { BatchSize = 100 }),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<MisconfiguredMenuItemDetector>.Instance);
    }

    private static async Task<Guid> GetSeededMenuIdAsync(PostgresTestDatabase db)
    {
        await using var ctx = db.CreateDbContext();
        return await ctx.Menus.AsNoTracking().Select(m => m.Id).FirstAsync();
    }

    // The bootstrap SQL seeds ~35 menu_items rows — some of which trip
    // detector scans (e.g. existing rows the bootstrap intentionally leaves
    // misconfigured for the page_templates seed migration to fix later).
    // For test isolation we clear them so the only rows the detector sees
    // are the ones the test itself seeded.
    private static async Task ClearMenuItemsAsync(PostgresTestDatabase db)
    {
        await using var ctx = db.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM menu_items;");
    }

    private static async Task SeedTemplateAsync(PostgresTestDatabase db, string key)
    {
        await using var ctx = db.CreateDbContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO page_templates (id, key, name, description, is_enabled, created_at_utc, updated_at_utc)
            VALUES ({Guid.NewGuid()}, {key}, {key}, NULL, TRUE, NOW(), NOW())
            ON CONFLICT (key) DO NOTHING");
    }

    private static async Task<Guid> SeedMenuItemAsync(
        PostgresTestDatabase db, Guid menuId, string itemType, string config, string displayName)
    {
        var id = Guid.NewGuid();
        await using var ctx = db.CreateDbContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon, item_type,
                config, is_visible, is_system, created_at_utc, updated_at_utc)
            VALUES (
                {id}, {menuId}, NULL, 999, {displayName}, NULL, {itemType},
                {config}::jsonb, TRUE, FALSE, NOW(), NOW())");
        return id;
    }
}
