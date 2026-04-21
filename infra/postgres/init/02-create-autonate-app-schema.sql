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
