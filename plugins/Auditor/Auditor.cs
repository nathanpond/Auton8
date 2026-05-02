using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoNate.Plugins.Auditor;

// Listens for every audit-grade event the host publishes (each access, deny,
// and mutation flows over HookPoints.AuditEventPublished) and persists those
// it's configured to keep into the plugin's own plg_<code>.audit_log table.
//
// Behavior is entirely settings-driven via the JSX settings page registered
// under "Plugins" in Site Configuration. Defaults match the off-by-default
// stance: collection disabled, 7-day retention, unsuccessful access ignored.
public sealed class Auditor : IAutoNatePlugin
{
    public string Name => "Auditor";
    public string Version => "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    // Pruning is best-effort: at most one prune per minute, triggered off the
    // hook handler so we don't need a separate timer/background thread (which
    // would outlive the plugin's load context).
    private static readonly TimeSpan MinPruneInterval = TimeSpan.FromMinutes(1);

    // Template key the AuditLog page template registers as (matches the
    // PageTemplates/AuditLog.template filename stem).
    private const string AuditLogTemplateKey = "AuditLog";

    public void Configure(IPluginContext context)
    {
        var logger = context.HostServices
            .GetService<ILoggerFactory>()
            ?.CreateLogger("Auditor");

        RegisterSettingsMenu(context, logger);
        RegisterAuditLogMenuItem(context, logger);

        var state = new AuditorState(context.Data, logger);

        context.Hooks.AddActionAsync(
            HookPoints.AuditEventPublished,
            priority: 100,
            async (args, ct) =>
            {
                if (args.Length == 0 || args[0] is not AuditEventNotification notification)
                {
                    return;
                }
                await state.HandleAsync(notification, ct).ConfigureAwait(false);
            });

        // Per-plugin data hook backing the AuditLog page template's
        // /api/admin/plugins/by-code/{code}/data/audit-log calls.
        context.Hooks.AddFilterAsync<PluginDataResponse>(
            HookPoints.PluginDataHookFor(context.Code),
            priority: 100,
            async (current, args, ct) =>
            {
                if (args.Length == 0 || args[0] is not PluginDataRequest req) return current;
                if (!string.Equals(req.View, "audit-log", StringComparison.Ordinal)) return current;
                return await state.QueryAuditLogAsync(req, ct).ConfigureAwait(false);
            });
    }

    public void Cleanup(IPluginContext context)
    {
        var logger = context.HostServices
            .GetService<ILoggerFactory>()
            ?.CreateLogger("Auditor");

        // Remove the AuditLog item from the icon menu's Settings group, then
        // — per the plugin spec — sweep a trailing separator we own if it's
        // still alone at the bottom. RemoveMenuItem is ownership-checked, so
        // pre-existing admin separators stay put.
        try
        {
            var settings = FindSettingsGroupChildren(context);
            if (settings is not null)
            {
                var (settingsId, children) = settings.Value;

                // Drop our AuditLog menu item.
                var ownedItem = children.FirstOrDefault(c =>
                    c.CreatedByPluginId == context.PluginId
                    && c.ItemType == "template"
                    && ReadConfigString(c.ConfigJson, "templateKey") == AuditLogTemplateKey);
                if (ownedItem is not null)
                {
                    context.Menus.RemoveMenuItem(ownedItem.Id);
                }

                // Re-snapshot — children no longer contains the AuditLog row
                // we just removed. If the new bottom is a separator we added,
                // remove it too so the gear menu doesn't end with a dangling
                // divider.
                var refreshed = FindSettingsGroupChildren(context);
                if (refreshed is not null)
                {
                    var trailing = refreshed.Value.Children.LastOrDefault();
                    if (trailing is not null
                        && trailing.ItemType == "separator"
                        && trailing.CreatedByPluginId == context.PluginId)
                    {
                        context.Menus.RemoveMenuItem(trailing.Id);
                    }
                }
            }

            // Sweep any other items we still own (Auditor Settings page,
            // a separator we added but somehow couldn't remove above, etc.).
            var removed = context.Menus.RemoveAll();
            logger?.LogInformation("Auditor cleanup removed {Count} remaining menu item(s).", removed);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Auditor cleanup failed while removing menu items.");
        }

        // The audit_log and plugin_settings_kv tables live in the plugin's own
        // plg_<code> schema, which the host DROP SCHEMA CASCADEs immediately
        // after this call returns. Same for the AuditLog page_templates row —
        // the FK CASCADE on plugins.id sweeps it. No explicit work needed.
    }

    private static void RegisterSettingsMenu(IPluginContext context, ILogger? logger)
    {
        try
        {
            context.Menus.AddPluginMenuItem(new NewMenuItem(
                DisplayName: "Auditor Settings",
                ItemType: "page",
                Icon: "fa fa-shield-alt",
                Config: new
                {
                    path = $"/admin/config/plugins/{context.Code}/auditor-settings",
                    contentType = "jsx",
                    content = SettingsJsx,
                }));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Auditor failed to register its settings menu item.");
        }
    }

    // Append the AuditLog template item under the icon menu's "Settings"
    // (gear) group, putting a separator above it first when the group's
    // current bottom is something other than a separator. This keeps the
    // pre-existing items visually distinct from plugin-injected items
    // without piling up consecutive dividers across multiple plugins.
    private static void RegisterAuditLogMenuItem(IPluginContext context, ILogger? logger)
    {
        try
        {
            var found = FindSettingsGroupChildren(context);
            if (found is null)
            {
                logger?.LogWarning(
                    "Auditor: 'Settings' group not found in icon menu; skipping AuditLog menu registration.");
                return;
            }
            var (settingsId, children) = found.Value;

            var lastChild = children.LastOrDefault();
            if (lastChild is null || lastChild.ItemType != "separator")
            {
                context.Menus.AddMenuItem("icon", settingsId, new NewMenuItem(
                    DisplayName: string.Empty,
                    ItemType: "separator",
                    Icon: null,
                    Config: null,
                    SortOrder: null,
                    IsVisible: true));
            }

            // Path lines up with the AuditLog template's auto-generated
            // default_path: /plugins/<code>/auditlog. Lower-case for stability.
            var path = $"/plugins/{context.Code}/auditlog";
            context.Menus.AddMenuItem("icon", settingsId, new NewMenuItem(
                DisplayName: "Audit Log",
                ItemType: "template",
                Icon: "fa fa-clipboard-list",
                Config: new { templateKey = AuditLogTemplateKey, path },
                SortOrder: null,
                IsVisible: true));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Auditor failed to register the AuditLog menu item.");
        }
    }

    // Locate the "Settings" group inside the icon menu and return its id +
    // children sorted by sort_order. Returns null when the icon menu or
    // group can't be found (e.g. a customized install that renamed/removed
    // the group). Comparing the icon helps survive a display-name rename.
    private static (Guid SettingsId, IReadOnlyList<MenuItemInfo> Children)? FindSettingsGroupChildren(
        IPluginContext context)
    {
        var menus = context.Menus.ListMenus();
        var icon = menus.FirstOrDefault(m => m.Key == "icon");
        if (icon is null) return null;

        var settings = icon.Items.FirstOrDefault(i =>
            i.ParentId is null
            && i.ItemType == "group"
            && (string.Equals(i.DisplayName, "Settings", StringComparison.Ordinal)
                || (i.Icon is not null && i.Icon.Contains("fa-gear", StringComparison.Ordinal))));
        if (settings is null) return null;

        var children = icon.Items
            .Where(i => i.ParentId == settings.Id)
            .OrderBy(i => i.SortOrder)
            .ToList();
        return (settings.Id, children);
    }

    private static string? ReadConfigString(string configJson, string field)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty(field, out var v)
                   && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Serialized config persisted in plg_<code>.plugin_settings_kv.
    private sealed record AuditorSettings(
        bool Collect,
        int RetentionDays,
        bool TrackUnsuccessful);

    private sealed class AuditorState
    {
        private readonly IPluginDataAccess _data;
        private readonly ILogger? _logger;
        private DateTime _nextPruneUtc = DateTime.MinValue;

        public AuditorState(IPluginDataAccess data, ILogger? logger)
        {
            _data = data;
            _logger = logger;
        }

        public async Task HandleAsync(AuditEventNotification notification, CancellationToken ct)
        {
            AuditorSettings settings;
            try
            {
                settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Auditor failed to load settings; skipping event {EventId}.", notification.EventId);
                return;
            }

            if (!settings.Collect) return;

            // Honor the "track unsuccessful" toggle. Unsuccessful = anything
            // not Allowed (Denied + Anonymous), since both indicate an attempt
            // to reach a resource that didn't go through.
            var isUnsuccessful = notification.AuthOutcome != AuditAuthOutcomeDto.Allowed;
            if (isUnsuccessful && !settings.TrackUnsuccessful) return;

            try
            {
                await _data.ExecuteAsync(
                    """
                    INSERT INTO audit_log (
                        event_id, event_type, topic_name, resource_kind,
                        resource_id, resource_label,
                        actor_id, actor_user_name, occurred_at,
                        request_id, correlation_id, ip_address, user_agent,
                        source_app_id, http_method, route_path,
                        auth_outcome, auth_decision_reason, envelope
                    ) VALUES (
                        @eventId, @eventType, @topicName, @resourceKind,
                        @resourceId, @resourceLabel,
                        @actorId, @actorUserName, @occurredAt,
                        @requestId, @correlationId, @ipAddress, @userAgent,
                        @sourceAppId, @httpMethod, @routePath,
                        @authOutcome, @authDecisionReason, @envelope::jsonb
                    )
                    ON CONFLICT (event_id) DO NOTHING;
                    """,
                    new
                    {
                        eventId = notification.EventId,
                        eventType = notification.EventType,
                        topicName = notification.TopicName,
                        resourceKind = notification.ResourceKind,
                        resourceId = (object?)notification.ResourceId ?? DBNull.Value,
                        resourceLabel = (object?)notification.ResourceLabel ?? DBNull.Value,
                        actorId = (object?)notification.ActorId ?? DBNull.Value,
                        actorUserName = (object?)notification.ActorUserName ?? DBNull.Value,
                        occurredAt = notification.OccurredAtUtc.UtcDateTime,
                        requestId = notification.RequestId,
                        correlationId = (object?)notification.CorrelationId ?? DBNull.Value,
                        ipAddress = notification.IpAddress,
                        userAgent = notification.UserAgent,
                        sourceAppId = notification.SourceAppId,
                        httpMethod = notification.HttpMethod,
                        routePath = notification.RoutePath,
                        authOutcome = notification.AuthOutcome.ToString(),
                        authDecisionReason = (object?)notification.AuthDecisionReason ?? DBNull.Value,
                        envelope = notification.EnvelopeJson,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Auditor failed to record event {EventId}.", notification.EventId);
                return;
            }

            await TryPruneAsync(settings.RetentionDays, ct).ConfigureAwait(false);
        }

        private async Task<AuditorSettings> LoadSettingsAsync(CancellationToken ct)
        {
            var json = await _data.QuerySingleOrDefaultAsync<string>(
                "SELECT settings_json::text FROM plugin_settings_kv WHERE id = 1 LIMIT 1;",
                ct: ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new AuditorSettings(Collect: false, RetentionDays: 7, TrackUnsuccessful: false);
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var collect = root.TryGetProperty("collect", out var c) && c.ValueKind == JsonValueKind.True;
                var retention = root.TryGetProperty("retentionDays", out var r)
                                && r.ValueKind == JsonValueKind.Number
                                && r.TryGetInt32(out var days) && days > 0
                    ? days
                    : 7;
                var unsuccessful = root.TryGetProperty("trackUnsuccessful", out var u)
                                   && u.ValueKind == JsonValueKind.True;
                return new AuditorSettings(collect, retention, unsuccessful);
            }
            catch (JsonException)
            {
                return new AuditorSettings(Collect: false, RetentionDays: 7, TrackUnsuccessful: false);
            }
        }

        private async Task TryPruneAsync(int retentionDays, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (now < _nextPruneUtc) return;
            _nextPruneUtc = now + MinPruneInterval;

            try
            {
                await _data.ExecuteAsync(
                    "DELETE FROM audit_log WHERE occurred_at < @cutoff;",
                    new { cutoff = now.AddDays(-retentionDays) },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Auditor retention prune failed.");
            }
        }

        // Backs the AuditLog page template's data fetch. Returns the most
        // recent rows ordered by occurred_at DESC plus the total count, so
        // the table can render pagination controls.
        public async Task<PluginDataResponse> QueryAuditLogAsync(
            PluginDataRequest req, CancellationToken ct)
        {
            const int DefaultLimit = 50;
            const int MaxLimit = 500;

            int limit = DefaultLimit;
            int offset = 0;
            if (req.Query.TryGetValue("limit", out var rawLimit)
                && int.TryParse(rawLimit, out var parsedLimit) && parsedLimit > 0)
            {
                limit = Math.Min(parsedLimit, MaxLimit);
            }
            if (req.Query.TryGetValue("offset", out var rawOffset)
                && int.TryParse(rawOffset, out var parsedOffset) && parsedOffset >= 0)
            {
                offset = parsedOffset;
            }

            try
            {
                var rows = await _data.QueryAsync<AuditLogRow>(
                    """
                    SELECT
                        id              AS Id,
                        event_id        AS EventId,
                        event_type      AS EventType,
                        topic_name      AS TopicName,
                        resource_kind   AS ResourceKind,
                        resource_id     AS ResourceId,
                        resource_label  AS ResourceLabel,
                        actor_id        AS ActorId,
                        actor_user_name AS ActorUserName,
                        occurred_at     AS OccurredAt,
                        request_id      AS RequestId,
                        correlation_id  AS CorrelationId,
                        ip_address      AS IpAddress,
                        user_agent      AS UserAgent,
                        source_app_id   AS SourceAppId,
                        http_method     AS HttpMethod,
                        route_path      AS RoutePath,
                        auth_outcome    AS AuthOutcome,
                        auth_decision_reason AS AuthDecisionReason
                    FROM audit_log
                    ORDER BY occurred_at DESC, id DESC
                    LIMIT @limit OFFSET @offset;
                    """,
                    new { limit, offset },
                    ct).ConfigureAwait(false);

                var total = await _data.QuerySingleOrDefaultAsync<long>(
                    "SELECT COUNT(*) FROM audit_log;", ct: ct).ConfigureAwait(false);

                var json = JsonSerializer.Serialize(new { rows, total, limit, offset }, JsonOptions);
                return new PluginDataResponse
                {
                    StatusCode = 200,
                    ContentJson = json,
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Auditor failed to query audit_log.");
                var errJson = JsonSerializer.Serialize(new { error = "audit-log query failed" }, JsonOptions);
                return new PluginDataResponse
                {
                    StatusCode = 500,
                    ContentJson = errJson,
                };
            }
        }
    }

    // Row shape returned to the AuditLog page template. Field names are
    // Pascal-cased to match the SQL aliases above; System.Text.Json's web
    // defaults camelCase them on the wire.
    private sealed class AuditLogRow
    {
        public long Id { get; set; }
        public Guid EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string TopicName { get; set; } = string.Empty;
        public string ResourceKind { get; set; } = string.Empty;
        public string? ResourceId { get; set; }
        public string? ResourceLabel { get; set; }
        public Guid? ActorId { get; set; }
        public string? ActorUserName { get; set; }
        public DateTime OccurredAt { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string SourceAppId { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public string RoutePath { get; set; } = string.Empty;
        public string AuthOutcome { get; set; } = string.Empty;
        public string? AuthDecisionReason { get; set; }
    }

    // JSX page rendered via the host's JsxPage component. Reads/writes settings
    // through the host's generic plugin-settings KV endpoint, which proxies to
    // plg_<code>.plugin_settings_kv. The path uses a literal {context.Code}
    // segment rendered into the closing settings link so the page can derive
    // its own code from the URL.
    private const string SettingsJsx = """
        function Page() {
          const [loading, setLoading] = useState(true);
          const [saving, setSaving] = useState(false);
          const [error, setError] = useState(null);
          const [savedAt, setSavedAt] = useState(null);
          const [collect, setCollect] = useState(false);
          const [retentionDays, setRetentionDays] = useState(7);
          const [trackUnsuccessful, setTrackUnsuccessful] = useState(false);

          // Derive the plugin code from the current admin URL:
          // /admin/config/plugins/{code}/auditor-settings
          const code = useMemo(() => {
            const m = window.location.pathname.match(/\/admin\/config\/plugins\/([a-z][a-z0-9]{7})\//);
            return m ? m[1] : null;
          }, []);

          useEffect(() => {
            if (!code) {
              setError("Could not infer plugin code from URL.");
              setLoading(false);
              return;
            }
            let cancelled = false;
            setLoading(true);
            api.get("/api/admin/plugins/by-code/" + code + "/settings")
              .then((res) => {
                if (cancelled) return;
                const body = (res && res.data) || {};
                const s = (body && body.settings) || {};
                if (typeof s.collect === "boolean") setCollect(s.collect);
                if (typeof s.retentionDays === "number" && s.retentionDays > 0) setRetentionDays(s.retentionDays);
                if (typeof s.trackUnsuccessful === "boolean") setTrackUnsuccessful(s.trackUnsuccessful);
                setError(null);
              })
              .catch((err) => {
                if (cancelled) return;
                setError((err && err.message) || String(err));
              })
              .finally(() => {
                if (!cancelled) setLoading(false);
              });
            return () => { cancelled = true; };
          }, [code]);

          const save = useCallback(() => {
            if (!code) return;
            setSaving(true);
            setError(null);
            const payload = {
              settings: {
                collect: !!collect,
                retentionDays: Math.max(1, Math.floor(Number(retentionDays) || 7)),
                trackUnsuccessful: !!trackUnsuccessful,
              },
            };
            api.put("/api/admin/plugins/by-code/" + code + "/settings", payload)
              .then((res) => {
                const body = (res && res.data) || {};
                const s = (body && body.settings) || payload.settings;
                if (typeof s.collect === "boolean") setCollect(s.collect);
                if (typeof s.retentionDays === "number") setRetentionDays(s.retentionDays);
                if (typeof s.trackUnsuccessful === "boolean") setTrackUnsuccessful(s.trackUnsuccessful);
                setSavedAt(new Date().toLocaleTimeString());
              })
              .catch((err) => setError((err && err.message) || String(err)))
              .finally(() => setSaving(false));
          }, [code, collect, retentionDays, trackUnsuccessful]);

          return (
            <>
              <div className="page-head">
                <h1 className="page-header mb-1">Auditor Settings</h1>
                <p className="page-head-copy">
                  Capture every audit-grade event the host publishes (data access, denials,
                  mutations) into the plugin's own log table. Off by default — flip
                  <em> Collect Audit Logs </em> on to start recording.
                </p>
              </div>

              {error && (
                <div className="alert alert-danger" role="alert">
                  <strong>Error:</strong> {error}
                </div>
              )}

              {loading ? (
                <div className="text-muted">
                  <i className="fa fa-spinner fa-spin me-2" /> Loading settings…
                </div>
              ) : (
                <div className="panel panel-inverse">
                  <div className="panel-heading">
                    <h4 className="panel-title">Configuration</h4>
                  </div>
                  <div className="panel-body">

                    <div className="form-check form-switch mb-3">
                      <input
                        className="form-check-input"
                        type="checkbox"
                        role="switch"
                        id="auditor-collect"
                        checked={collect}
                        onChange={(e) => setCollect(e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="auditor-collect">
                        <strong>Collect Audit Logs</strong>
                      </label>
                      <div className="form-text">
                        When off, no events are written. The hook still fires but the
                        Auditor short-circuits before touching the database.
                      </div>
                    </div>

                    <div className="form-check form-switch mb-3">
                      <input
                        className="form-check-input"
                        type="checkbox"
                        role="switch"
                        id="auditor-unsuccessful"
                        checked={trackUnsuccessful}
                        onChange={(e) => setTrackUnsuccessful(e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="auditor-unsuccessful">
                        <strong>Track unsuccessful data requests</strong>
                      </label>
                      <div className="form-text">
                        Includes denied and anonymous events. Off keeps only events the
                        authorization layer allowed.
                      </div>
                    </div>

                    <div className="mb-3" style={{ maxWidth: "16rem" }}>
                      <label htmlFor="auditor-retention" className="form-label">
                        <strong>History retention (days)</strong>
                      </label>
                      <input
                        id="auditor-retention"
                        type="number"
                        min="1"
                        className="form-control form-control-sm"
                        value={retentionDays}
                        onChange={(e) => setRetentionDays(e.target.value)}
                      />
                      <div className="form-text">
                        Rows older than this many days are pruned opportunistically as
                        new events arrive.
                      </div>
                    </div>

                    <div className="d-flex align-items-center gap-2">
                      <button
                        type="button"
                        className="btn btn-primary"
                        onClick={save}
                        disabled={saving || !code}
                      >
                        {saving ? (
                          <><i className="fa fa-spinner fa-spin me-2" /> Saving…</>
                        ) : "Save settings"}
                      </button>
                      {savedAt && !saving && (
                        <span className="text-success small">
                          <i className="fa fa-check me-1" /> Saved at {savedAt}
                        </span>
                      )}
                    </div>

                  </div>
                </div>
              )}
            </>
          );
        }
        """;
}
