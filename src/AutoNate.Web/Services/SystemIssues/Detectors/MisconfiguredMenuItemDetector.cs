using System.Text.Json;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Surfaces menu_items rows the SPA silently drops because their config is
// missing fields the nav renderer needs. Born from a real near-miss: the
// "System Issues" install seeded a template item with templateKey but no
// path; row landed in the DB, nav rendered nothing, and the only signal was
// "the menu item isn't there." A detector that catches this class of
// problem is exactly what the self-healing platform exists for.
//
// Misconfigurations checked (all detect-only — fixing requires human
// knowledge of the intended path/key):
//
//   - template item missing `templateKey` — SPA can't pick a component
//   - template item missing `path` — SPA's NavMenu.pathOf returns null and
//     the item silently disappears from the nav
//   - template item with templateKey that doesn't exist in page_templates —
//     SPA renders an "unknown template" stub
//   - route item missing both `path` and `aliasPath` — same null-path
//     problem, identical symptom
//
// One issue per (menu_item_id, problem_class). When the operator (or a
// future installer patch) fixes the row, the next tick auto-resolves the
// issue with no_longer_present.
public sealed class MisconfiguredMenuItemDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ISystemIssueRecorder recorder,
    ISystemIssueStore issueStore,
    IOptions<MisconfiguredMenuItemDetectorOptions> menuOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<MisconfiguredMenuItemDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly MisconfiguredMenuItemDetectorOptions _menuOptions = menuOptions.Value;

    public const string DetectorIdValue = "misconfigured_menu_item";

    // Fingerprint prefixes — public so a future remediator (if we ever add
    // one) can route by class.
    public const string FingerprintTemplateMissingTemplateKey = "menu:misconfigured:template_missing_key:";
    public const string FingerprintTemplateMissingPath = "menu:misconfigured:template_missing_path:";
    public const string FingerprintTemplateUnknownKey = "menu:misconfigured:template_unknown_key:";
    public const string FingerprintRouteMissingPath = "menu:misconfigured:route_missing_path:";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _menuOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var seenThisTick = new HashSet<string>(StringComparer.Ordinal);

        await ScanTemplateMissingTemplateKeyAsync(connection, seenThisTick, cancellationToken);
        await ScanTemplateMissingPathAsync(connection, seenThisTick, cancellationToken);
        await ScanTemplateUnknownKeyAsync(connection, seenThisTick, cancellationToken);
        await ScanRouteMissingPathAsync(connection, seenThisTick, cancellationToken);

        // Auto-resolve via DB query so issues stranded by an app restart
        // still get cleared when the operator fixes the row.
        var openInDb = await issueStore.ListOpenFingerprintsForDetectorAsync(DetectorIdValue, cancellationToken);
        foreach (var fingerprint in openInDb)
        {
            if (seenThisTick.Contains(fingerprint)) continue;
            await recorder.MarkResolvedByFingerprintAsync(
                fingerprint,
                SystemIssueResolutionKinds.NoLongerPresent,
                notes: "Menu item config no longer matches the misconfiguration pattern.",
                cancellationToken);
        }
    }

    // Targeted scan against a single menu_item id. Driven by the SPA's
    // render-failure report endpoint: when the nav silently drops an item,
    // the SPA POSTs the offending id and the backend re-validates here.
    // Server-side re-validation means the SPA can't spoof an issue — only
    // genuinely broken rows produce one.
    //
    // Returns the count of issue scans that matched, so the endpoint can
    // tell the client whether their report was confirmed (>0) or whether
    // the row actually looks fine from the server's perspective (0).
    // Doesn't touch _previousFingerprints — the periodic full-table sweep
    // owns auto-resolve, so a per-item scan can't accidentally close an
    // issue for a row it didn't look at.
    public async Task<RealtimeScanResult> ScanItemAsync(Guid menuItemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var matched = 0;
        var problems = new List<string>();

        // Run each scan filtered to a single id. Using the same WHERE
        // clauses as the full sweep keeps the per-item path consistent
        // with the periodic sweep — same row produces the same fingerprint
        // either way, and the issue store dedups them automatically.
        matched += await ScanOneAsync(connection, menuItemId, cancellationToken,
            sql: """
                SELECT id, display_name FROM menu_items
                WHERE id = @id
                  AND item_type = 'template'
                  AND COALESCE(config->>'templateKey', '') = '';
                """,
            problemTag: "template_missing_key",
            problems,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateMissingTemplateKey + id,
                Title: $"Menu item '{displayName}' is type=template but has no templateKey",
                Summary: "The SPA can't pick a React component to render; the item is silently dropped from the nav. Set config.templateKey to a registered page template id.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "template_missing_key", source = "spa_render_report" })));

        matched += await ScanOneAsync(connection, menuItemId, cancellationToken,
            sql: """
                SELECT id, display_name FROM menu_items
                WHERE id = @id
                  AND item_type = 'template'
                  AND COALESCE(config->>'templateKey', '') <> ''
                  AND COALESCE(config->>'path', '') = '';
                """,
            problemTag: "template_missing_path",
            problems,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateMissingPath + id,
                Title: $"Menu item '{displayName}' is type=template but has no path",
                Summary: "Template items must own their URL via config.path. The SPA's nav silently drops items whose resolved path is null. Set config.path to the URL the template should mount under.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "template_missing_path", source = "spa_render_report" })));

        matched += await ScanOneAsync(connection, menuItemId, cancellationToken,
            sql: """
                SELECT mi.id, mi.display_name, mi.config->>'templateKey' AS template_key
                FROM menu_items mi
                WHERE mi.id = @id
                  AND mi.item_type = 'template'
                  AND COALESCE(mi.config->>'templateKey', '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM page_templates pt WHERE pt.key = mi.config->>'templateKey'
                  );
                """,
            problemTag: "template_unknown_key",
            problems,
            buildIssue: (id, displayName, templateKey) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateUnknownKey + id,
                Title: $"Menu item '{displayName}' references unknown template '{templateKey}'",
                Summary: $"config.templateKey '{templateKey}' has no matching row in page_templates. The SPA renders an unknown-template stub. Either register the template or correct the key.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, templateKey, problem = "template_unknown_key", source = "spa_render_report" })));

        matched += await ScanOneAsync(connection, menuItemId, cancellationToken,
            sql: """
                SELECT id, display_name FROM menu_items
                WHERE id = @id
                  AND item_type = 'route'
                  AND COALESCE(config->>'path', '') = ''
                  AND COALESCE(config->>'aliasPath', '') = '';
                """,
            problemTag: "route_missing_path",
            problems,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintRouteMissingPath + id,
                Title: $"Menu item '{displayName}' is type=route but has no path or aliasPath",
                Summary: "Route items need either config.path (for direct routes) or config.aliasPath (for alias routes). Without either, the SPA's nav can't link the item.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "route_missing_path", source = "spa_render_report" })));

        return new RealtimeScanResult(matched, problems);
    }

    private async Task<int> ScanOneAsync(
        NpgsqlConnection connection,
        Guid menuItemId,
        CancellationToken cancellationToken,
        string sql,
        string problemTag,
        List<string> problems,
        Func<Guid, string, SystemIssueDraft> buildIssue)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", menuItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return 0;
        var id = reader.GetGuid(0);
        var displayName = await reader.IsDBNullAsync(1, cancellationToken) ? "(unnamed)" : reader.GetString(1);
        await reader.CloseAsync();
        await recorder.RecordAsync(buildIssue(id, displayName), cancellationToken);
        problems.Add(problemTag);
        return 1;
    }

    private async Task<int> ScanOneAsync(
        NpgsqlConnection connection,
        Guid menuItemId,
        CancellationToken cancellationToken,
        string sql,
        string problemTag,
        List<string> problems,
        Func<Guid, string, string, SystemIssueDraft> buildIssue)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", menuItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return 0;
        var id = reader.GetGuid(0);
        var displayName = await reader.IsDBNullAsync(1, cancellationToken) ? "(unnamed)" : reader.GetString(1);
        var extra = await reader.IsDBNullAsync(2, cancellationToken) ? "" : reader.GetString(2);
        await reader.CloseAsync();
        await recorder.RecordAsync(buildIssue(id, displayName, extra), cancellationToken);
        problems.Add(problemTag);
        return 1;
    }

    private Task ScanTemplateMissingTemplateKeyAsync(
        NpgsqlConnection connection, HashSet<string> seenThisTick, CancellationToken cancellationToken) =>
        ScanAsync(connection, seenThisTick, cancellationToken,
            sql: """
                SELECT id, display_name
                FROM menu_items
                WHERE item_type = 'template'
                  AND COALESCE(config->>'templateKey', '') = ''
                LIMIT @batch;
                """,
            fingerprintPrefix: FingerprintTemplateMissingTemplateKey,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateMissingTemplateKey + id,
                Title: $"Menu item '{displayName}' is type=template but has no templateKey",
                Summary: "The SPA can't pick a React component to render; the item is silently dropped from the nav. Set config.templateKey to a registered page template id.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "template_missing_key" })));

    private Task ScanTemplateMissingPathAsync(
        NpgsqlConnection connection, HashSet<string> seenThisTick, CancellationToken cancellationToken) =>
        ScanAsync(connection, seenThisTick, cancellationToken,
            sql: """
                SELECT id, display_name
                FROM menu_items
                WHERE item_type = 'template'
                  AND COALESCE(config->>'templateKey', '') <> ''
                  AND COALESCE(config->>'path', '') = ''
                LIMIT @batch;
                """,
            fingerprintPrefix: FingerprintTemplateMissingPath,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateMissingPath + id,
                Title: $"Menu item '{displayName}' is type=template but has no path",
                Summary: "Template items must own their URL via config.path. The SPA's nav silently drops items whose resolved path is null. Set config.path to the URL the template should mount under.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "template_missing_path" })));

    private Task ScanTemplateUnknownKeyAsync(
        NpgsqlConnection connection, HashSet<string> seenThisTick, CancellationToken cancellationToken) =>
        ScanAsync(connection, seenThisTick, cancellationToken,
            sql: """
                SELECT mi.id, mi.display_name, mi.config->>'templateKey' AS template_key
                FROM menu_items mi
                WHERE mi.item_type = 'template'
                  AND COALESCE(mi.config->>'templateKey', '') <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM page_templates pt WHERE pt.key = mi.config->>'templateKey'
                  )
                LIMIT @batch;
                """,
            fingerprintPrefix: FingerprintTemplateUnknownKey,
            buildIssue: (id, displayName, templateKey) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintTemplateUnknownKey + id,
                Title: $"Menu item '{displayName}' references unknown template '{templateKey}'",
                Summary: $"config.templateKey '{templateKey}' has no matching row in page_templates. The SPA renders an unknown-template stub. Either register the template or correct the key.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, templateKey, problem = "template_unknown_key" })));

    private Task ScanRouteMissingPathAsync(
        NpgsqlConnection connection, HashSet<string> seenThisTick, CancellationToken cancellationToken) =>
        ScanAsync(connection, seenThisTick, cancellationToken,
            sql: """
                SELECT id, display_name
                FROM menu_items
                WHERE item_type = 'route'
                  AND COALESCE(config->>'path', '') = ''
                  AND COALESCE(config->>'aliasPath', '') = ''
                LIMIT @batch;
                """,
            fingerprintPrefix: FingerprintRouteMissingPath,
            buildIssue: (id, displayName) => new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.DataIntegrity,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: FingerprintRouteMissingPath + id,
                Title: $"Menu item '{displayName}' is type=route but has no path or aliasPath",
                Summary: "Route items need either config.path (for direct routes) or config.aliasPath (for alias routes). Without either, the SPA's nav can't link the item.",
                RelatedEntityKind: "menu_item",
                RelatedEntityId: id.ToString(),
                FactsJson: JsonSerializer.Serialize(new { menuItemId = id, displayName, problem = "route_missing_path" })));

    private async Task ScanAsync(
        NpgsqlConnection connection,
        HashSet<string> seenThisTick,
        CancellationToken cancellationToken,
        string sql,
        string fingerprintPrefix,
        Func<Guid, string, SystemIssueDraft> buildIssue)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch", _menuOptions.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var displayName = await reader.IsDBNullAsync(1, cancellationToken) ? "(unnamed)" : reader.GetString(1);
            var fingerprint = fingerprintPrefix + id;
            seenThisTick.Add(fingerprint);
            await recorder.RecordAsync(buildIssue(id, displayName), cancellationToken);
        }
    }

    // Three-arg overload for scans that need an extra projected column
    // (e.g. the unknown-templateKey case carries the offending key).
    private async Task ScanAsync(
        NpgsqlConnection connection,
        HashSet<string> seenThisTick,
        CancellationToken cancellationToken,
        string sql,
        string fingerprintPrefix,
        Func<Guid, string, string, SystemIssueDraft> buildIssue)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch", _menuOptions.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var displayName = await reader.IsDBNullAsync(1, cancellationToken) ? "(unnamed)" : reader.GetString(1);
            var extra = await reader.IsDBNullAsync(2, cancellationToken) ? "" : reader.GetString(2);
            var fingerprint = fingerprintPrefix + id;
            seenThisTick.Add(fingerprint);
            await recorder.RecordAsync(buildIssue(id, displayName, extra), cancellationToken);
        }
    }
}

// Returned from MisconfiguredMenuItemDetector.ScanItemAsync. `Matched` is
// the count of distinct problem classes the row tripped (0 means the SPA's
// report doesn't reproduce — likely a stale render against a row that's
// since been fixed). `Problems` carries the matched class tags so the API
// can echo them back to the SPA for confirmation.
public sealed record RealtimeScanResult(int Matched, IReadOnlyList<string> Problems);

public sealed class MisconfiguredMenuItemDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:MisconfiguredMenuItem";

    // Menu items change rarely; 30 minutes is plenty.
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    // Per-class cap so a misconfigured plugin that drops hundreds of bad
    // menu items at once doesn't open thousands of issues in one tick.
    public int BatchSize { get; set; } = 100;
}
