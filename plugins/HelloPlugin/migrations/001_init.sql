-- HelloPlugin's own table. Lives inside its plg_<code> schema; the host applies
-- this once on the first enable after upload and tracks it in __plugin_migrations
-- so subsequent enables skip it.
CREATE TABLE IF NOT EXISTS greetings (
    id BIGSERIAL PRIMARY KEY,
    saw_action TEXT NOT NULL,
    saw_kind TEXT NOT NULL,
    saw_id TEXT NOT NULL,
    saw_effect TEXT NOT NULL,
    seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
