-- Capture an identifier for the resource each audit event touched, parsed
-- by the host's DaprAuditEventPublisher from envelope.resource. The full
-- envelope is still kept verbatim in `envelope` JSONB; these columns just
-- promote the most useful fields to first-class so the AuditLog page can
-- show "what was accessed" without parsing JSON in JavaScript.
ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS resource_id TEXT NULL,
    ADD COLUMN IF NOT EXISTS resource_label TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_audit_log_resource_id
    ON audit_log (resource_id)
    WHERE resource_id IS NOT NULL;
