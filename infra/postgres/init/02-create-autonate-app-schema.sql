CREATE TABLE IF NOT EXISTS local_users (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    email TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    user_id UUID NOT NULL UNIQUE,
    created_date TIMESTAMPTZ NOT NULL,
    last_login_date TIMESTAMPTZ NULL,
    idp_key TEXT NOT NULL UNIQUE
);

CREATE INDEX IF NOT EXISTS ix_local_users_username
    ON local_users (username);

CREATE TABLE IF NOT EXISTS workflow_models (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    process_key TEXT NOT NULL UNIQUE,
    bpmn_xml TEXT NOT NULL,
    is_draft BOOLEAN NOT NULL DEFAULT TRUE,
    draft_version_number INTEGER NOT NULL DEFAULT 1,
    published_version_number INTEGER NULL,
    active_process_instance_id TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    last_deployment_id TEXT NULL,
    last_process_definition_id TEXT NULL,
    last_process_definition_key TEXT NULL,
    last_process_definition_version INTEGER NULL,
    last_deployed_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_workflow_models_updated_at_utc
    ON workflow_models (updated_at_utc DESC);

ALTER TABLE workflow_models
    ADD COLUMN IF NOT EXISTS is_draft BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE workflow_models
    ADD COLUMN IF NOT EXISTS draft_version_number INTEGER NOT NULL DEFAULT 1;

ALTER TABLE workflow_models
    ADD COLUMN IF NOT EXISTS published_version_number INTEGER NULL;

CREATE TABLE IF NOT EXISTS workflow_model_versions (
    id UUID PRIMARY KEY,
    workflow_model_id UUID NOT NULL REFERENCES workflow_models (id) ON DELETE CASCADE,
    version_number INTEGER NOT NULL,
    name TEXT NOT NULL,
    process_key TEXT NOT NULL,
    bpmn_xml TEXT NOT NULL,
    deployment_id TEXT NOT NULL,
    process_definition_id TEXT NOT NULL,
    process_definition_key TEXT NOT NULL,
    process_definition_version INTEGER NOT NULL,
    published_at_utc TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS workflow_model_versions_workflow_model_id_version_number_key
    ON workflow_model_versions (workflow_model_id, version_number);

CREATE INDEX IF NOT EXISTS ix_workflow_model_versions_workflow_model_id
    ON workflow_model_versions (workflow_model_id);

-- =============================================================================
-- Record management framework
--   record_types             - user-defined record type definitions
--   record_type_fields       - fields attached to each record type
--   record_type_audit_log    - audit of schema changes (type/field lifecycle)
-- Record / edge / comment tables are added in later phases.
-- =============================================================================

CREATE TABLE IF NOT EXISTS record_types (
    id UUID PRIMARY KEY,
    short_code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT NULL,
    icon TEXT NULL,
    color TEXT NULL,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    next_key_number BIGINT NOT NULL DEFAULT 1,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_record_types_updated_at_utc
    ON record_types (updated_at_utc DESC);

CREATE TABLE IF NOT EXISTS record_type_fields (
    id UUID PRIMARY KEY,
    record_type_id UUID NOT NULL REFERENCES record_types (id) ON DELETE CASCADE,
    field_key TEXT NOT NULL,
    display_name TEXT NOT NULL,
    data_type TEXT NOT NULL,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_required BOOLEAN NOT NULL DEFAULT FALSE,
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    UNIQUE (record_type_id, field_key)
);

CREATE INDEX IF NOT EXISTS ix_record_type_fields_record_type_id
    ON record_type_fields (record_type_id, sort_order);

CREATE TABLE IF NOT EXISTS record_type_audit_log (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    record_type_id UUID NOT NULL,
    change_kind TEXT NOT NULL,
    before JSONB NULL,
    after JSONB NULL,
    changed_by UUID NOT NULL,
    changed_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_record_type_audit_log_record_type_id
    ON record_type_audit_log (record_type_id, changed_at_utc DESC);

-- Records: instance data for each record type. Custom field values live in `values`
-- JSONB; default fields are top-level columns for fast access and indexing.
CREATE TABLE IF NOT EXISTS records (
    id UUID PRIMARY KEY,
    record_type_id UUID NOT NULL REFERENCES record_types (id) ON DELETE RESTRICT,
    key TEXT NOT NULL UNIQUE,
    key_number BIGINT NOT NULL,
    name TEXT NOT NULL,
    assignee_ids UUID[] NOT NULL DEFAULT '{}',
    status TEXT NULL,
    due_date DATE NULL,
    values JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_records_record_type_id
    ON records (record_type_id);

CREATE INDEX IF NOT EXISTS ix_records_values_gin
    ON records USING GIN (values jsonb_path_ops);

CREATE INDEX IF NOT EXISTS ix_records_assignee_ids_gin
    ON records USING GIN (assignee_ids);

CREATE INDEX IF NOT EXISTS ix_records_type_updated_active
    ON records (record_type_id, updated_at_utc DESC)
    WHERE is_archived = FALSE;

CREATE INDEX IF NOT EXISTS ix_records_type_updated_archived
    ON records (record_type_id, updated_at_utc DESC)
    WHERE is_archived = TRUE;

CREATE INDEX IF NOT EXISTS ix_records_created_by
    ON records (created_by, updated_at_utc DESC)
    WHERE is_archived = FALSE;

-- Per-record change history. Every mutation writes one row per changed field
-- (or per top-level change like name/archive/assignees) in the same transaction
-- as the mutation. This is the authoritative source of "history of record X".
CREATE TABLE IF NOT EXISTS record_field_changes (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    record_id UUID NOT NULL,
    change_set_id UUID NULL,
    change_kind TEXT NOT NULL,
    field_key TEXT NULL,
    old_value JSONB NULL,
    new_value JSONB NULL,
    changed_by UUID NOT NULL,
    changed_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_record_field_changes_record
    ON record_field_changes (record_id, changed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_record_field_changes_record_field
    ON record_field_changes (record_id, field_key, changed_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_record_field_changes_change_set
    ON record_field_changes (change_set_id);

-- Edge types: configurable relationship definitions between records.
-- from_record_type_ids / to_record_type_ids: NULL means "any record type".
-- cardinality + allow_self_reference are enforced in the application layer.
CREATE TABLE IF NOT EXISTS record_edge_types (
    id UUID PRIMARY KEY,
    short_code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    inverse_name TEXT NULL,
    is_directed BOOLEAN NOT NULL DEFAULT TRUE,
    allow_self_reference BOOLEAN NOT NULL DEFAULT FALSE,
    cardinality TEXT NOT NULL DEFAULT 'many_to_many',
    from_record_type_ids UUID[] NULL,
    to_record_type_ids UUID[] NULL,
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS record_edge_type_fields (
    id UUID PRIMARY KEY,
    edge_type_id UUID NOT NULL REFERENCES record_edge_types (id) ON DELETE CASCADE,
    field_key TEXT NOT NULL,
    display_name TEXT NOT NULL,
    data_type TEXT NOT NULL,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_required BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    UNIQUE (edge_type_id, field_key)
);

CREATE INDEX IF NOT EXISTS ix_record_edge_type_fields_edge_type
    ON record_edge_type_fields (edge_type_id, sort_order);

CREATE TABLE IF NOT EXISTS record_edges (
    id UUID PRIMARY KEY,
    edge_type_id UUID NOT NULL REFERENCES record_edge_types (id) ON DELETE RESTRICT,
    from_record_id UUID NOT NULL REFERENCES records (id) ON DELETE CASCADE,
    to_record_id UUID NOT NULL REFERENCES records (id) ON DELETE CASCADE,
    data JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_record_edges_from
    ON record_edges (from_record_id, edge_type_id);

CREATE INDEX IF NOT EXISTS ix_record_edges_to
    ON record_edges (to_record_id, edge_type_id);

CREATE INDEX IF NOT EXISTS ix_record_edges_type
    ON record_edges (edge_type_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_record_edges_triple
    ON record_edges (edge_type_id, from_record_id, to_record_id);

-- Comments: free-text commentary attached to a record. The current body lives
-- on record_comments; every edit writes the PREVIOUS body to
-- record_comment_revisions in the same transaction so the history is
-- always reconstructable. Deletion is soft.
CREATE TABLE IF NOT EXISTS record_comments (
    id UUID PRIMARY KEY,
    record_id UUID NOT NULL REFERENCES records (id) ON DELETE CASCADE,
    author_id UUID NOT NULL,
    body TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    body_updated_at_utc TIMESTAMPTZ NOT NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at_utc TIMESTAMPTZ NULL,
    deleted_by UUID NULL
);

CREATE INDEX IF NOT EXISTS ix_record_comments_record_active
    ON record_comments (record_id, created_at_utc DESC)
    WHERE is_deleted = FALSE;

CREATE INDEX IF NOT EXISTS ix_record_comments_record_all
    ON record_comments (record_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS record_comment_revisions (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    comment_id UUID NOT NULL REFERENCES record_comments (id) ON DELETE CASCADE,
    body TEXT NOT NULL,
    replaced_at_utc TIMESTAMPTZ NOT NULL,
    replaced_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_record_comment_revisions_comment
    ON record_comment_revisions (comment_id, replaced_at_utc DESC);

INSERT INTO local_users (
    username,
    password_hash,
    password_salt,
    email,
    first_name,
    last_name,
    user_id,
    created_date,
    last_login_date,
    idp_key
)
VALUES (
    'admin',
    'ItdHztyrstpGA82U3e+0MtFcTVZq5N1jW5YvNtRvMTw=',
    '041Gg5Nyee8Xo8ge595Jyw==',
    'admin@localhost',
    'Admin',
    'User',
    '11111111-1111-1111-1111-111111111111',
    NOW(),
    NULL,
    'local-admin'
)
ON CONFLICT (username) DO NOTHING;
