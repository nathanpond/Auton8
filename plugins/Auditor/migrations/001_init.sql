-- Auditor plugin storage. Both tables live in the plugin's own plg_<code>
-- schema; the host applies this once on the first enable after upload and
-- tracks it in __plugin_migrations so subsequent enables skip it.

-- Single-row settings blob, populated by the Auditor Settings page through
-- the host's generic /api/admin/plugins/by-code/{code}/settings endpoint.
-- Defaults match the page's "off-by-default" stance: collection disabled,
-- 7-day retention, unsuccessful access ignored.
CREATE TABLE IF NOT EXISTS plugin_settings_kv (
    id INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    settings_json JSONB NOT NULL DEFAULT
        '{"collect": false, "retentionDays": 7, "trackUnsuccessful": false}'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO plugin_settings_kv (id) VALUES (1)
ON CONFLICT (id) DO NOTHING;

-- The audit log itself. Denormalized for cheap reads in the admin UI; the
-- full envelope is also stored as JSONB so consumers can reconstruct anything
-- the host originally published.
--
-- resource_id and resource_label promote the most useful fields out of
-- envelope.resource so the AuditLog page doesn't have to parse JSON in JS.
-- They live here in 001 because Auditor.cs INSERTs them unconditionally and
-- splitting them across migrations creates a window where the plugin is
-- enabled but the columns don't exist yet. Migration 002 keeps the same
-- ALTER TABLE IF NOT EXISTS so existing deployments are no-ops.
CREATE TABLE IF NOT EXISTS audit_log (
    id BIGSERIAL PRIMARY KEY,
    event_id UUID NOT NULL,
    event_type TEXT NOT NULL,
    topic_name TEXT NOT NULL,
    resource_kind TEXT NOT NULL,
    resource_id TEXT NULL,
    resource_label TEXT NULL,
    actor_id UUID NULL,
    actor_user_name TEXT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    request_id TEXT NOT NULL,
    correlation_id TEXT NULL,
    ip_address TEXT NOT NULL,
    user_agent TEXT NOT NULL,
    source_app_id TEXT NOT NULL,
    http_method TEXT NOT NULL,
    route_path TEXT NOT NULL,
    auth_outcome TEXT NOT NULL,
    auth_decision_reason TEXT NULL,
    envelope JSONB NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_audit_log_occurred_at ON audit_log (occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_log_event_type ON audit_log (event_type);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor_id ON audit_log (actor_id);
CREATE INDEX IF NOT EXISTS ix_audit_log_resource_id
    ON audit_log (resource_id)
    WHERE resource_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_log_event_id ON audit_log (event_id);
