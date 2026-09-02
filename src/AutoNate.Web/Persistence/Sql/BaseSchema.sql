-- Auton8's base schema: the foundational tables every later batch in
-- DatabaseSchemaInitializer assumes already exist.
--
-- THE single copy. It is an embedded resource of AutoNate.Web, applied by
-- EnsureAsync as its first step, and read from that same resource by both test
-- fixtures. It used to live in infra/postgres/init/ and be mounted into the
-- Postgres container's entrypoint, which meant the application could not
-- initialise an empty database on its own and left three consumers keeping one
-- file in step by hand.
--
-- No psql meta-commands. This is executed through Npgsql against a connection
-- already pointed at the target database; a `\c` here would be a syntax error.

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
    idp_key TEXT NOT NULL UNIQUE,
    failed_login_attempts INTEGER NOT NULL DEFAULT 0,
    is_locked BOOLEAN NOT NULL DEFAULT FALSE,
    locked_at_utc TIMESTAMPTZ NULL
);

ALTER TABLE local_users
    ADD COLUMN IF NOT EXISTS failed_login_attempts INTEGER NOT NULL DEFAULT 0;

ALTER TABLE local_users
    ADD COLUMN IF NOT EXISTS is_locked BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE local_users
    ADD COLUMN IF NOT EXISTS locked_at_utc TIMESTAMPTZ NULL;

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

ALTER TABLE workflow_models
    ADD COLUMN IF NOT EXISTS default_variables JSONB NULL;

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

-- =============================================================================
-- Authorization framework (Phase 1 scaffolding)
--   entity_kinds         - registry mirror, mostly documentation
--   entity_edges         - generalized polymorphic relationships between entities
--   roles                - named bundles of permissions; SuperAdmin is built-in
--   role_assignments     - which principal (user|group) holds which role
--   auth_cache_version   - single-row counter to bust the in-memory grant cache
--   auth_seed_state      - one-shot keys for migrations such as the SuperAdmin
--                          backfill
-- Permission grants and groups arrive in later phases.
-- =============================================================================

CREATE TABLE IF NOT EXISTS entity_kinds (
    kind TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    is_external BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS entity_edges (
    id UUID PRIMARY KEY,
    edge_kind TEXT NOT NULL,
    from_kind TEXT NOT NULL,
    from_id TEXT NOT NULL,
    to_kind TEXT NOT NULL,
    to_id TEXT NOT NULL,
    data JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_entity_edges_to
    ON entity_edges (to_kind, to_id, edge_kind);
CREATE INDEX IF NOT EXISTS ix_entity_edges_from
    ON entity_edges (from_kind, from_id, edge_kind);
CREATE UNIQUE INDEX IF NOT EXISTS uq_entity_edges_triple
    ON entity_edges (edge_kind, from_kind, from_id, to_kind, to_id);

CREATE TABLE IF NOT EXISTS roles (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE TABLE IF NOT EXISTS role_assignments (
    id UUID PRIMARY KEY,
    role_id UUID NOT NULL REFERENCES roles (id) ON DELETE CASCADE,
    principal_kind TEXT NOT NULL,
    principal_id TEXT NOT NULL,
    scope_string TEXT NULL,
    scope_ast JSONB NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_role_assignments_principal
    ON role_assignments (principal_kind, principal_id);
CREATE INDEX IF NOT EXISTS ix_role_assignments_role
    ON role_assignments (role_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_role_assignments_triple
    ON role_assignments (role_id, principal_kind, principal_id);

CREATE TABLE IF NOT EXISTS auth_cache_version (
    id INT PRIMARY KEY DEFAULT 1,
    version BIGINT NOT NULL DEFAULT 1,
    bumped_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO auth_cache_version (id, version, bumped_at_utc)
VALUES (1, 1, NOW())
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS auth_seed_state (
    key TEXT PRIMARY KEY,
    applied_at_utc TIMESTAMPTZ NOT NULL
);

INSERT INTO roles (
    id, name, description, is_system,
    created_at_utc, created_by, updated_at_utc, updated_by
)
VALUES (
    '00000000-0000-0000-0000-000000000001'::uuid,
    'SuperAdmin',
    'Built-in role that bypasses all authorization checks.',
    TRUE,
    NOW(),
    '00000000-0000-0000-0000-000000000000'::uuid,
    NOW(),
    '00000000-0000-0000-0000-000000000000'::uuid
)
ON CONFLICT (id) DO NOTHING;

-- =============================================================================
-- Authorization framework (Phase 3: groups, group_members)
-- Permissions used to live in a separate role_permissions table; they're now
-- unified into permission_grants below with principal_kind='role'.
-- =============================================================================

CREATE TABLE IF NOT EXISTS groups (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE TABLE IF NOT EXISTS group_members (
    group_id UUID NOT NULL REFERENCES groups (id) ON DELETE CASCADE,
    user_id UUID NOT NULL,
    added_at_utc TIMESTAMPTZ NOT NULL,
    added_by UUID NOT NULL,
    PRIMARY KEY (group_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_group_members_user
    ON group_members (user_id);

-- =============================================================================
-- Authorization framework (Phase 4: direct grants + read-path enforcement)
-- =============================================================================

CREATE TABLE IF NOT EXISTS permission_grants (
    id UUID PRIMARY KEY,
    principal_kind TEXT NOT NULL,           -- 'user' | 'group' | 'role'
    principal_id TEXT NOT NULL,
    action TEXT NOT NULL,
    selector_string TEXT NOT NULL,
    selector_ast JSONB NOT NULL,
    effect TEXT NOT NULL CHECK (effect IN ('allow','deny')),
    priority INT NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_permission_grants_principal
    ON permission_grants (principal_kind, principal_id);

-- No user is seeded here.
--
-- This used to INSERT `admin` with its password_hash AND password_salt
-- committed to the repository, ungated by environment. Combined with
-- Authorization:AssignSuperAdminToAllExistingUsers, every install that ran
-- this script came up with a super-admin whose password was public.
--
-- The first administrator is now created by the application at startup, only
-- while local_users is empty and only from configured credentials --
-- see BootstrapAdminOptions and DatabaseSchemaInitializer.EnsureBootstrapAdminAsync.

-- =============================================================================
-- Site menus & dynamic pages
-- =============================================================================

CREATE TABLE IF NOT EXISTS page_templates (
    id UUID PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    -- Plugin-supplied page templates extend the host's built-in set. Content
    -- is JSX source compiled at request time via the SPA's JsxPage component.
    -- created_by_plugin_id is set when a plugin auto-registered the row from
    -- its <pluginFolder>/PageTemplates/*.template files; FK CASCADE on
    -- plugins ensures these templates go away with the plugin that owns them.
    content TEXT NULL,
    content_type TEXT NOT NULL DEFAULT 'builtin',
    created_by_plugin_id UUID NULL,
    -- Presentation metadata used by the admin template picker. thumbnail_url
    -- holds an http(s) URL or a data: URI for a 200x150px preview image;
    -- category is a freeform text bucket for grouping templates in the UI.
    -- Templates do not carry a default URL — the URL lives on each menu item
    -- that mounts the template (menu_items.config->>'path').
    thumbnail_url TEXT NULL,
    category TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_page_templates_created_by_plugin_id
    ON page_templates (created_by_plugin_id)
    WHERE created_by_plugin_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS menus (
    id UUID PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT NULL,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE TABLE IF NOT EXISTS menu_items (
    id UUID PRIMARY KEY,
    menu_id UUID NOT NULL REFERENCES menus (id) ON DELETE CASCADE,
    parent_id UUID NULL REFERENCES menu_items (id) ON DELETE CASCADE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    display_name TEXT NOT NULL,
    icon TEXT NULL,
    item_type TEXT NOT NULL,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    permission_required TEXT NULL,
    is_visible BOOLEAN NOT NULL DEFAULT TRUE,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    -- FK to plugins(id) added by DatabaseSchemaInitializer once that table exists.
    created_by_plugin_id UUID NULL
);

CREATE INDEX IF NOT EXISTS ix_menu_items_menu_parent_sort
    ON menu_items (menu_id, parent_id NULLS FIRST, sort_order);

CREATE INDEX IF NOT EXISTS ix_menu_items_page_path
    ON menu_items ((config->>'path'))
    WHERE item_type = 'page';

CREATE INDEX IF NOT EXISTS ix_menu_items_template_key
    ON menu_items ((config->>'templateKey'))
    WHERE item_type = 'template';

CREATE TABLE IF NOT EXISTS status_appearance_entries (
    id UUID PRIMARY KEY,
    status TEXT NOT NULL UNIQUE,
    color TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

CREATE TABLE IF NOT EXISTS site_appearance_settings (
    id UUID PRIMARY KEY,
    site_name TEXT NOT NULL,
    logo_mode TEXT NOT NULL,
    logo_image_url TEXT NULL,
    logo_icon TEXT NULL,
    logo_text TEXT NOT NULL,
    login_tagline TEXT NULL,
    login_cover_image_url TEXT NULL,
    primary_accent_color TEXT NOT NULL,
    header_bg TEXT NOT NULL,
    header_color TEXT NOT NULL,
    top_menu_bg TEXT NOT NULL,
    top_menu_link_color TEXT NOT NULL,
    top_menu_link_hover_bg TEXT NOT NULL,
    top_menu_link_hover_color TEXT NOT NULL,
    top_menu_link_active_bg TEXT NOT NULL,
    top_menu_link_active_color TEXT NOT NULL,
    sidebar_bg TEXT NOT NULL,
    sidebar_link_color TEXT NOT NULL,
    sidebar_link_hover_color TEXT NOT NULL,
    sidebar_active_bg TEXT NOT NULL,
    sidebar_active_color TEXT NOT NULL,
    sidebar_icon_color TEXT NOT NULL,
    sidebar_submenu_bg TEXT NOT NULL,
    sidebar_section_color TEXT NOT NULL,
    surface_bg TEXT NOT NULL,
    surface_secondary_bg TEXT NOT NULL,
    surface_text_color TEXT NOT NULL,
    border_color TEXT NOT NULL,
    dropdown_bg TEXT NOT NULL,
    modal_bg TEXT NOT NULL,
    secondary_button_bg TEXT NOT NULL DEFAULT '#ffffff',
    secondary_button_text_color TEXT NOT NULL DEFAULT '#495057',
    secondary_button_border_color TEXT NOT NULL DEFAULT '#6c757d',
    secondary_button_hover_bg TEXT NOT NULL DEFAULT '#f1f3f5',
    secondary_button_hover_text_color TEXT NOT NULL DEFAULT '#212529',
    created_at_utc TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    updated_by UUID NOT NULL
);

ALTER TABLE site_appearance_settings
    ADD COLUMN IF NOT EXISTS secondary_button_bg TEXT NOT NULL DEFAULT '#ffffff';
ALTER TABLE site_appearance_settings
    ADD COLUMN IF NOT EXISTS secondary_button_text_color TEXT NOT NULL DEFAULT '#495057';
ALTER TABLE site_appearance_settings
    ADD COLUMN IF NOT EXISTS secondary_button_border_color TEXT NOT NULL DEFAULT '#6c757d';
ALTER TABLE site_appearance_settings
    ADD COLUMN IF NOT EXISTS secondary_button_hover_bg TEXT NOT NULL DEFAULT '#f1f3f5';
ALTER TABLE site_appearance_settings
    ADD COLUMN IF NOT EXISTS secondary_button_hover_text_color TEXT NOT NULL DEFAULT '#212529';

-- Seed the four built-in menus and their items mirroring the previously
-- hardcoded structure in NavMenu.tsx and ConfigLayout.tsx. Items are seeded
-- only when the menu is first created so admin edits aren't clobbered on
-- re-runs.

DO $$
DECLARE
    seed_actor UUID := '00000000-0000-0000-0000-000000000000';
    main_id UUID := '00000000-0000-0000-0001-000000000001';
    icon_id UUID := '00000000-0000-0000-0001-000000000002';
    user_id UUID := '00000000-0000-0000-0001-000000000003';
    site_id UUID := '00000000-0000-0000-0001-000000000004';
    appearance_id UUID := '00000000-0000-0000-0001-000000000005';
    g UUID;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM site_appearance_settings WHERE id = appearance_id) THEN
        INSERT INTO site_appearance_settings (
            id, site_name, logo_mode, logo_image_url, logo_icon, logo_text,
            login_tagline, login_cover_image_url, primary_accent_color,
            header_bg, header_color, top_menu_bg, top_menu_link_color,
            top_menu_link_hover_bg, top_menu_link_hover_color,
            top_menu_link_active_bg, top_menu_link_active_color,
            sidebar_bg, sidebar_link_color, sidebar_link_hover_color,
            sidebar_active_bg, sidebar_active_color, sidebar_icon_color,
            sidebar_submenu_bg, sidebar_section_color, surface_bg,
            surface_secondary_bg, surface_text_color, border_color,
            dropdown_bg, modal_bg, secondary_button_bg,
            secondary_button_text_color, secondary_button_border_color,
            secondary_button_hover_bg, secondary_button_hover_text_color,
            created_at_utc, created_by,
            updated_at_utc, updated_by)
        VALUES (
            appearance_id, 'Auton8', 'icon', NULL, 'fa fa-robot', 'Auton8',
            'Sign in to continue to the automation dashboard',
            '/assets/img/login-bg/space.jpg', '#00acac',
            '#ffffff', '#212529', '#20252a', '#a6aaac',
            '#20252a', '#ffffff', '#20252a', '#ffffff',
            '#ffffff', '#6c757d', '#212529', '#f1f3f5',
            '#212529', '#212529', '#ffffff', '#adb5bd',
            '#ffffff', '#dee2e6', '#212529', '#ced4da',
            '#ffffff', '#ffffff', '#ffffff', '#495057',
            '#6c757d', '#f1f3f5', '#212529',
            NOW(), seed_actor, NOW(), seed_actor);
    END IF;

    UPDATE site_appearance_settings
    SET secondary_button_bg = COALESCE(secondary_button_bg, '#ffffff'),
        secondary_button_text_color = COALESCE(secondary_button_text_color, '#495057'),
        secondary_button_border_color = COALESCE(secondary_button_border_color, '#6c757d'),
        secondary_button_hover_bg = COALESCE(secondary_button_hover_bg, '#f1f3f5'),
        secondary_button_hover_text_color = COALESCE(secondary_button_hover_text_color, '#212529')
    WHERE id = appearance_id;

    UPDATE site_appearance_settings
    SET top_menu_bg = '#20252a',
        top_menu_link_color = '#a6aaac',
        top_menu_link_hover_bg = '#20252a',
        top_menu_link_hover_color = '#ffffff',
        top_menu_link_active_bg = '#20252a',
        top_menu_link_active_color = '#ffffff',
        updated_at_utc = NOW(),
        updated_by = seed_actor
    WHERE id = appearance_id
      AND top_menu_bg = '#ffffff'
      AND top_menu_link_color = '#6c757d'
      AND top_menu_link_hover_bg = '#f8f9fa'
      AND top_menu_link_hover_color = '#212529'
      AND top_menu_link_active_bg = '#f8f9fa'
      AND top_menu_link_active_color = '#212529';

    -- ---------- Main menu ----------
    IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'main') THEN
        INSERT INTO menus (id, key, name, description, is_system,
            created_at_utc, created_by, updated_at_utc, updated_by)
        VALUES (main_id, 'main', 'Main Menu',
            'The top navigation bar shown on every page.',
            TRUE, NOW(), seed_actor, NOW(), seed_actor);

        -- Dashboard group
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, main_id, NULL, 0, 'Dashboard', 'fa fa-house',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), main_id, g, 0, 'Home', NULL,
            'route', '{"path":"/home"}'::jsonb, TRUE, TRUE, NOW(), NOW());

        -- Records group (with dynamic record-type children)
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, main_id, NULL, 1, 'Records', 'fa fa-database',
            'group', '{"dynamicChildren":"recordTypes"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), main_id, g, 0, 'Record Types', NULL,
            'route', '{"path":"/record-types"}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), main_id, g, 1, 'Edge Types', NULL,
            'route', '{"path":"/record-edge-types"}'::jsonb, TRUE, TRUE, NOW(), NOW());

        -- Workflows group
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, main_id, NULL, 2, 'Workflows', 'fa fa-diagram-project',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), main_id, g, 0, 'Workflow Studio', NULL,
            'route', '{"path":"/workflow"}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), main_id, g, 1, 'Workflow Executions', NULL,
            'route', '{"path":"/workflow-executions"}'::jsonb, TRUE, TRUE, NOW(), NOW());
    END IF;

    -- ---------- Icon menu (top-right icon strip) ----------
    -- Each top-level item becomes its own icon in the top bar. Group items
    -- render as icon+dropdown; route/page/link items render as a single icon
    -- link. Default install seeds a single 'Settings' (gear) group with all
    -- the existing admin shortcuts.
    IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'icon') THEN
        INSERT INTO menus (id, key, name, description, is_system,
            created_at_utc, created_by, updated_at_utc, updated_by)
        VALUES (icon_id, 'icon', 'Icon Menu',
            'Top-right icon strip. Each top-level item is a separate icon; group items become dropdowns.',
            TRUE, NOW(), seed_actor, NOW(), seed_actor);

        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, icon_id, NULL, 0, 'Settings', 'fa fa-gear',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 0, 'Site Configuration',
            'fa fa-sliders', 'route', '{"path":"/admin/config"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 1, 'Manage Users',
            'fa fa-users', 'route', '{"path":"/manage-users"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 2, 'Roles & Permissions',
            'fa fa-user-shield', 'route', '{"path":"/admin/roles"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 3, 'Groups',
            'fa fa-people-group', 'route', '{"path":"/admin/groups"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 4, 'Permissions',
            'fa fa-key', 'route', '{"path":"/admin/grants"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 5, 'Hierarchy',
            'fa fa-sitemap', 'route', '{"path":"/admin/hierarchy"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), icon_id, g, 6, 'Effective Permissions',
            'fa fa-magnifying-glass', 'route', '{"path":"/admin/explain"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
    END IF;

    -- ---------- User menu ----------
    IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'user') THEN
        INSERT INTO menus (id, key, name, description, is_system,
            created_at_utc, created_by, updated_at_utc, updated_by)
        VALUES (user_id, 'user', 'User Menu',
            'The dropdown beside the signed-in user''s name in the top navigation.',
            TRUE, NOW(), seed_actor, NOW(), seed_actor);

        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), user_id, NULL, 0, 'User Profile',
            'fa fa-user', 'route', '{"path":"/user-profile"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), user_id, NULL, 1, '',
            NULL, 'separator', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), user_id, NULL, 2, 'Logout',
            'fa fa-right-from-bracket', 'action', '{"action":"logout"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
    END IF;

    -- ---------- Site Configuration menu (left side nav at /admin/config) ----------
    IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'site-config') THEN
        INSERT INTO menus (id, key, name, description, is_system,
            created_at_utc, created_by, updated_at_utc, updated_by)
        VALUES (site_id, 'site-config', 'Site Configuration',
            'The left-hand navigation shown inside the Site Configuration area.',
            TRUE, NOW(), seed_actor, NOW(), seed_actor);

        -- Site Information group
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, site_id, NULL, 0, 'Site Information', 'fa fa-circle-info',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 0, 'Bus Watcher', 'fa fa-tower-broadcast',
            'route', '{"path":"/admin/config/bus-watcher"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 1, 'Events', 'fa fa-bell',
            'route', '{"path":"/admin/config/events"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());

        -- Sitewide Configuration group
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, site_id, NULL, 1, 'Sitewide Configuration', 'fa fa-sliders',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 0, 'General', 'fa fa-gear',
            'route', '{"path":"/admin/config/general"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 1, 'Features', 'fa fa-toggle-on',
            'route', '{"path":"/admin/config/features"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 2, 'Appearance', 'fa fa-palette',
            'route', '{"path":"/admin/config/appearance"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 3, 'Status Appearance', 'fa fa-circle-info',
            'route', '{"path":"/admin/config/status-appearance"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 4, 'External Connections', 'fa fa-plug',
            'route', '{"path":"/admin/config/external-connections"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 5, 'Pages / Menus', 'fa fa-list',
            'route', '{"path":"/admin/config/pages-menus"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());

        -- Security group
        g := gen_random_uuid();
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (g, site_id, NULL, 2, 'Security', 'fa fa-shield-halved',
            'group', '{}'::jsonb, TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 0, 'Manage Users', 'fa fa-users',
            'route', '{"path":"/admin/config/users"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 1, 'Manage Groups', 'fa fa-people-group',
            'route', '{"path":"/admin/config/groups"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 2, 'Manage Roles', 'fa fa-user-shield',
            'route', '{"path":"/admin/config/roles"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 3, 'Set Permissions', 'fa fa-key',
            'route', '{"path":"/admin/config/permissions"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
        INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name,
            icon, item_type, config, is_visible, is_system,
            created_at_utc, updated_at_utc)
        VALUES (gen_random_uuid(), site_id, g, 4, 'Permission Checker', 'fa fa-magnifying-glass',
            'route', '{"path":"/admin/config/permission-checker"}'::jsonb,
            TRUE, TRUE, NOW(), NOW());
    END IF;
END $$;

-- Phase 5 of the audit-events plan: durable outbox between event publishers
-- and the bus. EfCoreAuditEventOutbox writes one row per published event;
-- AuditOutboxDispatcher polls undispatched rows and posts to Dapr. Mirrored
-- in DatabaseSchemaInitializer for incremental migrations on existing DBs.
CREATE TABLE IF NOT EXISTS audit_outbox (
    id BIGSERIAL PRIMARY KEY,
    topic TEXT NOT NULL,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    dispatched_at_utc TIMESTAMPTZ NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT NULL,
    next_attempt_after_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_audit_outbox_pending
    ON audit_outbox (next_attempt_after_utc)
    WHERE dispatched_at_utc IS NULL;

-- workflow_execution_errors table. Mirrored in DatabaseSchemaInitializer
-- (WorkflowExecutionErrorsSql). WorkflowExecutionErrorRecorder writes one row
-- per job.execution.failed event so the executions UI and the
-- WorkflowExecutionErrorOpenDetector can surface stuck-process states.
CREATE TABLE IF NOT EXISTS workflow_execution_errors (
    id UUID PRIMARY KEY,
    process_instance_id TEXT NOT NULL,
    activity_id TEXT NOT NULL,
    activity_name TEXT NULL,
    error_message TEXT NULL,
    raw_flowable_event_type TEXT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_workflow_execution_errors_process_instance_id
    ON workflow_execution_errors (process_instance_id);

-- error_stack_trace was added after the initial table; idempotent so it's
-- safe on both fresh installs (column was just created above) and upgrades.
ALTER TABLE workflow_execution_errors
    ADD COLUMN IF NOT EXISTS error_stack_trace TEXT NULL;

-- Plugins table. Mirrored in DatabaseSchemaInitializer (PluginsSchemaSql) —
-- needed at bootstrap time so OrphanReferenceDetector can scan
-- menu_items.created_by_plugin_id orphans even before any plugin work runs.
CREATE TABLE IF NOT EXISTS plugins (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    version TEXT NOT NULL,
    entry_assembly TEXT NOT NULL,
    entry_type TEXT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    uploaded_at TIMESTAMPTZ NOT NULL,
    uploaded_by UUID NOT NULL,
    last_enabled_at TIMESTAMPTZ NULL,
    last_disabled_at TIMESTAMPTZ NULL,
    last_error TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_plugins_status ON plugins (status);

-- Notifications inbox. Mirrored in DatabaseSchemaInitializer (NotificationsSql).
-- One row per delivered notification; cleared by event-driven hooks +
-- OrphanedNotificationCleanupService + OrphanReferenceDetector remediator.
CREATE TABLE IF NOT EXISTS notifications (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    kind TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    related_entity_kind TEXT NULL,
    related_entity_id TEXT NULL,
    link_path TEXT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    read_at_utc TIMESTAMPTZ NULL,
    parent_entity_kind TEXT NULL,
    parent_entity_id TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_notifications_user_created
    ON notifications (user_id, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_notifications_user_unread
    ON notifications (user_id, is_read);

CREATE INDEX IF NOT EXISTS ix_notifications_parent
    ON notifications (parent_entity_kind, parent_entity_id)
    WHERE parent_entity_id IS NOT NULL;

-- Phase 4 of the self-healing plan: parking lot for audit_outbox rows the
-- dispatcher has given up on. AuditOutboxDeadLetterParkRemediator moves rows
-- here so the live audit_outbox stays small. Mirrored in
-- DatabaseSchemaInitializer.
CREATE TABLE IF NOT EXISTS audit_outbox_dead_letters (
    id BIGSERIAL PRIMARY KEY,
    original_outbox_id BIGINT NOT NULL,
    topic TEXT NOT NULL,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    original_created_at_utc TIMESTAMPTZ NOT NULL,
    attempt_count INTEGER NOT NULL,
    last_error TEXT NULL,
    parked_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    parked_reason TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_audit_outbox_dead_letters_parked_at
    ON audit_outbox_dead_letters (parked_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_audit_outbox_dead_letters_topic
    ON audit_outbox_dead_letters (topic, event_type);

-- Self-healing platform: every detector writes one row per distinct issue it
-- finds. The partial unique index on `fingerprint` is the dedup contract — re-
-- detecting the same issue bumps occurrence_count instead of inserting. Re-
-- occurrence after resolution opens a fresh row because the index only covers
-- open/acknowledged states. Mirrored in DatabaseSchemaInitializer.
CREATE TABLE IF NOT EXISTS system_issues (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    detector_id TEXT NOT NULL,
    category TEXT NOT NULL,
    severity TEXT NOT NULL,
    fingerprint TEXT NOT NULL,
    title TEXT NOT NULL,
    summary TEXT NULL,
    related_entity_kind TEXT NULL,
    related_entity_id TEXT NULL,
    facts_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    state TEXT NOT NULL DEFAULT 'open',
    first_seen_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    occurrence_count INTEGER NOT NULL DEFAULT 1,
    acknowledged_at_utc TIMESTAMPTZ NULL,
    acknowledged_by UUID NULL,
    resolved_at_utc TIMESTAMPTZ NULL,
    resolution_kind TEXT NULL,
    resolution_notes TEXT NULL,
    auto_remediation_attempt_count INTEGER NOT NULL DEFAULT 0,
    auto_remediation_last_error TEXT NULL,
    next_remediation_after_utc TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_system_issues_open_fingerprint
    ON system_issues (fingerprint)
    WHERE state IN ('open', 'acknowledged');

CREATE INDEX IF NOT EXISTS ix_system_issues_open
    ON system_issues (severity, last_seen_at_utc DESC)
    WHERE state = 'open';

CREATE INDEX IF NOT EXISTS ix_system_issues_related
    ON system_issues (related_entity_kind, related_entity_id)
    WHERE related_entity_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_system_issues_remediation_due
    ON system_issues (next_remediation_after_utc)
    WHERE state = 'open' AND next_remediation_after_utc IS NOT NULL;
