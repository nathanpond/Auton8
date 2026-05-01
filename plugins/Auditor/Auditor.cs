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

    public void Configure(IPluginContext context)
    {
        var logger = context.HostServices
            .GetService<ILoggerFactory>()
            ?.CreateLogger("Auditor");

        RegisterSettingsMenu(context, logger);

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
                        actor_id, actor_user_name, occurred_at,
                        request_id, correlation_id, ip_address, user_agent,
                        source_app_id, http_method, route_path,
                        auth_outcome, auth_decision_reason, envelope
                    ) VALUES (
                        @eventId, @eventType, @topicName, @resourceKind,
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
