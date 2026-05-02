using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AuthorizationOptions = AutoNate.Web.Authorization.AuthorizationOptions;

namespace AutoNate.Web.Persistence;

internal static class DatabaseSchemaInitializer
{
    private const string WorkflowVersioningSql =
        """
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

        ALTER TABLE workflow_models
            ADD COLUMN IF NOT EXISTS is_draft BOOLEAN NOT NULL DEFAULT TRUE;

        ALTER TABLE workflow_models
            ADD COLUMN IF NOT EXISTS draft_version_number INTEGER NOT NULL DEFAULT 1;

        ALTER TABLE workflow_models
            ADD COLUMN IF NOT EXISTS published_version_number INTEGER NULL;

        UPDATE workflow_models
        SET is_draft = CASE
                WHEN last_deployment_id IS NULL THEN TRUE
                WHEN published_version_number IS NOT NULL AND draft_version_number = published_version_number THEN FALSE
                ELSE TRUE
            END
        WHERE is_draft IS DISTINCT FROM CASE
                WHEN last_deployment_id IS NULL THEN TRUE
                WHEN published_version_number IS NOT NULL AND draft_version_number = published_version_number THEN FALSE
                ELSE TRUE
            END;

        UPDATE workflow_models
        SET draft_version_number = CASE
                WHEN last_process_definition_version IS NOT NULL THEN GREATEST(last_process_definition_version, 1)
                ELSE GREATEST(draft_version_number, 1)
            END
        WHERE draft_version_number IS NULL
           OR draft_version_number < 1
           OR (last_process_definition_version IS NOT NULL AND draft_version_number <> last_process_definition_version);

        UPDATE workflow_models
        SET published_version_number = last_process_definition_version
        WHERE published_version_number IS NULL
          AND last_process_definition_version IS NOT NULL;

        INSERT INTO workflow_model_versions (
            id,
            workflow_model_id,
            version_number,
            name,
            process_key,
            bpmn_xml,
            deployment_id,
            process_definition_id,
            process_definition_key,
            process_definition_version,
            published_at_utc
        )
        SELECT
            (
                substr(backfill_version_id.hash, 1, 8) || '-' ||
                substr(backfill_version_id.hash, 9, 4) || '-' ||
                substr(backfill_version_id.hash, 13, 4) || '-' ||
                substr(backfill_version_id.hash, 17, 4) || '-' ||
                substr(backfill_version_id.hash, 21, 12)
            )::uuid,
            wm.id,
            wm.last_process_definition_version,
            wm.name,
            wm.process_key,
            wm.bpmn_xml,
            wm.last_deployment_id,
            wm.last_process_definition_id,
            wm.last_process_definition_key,
            wm.last_process_definition_version,
            COALESCE(wm.last_deployed_at_utc, wm.updated_at_utc)
        FROM workflow_models wm
        CROSS JOIN LATERAL (
            SELECT md5(wm.id::text || ':' || wm.last_process_definition_version::text) AS hash
        ) AS backfill_version_id
        WHERE wm.last_process_definition_version IS NOT NULL
          AND wm.last_deployment_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1
              FROM workflow_model_versions version
              WHERE version.workflow_model_id = wm.id
                AND version.version_number = wm.last_process_definition_version
          )
        ON CONFLICT (workflow_model_id, version_number) DO NOTHING;
        """;

    private const string RecordsSchemaSql =
        """
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
            config JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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
        """;

    private const string RecordsDataSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS records (
            id UUID PRIMARY KEY,
            record_type_id UUID NOT NULL REFERENCES record_types (id) ON DELETE RESTRICT,
            key TEXT NOT NULL UNIQUE,
            key_number BIGINT NOT NULL,
            name TEXT NOT NULL,
            assignee_ids UUID[] NOT NULL DEFAULT '{{}}',
            values JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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

        ALTER TABLE record_field_changes
            ADD COLUMN IF NOT EXISTS change_set_id UUID NULL;

        CREATE INDEX IF NOT EXISTS ix_record_field_changes_change_set
            ON record_field_changes (change_set_id);

        -- Backfill existing rows: rows that share (record_id, changed_at_utc,
        -- changed_by) came from the same mutation, so give each such group a
        -- single change_set_id. Idempotent because we only touch NULLs.
        UPDATE record_field_changes rfc
        SET change_set_id = grp.id
        FROM (
            SELECT record_id, changed_at_utc, changed_by, gen_random_uuid() AS id
            FROM record_field_changes
            WHERE change_set_id IS NULL
            GROUP BY record_id, changed_at_utc, changed_by
        ) AS grp
        WHERE rfc.change_set_id IS NULL
          AND rfc.record_id = grp.record_id
          AND rfc.changed_at_utc = grp.changed_at_utc
          AND rfc.changed_by = grp.changed_by;
        """;

    private const string RecordsEdgesSchemaSql =
        """
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
            config JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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
            data JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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
        """;

    private const string RecordsCommentsSchemaSql =
        """
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
        """;

    private const string AuthorizationSchemaSql =
        """
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
            data JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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

        CREATE TABLE IF NOT EXISTS permission_grants (
            id UUID PRIMARY KEY,
            principal_kind TEXT NOT NULL,
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
        """;

    // Phase 2: backfill creator and assignee edges from the legacy columns into
    // the generalized entity_edges table. NOT EXISTS guards make it idempotent;
    // the auth_seed_state row records when it last completed for ops visibility.
    private const string RecordEdgeBackfillSql =
        """
        INSERT INTO entity_edges (
            id, edge_kind, from_kind, from_id, to_kind, to_id,
            data, created_at_utc, created_by
        )
        SELECT
            gen_random_uuid(),
            'creator', 'user', r.created_by::text, 'record', r.id::text,
            '{{}}'::jsonb, r.created_at_utc, r.created_by
        FROM records r
        WHERE NOT EXISTS (
            SELECT 1 FROM entity_edges e
            WHERE e.edge_kind = 'creator'
              AND e.from_kind = 'user'
              AND e.from_id   = r.created_by::text
              AND e.to_kind   = 'record'
              AND e.to_id     = r.id::text
        );

        INSERT INTO entity_edges (
            id, edge_kind, from_kind, from_id, to_kind, to_id,
            data, created_at_utc, created_by
        )
        SELECT
            gen_random_uuid(),
            'assignee', 'user', a::text, 'record', r.id::text,
            '{{}}'::jsonb, r.created_at_utc, r.created_by
        FROM records r
        CROSS JOIN LATERAL UNNEST(r.assignee_ids) AS a
        WHERE NOT EXISTS (
            SELECT 1 FROM entity_edges e
            WHERE e.edge_kind = 'assignee'
              AND e.from_kind = 'user'
              AND e.from_id   = a::text
              AND e.to_kind   = 'record'
              AND e.to_id     = r.id::text
        );

        INSERT INTO auth_seed_state (key, applied_at_utc)
        VALUES ('record_edges_backfill_v1', NOW())
        ON CONFLICT (key) DO UPDATE SET applied_at_utc = EXCLUDED.applied_at_utc;
        """;

    // Phase 5: one-shot SuperAdmin backfill. Grants the built-in SuperAdmin
    // role to every existing local_user so flipping enforcement to "full"
    // doesn't lock anyone out. Gated by Authorization:AssignSuperAdminToAllExistingUsers
    // (default true) and a seed_state key to ensure it only runs once.
    private const string SuperAdminBackfillSql =
        """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'superadmin_backfill_v1') THEN
                INSERT INTO role_assignments (
                    id, role_id, principal_kind, principal_id,
                    scope_string, scope_ast, created_at_utc, created_by
                )
                SELECT
                    gen_random_uuid(),
                    '00000000-0000-0000-0000-000000000001'::uuid,
                    'user',
                    u.user_id::text,
                    NULL, NULL,
                    NOW(),
                    '00000000-0000-0000-0000-000000000000'::uuid
                FROM local_users u
                WHERE NOT EXISTS (
                    SELECT 1 FROM role_assignments r
                    WHERE r.role_id = '00000000-0000-0000-0000-000000000001'::uuid
                      AND r.principal_kind = 'user'
                      AND r.principal_id = u.user_id::text
                );

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('superadmin_backfill_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Phase 7: shadow record_edges into the generalized entity_edges table.
    // Reuse each record_edges.id as the entity_edges.id so dedup is by primary
    // key — re-running is a no-op. edge_kind comes from
    // record_edge_types.short_code; from/to are 'record' (the only thing
    // record_edges currently models).
    private const string RecordEdgeShadowBackfillSql =
        """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'record_edge_shadow_v1') THEN
                INSERT INTO entity_edges (
                    id, edge_kind, from_kind, from_id, to_kind, to_id,
                    data, created_at_utc, created_by
                )
                SELECT
                    re.id,
                    et.short_code,
                    'record', re.from_record_id::text,
                    'record', re.to_record_id::text,
                    re.data, re.created_at_utc, re.created_by
                FROM record_edges re
                JOIN record_edge_types et ON et.id = re.edge_type_id
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('record_edge_shadow_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Phase 7: partial indexes for the hottest selector subqueries. The
    // assignee=user predicate compiles to an EXISTS join that does
    // (edge_kind='assignee', to_kind='record', to_id=r.id::text). A partial
    // index on to_id where edge_kind+to_kind match is smaller and faster than
    // the full composite — significant when entity_edges grows large.
    private const string EntityEdgeHotIndexesSql =
        """
        CREATE INDEX IF NOT EXISTS ix_entity_edges_assignee_to_record
            ON entity_edges (to_id)
            WHERE edge_kind = 'assignee' AND to_kind = 'record';

        CREATE INDEX IF NOT EXISTS ix_entity_edges_creator_to_record
            ON entity_edges (to_id)
            WHERE edge_kind = 'creator' AND to_kind = 'record';

        CREATE INDEX IF NOT EXISTS ix_entity_edges_supervisor_to_user
            ON entity_edges (to_id)
            WHERE edge_kind = 'supervisor' AND to_kind = 'user';

        -- Multi-hop predicates like /record/*[assignee=user[supervisor=user]]
        -- run an inner subquery that asks "given the actor, who do they
        -- supervise?". Indexing the supervisor edges by from_id makes that
        -- subquery a hash/index lookup instead of a sequential scan.
        CREATE INDEX IF NOT EXISTS ix_entity_edges_supervisor_from_user
            ON entity_edges (from_id)
            WHERE edge_kind = 'supervisor' AND from_kind = 'user' AND to_kind = 'user';
        """;

    // Phase 8: unify role_permissions into permission_grants and drop the
    // legacy table. Each role_permissions row becomes a permission_grant with
    // principal_kind='role' and principal_id=role_id::text. Gated by a
    // seed_state row so re-running on a migrated DB is a cheap no-op.
    private const string RolePermissionsToGrantsSql =
        """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'role_permissions_to_grants_v1') THEN
                IF EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_name = 'role_permissions'
                ) THEN
                    INSERT INTO permission_grants (
                        id, principal_kind, principal_id, action,
                        selector_string, selector_ast, effect, priority,
                        created_at_utc, created_by, updated_at_utc, updated_by
                    )
                    SELECT
                        rp.id, 'role', rp.role_id::text, rp.action,
                        rp.selector_string, rp.selector_ast, rp.effect, rp.priority,
                        rp.created_at_utc, rp.created_by, rp.updated_at_utc, rp.updated_by
                    FROM role_permissions rp
                    ON CONFLICT (id) DO NOTHING;

                    DROP TABLE role_permissions;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('role_permissions_to_grants_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    private const string PageTemplatesSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS page_templates (
            id UUID PRIMARY KEY,
            key TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            description TEXT NULL,
            default_path TEXT NOT NULL UNIQUE,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_menu_items_template_key
            ON menu_items ((config->>'templateKey'))
            WHERE item_type = 'template';
        """;

    // Plugin-supplied page templates extend the host's built-in template set.
    // The plugin ships .template files under <pluginFolder>/PageTemplates and
    // PluginRuntime upserts a row here on each enable; the rendered content
    // travels in this table (not in the file system at request time) so the
    // SPA's existing /api/pages/lookup pipeline serves it as JSX. Ownership
    // is tracked by created_by_plugin_id with FK CASCADE on the plugins row,
    // so deleting a plugin sweeps every template it ever registered.
    //
    // The block runs *after* PluginsSchemaSql (so the FK target exists) and is
    // idempotent: every column add is `IF NOT EXISTS`, the FK is gated by a
    // pg_constraint lookup, and the index uses `IF NOT EXISTS`.
    private const string PageTemplatesPluginColumnsSql =
        """
        ALTER TABLE page_templates
            ADD COLUMN IF NOT EXISTS content TEXT NULL;

        ALTER TABLE page_templates
            ADD COLUMN IF NOT EXISTS content_type TEXT NOT NULL DEFAULT 'builtin';

        ALTER TABLE page_templates
            ADD COLUMN IF NOT EXISTS created_by_plugin_id UUID NULL;

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'page_templates_created_by_plugin_id_fkey'
            ) THEN
                ALTER TABLE page_templates
                    ADD CONSTRAINT page_templates_created_by_plugin_id_fkey
                    FOREIGN KEY (created_by_plugin_id)
                    REFERENCES plugins (id) ON DELETE CASCADE;
            END IF;
        END $$;

        CREATE INDEX IF NOT EXISTS ix_page_templates_created_by_plugin_id
            ON page_templates (created_by_plugin_id)
            WHERE created_by_plugin_id IS NOT NULL;
        """;

    private const string MenusSchemaSql =
        """
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
            config JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            permission_required TEXT NULL,
            is_visible BOOLEAN NOT NULL DEFAULT TRUE,
            is_system BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_menu_items_menu_parent_sort
            ON menu_items (menu_id, parent_id NULLS FIRST, sort_order);

        CREATE INDEX IF NOT EXISTS ix_menu_items_page_path
            ON menu_items ((config->>'path'))
            WHERE item_type = 'page';

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
                    appearance_id, 'Auto Nate', 'icon', NULL, 'fa fa-robot', 'Auto Nate',
                    'Sign in to continue to the automation dashboard',
                    '/spa/assets/img/login-bg/login-bg-17.jpg', '#00acac',
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

            IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'main') THEN
                INSERT INTO menus (id, key, name, description, is_system,
                    created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES (main_id, 'main', 'Main Menu',
                    'The top navigation bar shown on every page.',
                    TRUE, NOW(), seed_actor, NOW(), seed_actor);

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, main_id, NULL, 0, 'Dashboard', 'fa fa-house', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 0, 'Home', NULL, 'template', '{{"templateKey":"home"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, main_id, NULL, 1, 'Records', 'fa fa-database', 'group', '{{"dynamicChildren":"recordTypes"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 0, 'Record Types', NULL, 'route', '{{"path":"/record-types"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 1, 'Edge Types', NULL, 'route', '{{"path":"/record-edge-types"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, main_id, NULL, 2, 'Workflows', 'fa fa-diagram-project', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 0, 'Workflow Studio', NULL, 'route', '{{"path":"/workflow"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 1, 'Workflow Executions', NULL, 'route', '{{"path":"/workflow-executions"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
            END IF;

            IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'icon') THEN
                INSERT INTO menus (id, key, name, description, is_system, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES (icon_id, 'icon', 'Icon Menu', 'Top-right icon strip. Each top-level item is a separate icon; group items become dropdowns.', TRUE, NOW(), seed_actor, NOW(), seed_actor);

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, icon_id, NULL, 0, 'Settings', 'fa fa-gear', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 0, 'Site Configuration', 'fa fa-sliders', 'route', '{{"path":"/admin/config"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 1, 'Manage Users', 'fa fa-users', 'template', '{{"templateKey":"manageUsers"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 2, 'Roles & Permissions', 'fa fa-user-shield', 'template', '{{"templateKey":"adminRoles"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 3, 'Groups', 'fa fa-people-group', 'template', '{{"templateKey":"adminGroups"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 4, 'Permissions', 'fa fa-key', 'template', '{{"templateKey":"adminGrants"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 5, 'Hierarchy', 'fa fa-sitemap', 'template', '{{"templateKey":"adminHierarchy"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), icon_id, g, 6, 'Effective Permissions', 'fa fa-magnifying-glass', 'template', '{{"templateKey":"adminExplain"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
            END IF;

            IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'user') THEN
                INSERT INTO menus (id, key, name, description, is_system, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES (user_id, 'user', 'User Menu', 'The dropdown beside the signed-in user''s name.', TRUE, NOW(), seed_actor, NOW(), seed_actor);

                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), user_id, NULL, 0, 'User Profile', 'fa fa-user', 'template', '{{"templateKey":"userProfile"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), user_id, NULL, 1, '', NULL, 'separator', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), user_id, NULL, 2, 'Logout', 'fa fa-right-from-bracket', 'action', '{{"action":"logout"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
            END IF;

            IF NOT EXISTS (SELECT 1 FROM menus WHERE key = 'site-config') THEN
                INSERT INTO menus (id, key, name, description, is_system, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES (site_id, 'site-config', 'Site Configuration', 'The left-hand navigation shown inside the Site Configuration area.', TRUE, NOW(), seed_actor, NOW(), seed_actor);

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, site_id, NULL, 0, 'Site Information', 'fa fa-circle-info', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 0, 'Bus Watcher', 'fa fa-tower-broadcast', 'template', '{{"templateKey":"configBusWatcher"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 1, 'Events', 'fa fa-bell', 'template', '{{"templateKey":"configEvents"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, site_id, NULL, 1, 'Sitewide Configuration', 'fa fa-sliders', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 0, 'General', 'fa fa-gear', 'template', '{{"templateKey":"configGeneral"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 1, 'Features', 'fa fa-toggle-on', 'template', '{{"templateKey":"configFeatures"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 2, 'Appearance', 'fa fa-palette', 'template', '{{"templateKey":"configAppearance"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 3, 'Status Appearance', 'fa fa-circle-info', 'template', '{{"templateKey":"configStatusAppearance"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 4, 'External Connections', 'fa fa-plug', 'template', '{{"templateKey":"configExternalConnections"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 5, 'Pages / Menus', 'fa fa-list', 'template', '{{"templateKey":"configPagesMenus"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, site_id, NULL, 2, 'Security', 'fa fa-shield-halved', 'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 0, 'Manage Users', 'fa fa-users', 'template', '{{"templateKey":"configSecurityUsers"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 1, 'Manage Groups', 'fa fa-people-group', 'template', '{{"templateKey":"configSecurityGroups"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 2, 'Manage Roles', 'fa fa-user-shield', 'template', '{{"templateKey":"configSecurityRoles"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 3, 'Set Permissions', 'fa fa-key', 'template', '{{"templateKey":"configSecurityPermissions"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), site_id, g, 4, 'Permission Checker', 'fa fa-magnifying-glass', 'template', '{{"templateKey":"configSecurityPermissionChecker"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
            END IF;
        END $$;
        """;

    // Icon menu was originally seeded as flat top-level routes that all lived
    // inside a single hardcoded gear dropdown. The new model treats every
    // top-level icon menu item as its own top-bar element (icon-with-dropdown
    // for groups, icon-link for routes/pages/links). To preserve the gear
    // experience, wrap the existing top-level items inside a "Settings" group.
    // Idempotent: gated by an auth_seed_state row.
    private const string IconMenuWrapSettingsSql =
        """
        DO $$
        DECLARE
            icon_id UUID := '00000000-0000-0000-0001-000000000002';
            settings_group_id UUID;
            seed_actor UUID := '00000000-0000-0000-0000-000000000000';
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'icon_menu_wrap_settings_v1') THEN
                -- Only run when the icon menu exists and has no top-level groups yet.
                IF EXISTS (SELECT 1 FROM menus WHERE id = icon_id)
                   AND NOT EXISTS (
                       SELECT 1 FROM menu_items
                       WHERE menu_id = icon_id AND parent_id IS NULL AND item_type = 'group'
                   )
                THEN
                    settings_group_id := gen_random_uuid();
                    INSERT INTO menu_items (
                        id, menu_id, parent_id, sort_order, display_name, icon,
                        item_type, config, is_visible, is_system,
                        created_at_utc, updated_at_utc
                    )
                    VALUES (
                        settings_group_id, icon_id, NULL, 0, 'Settings', 'fa fa-gear',
                        'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW()
                    );

                    UPDATE menu_items
                    SET parent_id = settings_group_id, updated_at_utc = NOW()
                    WHERE menu_id = icon_id
                      AND parent_id IS NULL
                      AND id <> settings_group_id;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('icon_menu_wrap_settings_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Introduce a "Site Information" group to the site-config left-nav that
    // holds non-security, non-sitewide informational pages. Moves the Bus
    // Watcher item out of the main menu's Workflows group and into this new
    // group (rerouted to /admin/config/bus-watcher so it renders inside
    // ConfigLayout), and adds the new Events documentation page. Idempotent
    // via auth_seed_state.
    private const string SiteConfigSiteInformationSql =
        """
        DO $$
        DECLARE
            main_id UUID := '00000000-0000-0000-0001-000000000001';
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            site_information_group_id UUID;
            bus_watcher_item_id UUID;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_site_information_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO site_information_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Site Information'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF site_information_group_id IS NULL THEN
                        UPDATE menu_items
                        SET sort_order = sort_order + 1,
                            updated_at_utc = NOW()
                        WHERE menu_id = site_id
                          AND parent_id IS NULL;

                        site_information_group_id := gen_random_uuid();
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            site_information_group_id, site_id, NULL, 0,
                            'Site Information', 'fa fa-circle-info',
                            'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;

                    SELECT id INTO bus_watcher_item_id
                    FROM menu_items
                    WHERE menu_id = main_id
                      AND display_name = 'Bus Watcher'
                      AND item_type = 'route'
                      AND config->>'path' = '/bus-watcher'
                    LIMIT 1;

                    IF bus_watcher_item_id IS NOT NULL THEN
                        UPDATE menu_items
                        SET menu_id = site_id,
                            parent_id = site_information_group_id,
                            sort_order = 0,
                            icon = 'fa fa-tower-broadcast',
                            item_type = 'template',
                            config = '{{"templateKey":"configBusWatcher"}}'::jsonb,
                            updated_at_utc = NOW()
                        WHERE id = bus_watcher_item_id;
                    ELSIF NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = site_information_group_id
                          AND display_name = 'Bus Watcher'
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, site_information_group_id, 0,
                            'Bus Watcher', 'fa fa-tower-broadcast',
                            'template', '{{"templateKey":"configBusWatcher"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = site_information_group_id
                          AND (config->>'path' = '/admin/config/events'
                               OR config->>'templateKey' = 'configEvents')
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, site_information_group_id, 1,
                            'Events', 'fa fa-bell',
                            'template', '{{"templateKey":"configEvents"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_site_information_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Add the System Health page to the Site Information group of the
    // site-config left-nav. Runs after PageTemplatesSeedSql so the
    // configSystemHealth template row exists. Idempotent via auth_seed_state
    // and a content guard so the menu item isn't double-inserted on reseed.
    private const string SiteConfigSystemHealthSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            site_information_group_id UUID;
            next_sort INT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_system_health_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO site_information_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Site Information'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF site_information_group_id IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1 FROM menu_items
                           WHERE menu_id = site_id
                             AND parent_id = site_information_group_id
                             AND config->>'templateKey' = 'configSystemHealth'
                       )
                    THEN
                        SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_sort
                        FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = site_information_group_id;

                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, site_information_group_id, next_sort,
                            'System Health', 'fa fa-heart-pulse',
                            'template', '{{"templateKey":"configSystemHealth"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_system_health_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    private const string SiteConfigStatusAppearanceSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            sitewide_group_id UUID;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_status_appearance_v1') THEN
                SELECT id
                INTO sitewide_group_id
                FROM menu_items
                WHERE menu_id = site_id
                  AND parent_id IS NULL
                  AND display_name = 'Sitewide Configuration'
                ORDER BY sort_order, created_at_utc
                LIMIT 1;

                IF sitewide_group_id IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1
                       FROM menu_items
                       WHERE menu_id = site_id
                         AND parent_id = sitewide_group_id
                         AND (config->>'path' = '/admin/config/status-appearance'
                              OR config->>'templateKey' = 'configStatusAppearance')
                   )
                THEN
                    UPDATE menu_items
                    SET sort_order = sort_order + 1,
                        updated_at_utc = NOW()
                    WHERE menu_id = site_id
                      AND parent_id = sitewide_group_id
                      AND sort_order >= 3;

                    INSERT INTO menu_items (
                        id, menu_id, parent_id, sort_order, display_name, icon,
                        item_type, config, is_visible, is_system,
                        created_at_utc, updated_at_utc
                    )
                    VALUES (
                        gen_random_uuid(), site_id, sitewide_group_id, 3,
                        'Status Appearance', 'fa fa-circle-info',
                        'template', '{{"templateKey":"configStatusAppearance"}}'::jsonb,
                        TRUE, TRUE, NOW(), NOW()
                    );
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_status_appearance_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Seed page_templates rows for every built-in template, create the
    // `standalone` system menu (a hidden container for templates that need a
    // URL but no nav placement), and migrate any pre-template menu_items rows
    // that point at a known templated path to item_type='template'. Idempotent
    // by construction: page_templates inserts use ON CONFLICT, the standalone
    // menu uses ON CONFLICT, and the route→template UPDATE filters on
    // item_type='route' so once converted, it doesn't match again.
    private const string PageTemplatesSeedSql =
        """
        DO $$
        DECLARE
            seed_actor UUID := '00000000-0000-0000-0000-000000000000';
            standalone_id UUID := '00000000-0000-0000-0001-000000000006';
        BEGIN
            INSERT INTO page_templates (id, key, name, description, default_path, is_enabled, created_at_utc, updated_at_utc)
            VALUES
              (gen_random_uuid(), 'home', 'Home', 'The main dashboard.', '/home', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'userProfile', 'User Profile', 'View and edit the signed-in user''s profile.', '/user-profile', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'manageUsers', 'Manage Users', 'Top-level user management page.', '/manage-users', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'busWatcher', 'Bus Watcher', 'Live event bus inspector.', '/bus-watcher', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminRoles', 'Roles & Permissions', 'Manage roles and assigned permissions.', '/admin/roles', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminGroups', 'Groups', 'Manage user groups.', '/admin/groups', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminGrants', 'Permission Grants', 'Direct permission grants for principals.', '/admin/grants', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminHierarchy', 'Hierarchy', 'View role / group hierarchy.', '/admin/hierarchy', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminExplain', 'Effective Permissions', 'Explain why a principal has a permission.', '/admin/explain', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminPlugins', 'Plugins', 'Manage installed plugins.', '/admin/plugins', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configGeneral', 'General (Site Config)', 'General sitewide configuration.', '/admin/config/general', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configFeatures', 'Features (Site Config)', 'Feature toggles.', '/admin/config/features', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configAppearance', 'Site Appearance (Site Config)', 'Sitewide appearance settings.', '/admin/config/appearance', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configStatusAppearance', 'Status Appearance (Site Config)', 'Status colour mapping.', '/admin/config/status-appearance', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configExternalConnections', 'External Connections (Site Config)', 'External service connections.', '/admin/config/external-connections', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPagesMenus', 'Pages / Menus (Site Config)', 'Pages and menus admin.', '/admin/config/pages-menus', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configBusWatcher', 'Bus Watcher (Site Config)', 'Bus watcher mounted inside Site Config.', '/admin/config/bus-watcher', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configEvents', 'Events (Site Config)', 'Event subscriptions and topics.', '/admin/config/events', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSystemHealth', 'System Health (Site Config)', 'Live status of every component and its connections.', '/admin/config/system-health', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityUsers', 'Manage Users (Site Config)', 'User management mounted inside Site Config.', '/admin/config/users', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityGroups', 'Manage Groups (Site Config)', 'Group management mounted inside Site Config.', '/admin/config/groups', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityRoles', 'Manage Roles (Site Config)', 'Role management mounted inside Site Config.', '/admin/config/roles', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityPermissions', 'Set Permissions (Site Config)', 'Set permissions mounted inside Site Config.', '/admin/config/permissions', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityPermissionChecker', 'Permission Checker (Site Config)', 'Effective-permission checker.', '/admin/config/permission-checker', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPlugins', 'Manage Plugins (Site Config)', 'Plugin management mounted inside Site Config.', '/admin/config/plugins', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPluginDocumentation', 'Plugin Documentation', 'How AutoNate plugins work and the patterns for working within them.', '/admin/config/plugins/documentation', TRUE, NOW(), NOW())
            ON CONFLICT (key) DO NOTHING;

            INSERT INTO menus (id, key, name, description, is_system,
                created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES (standalone_id, 'standalone', 'Standalone Pages',
                'Page templates URL-reachable but not shown in any visible nav.',
                TRUE, NOW(), seed_actor, NOW(), seed_actor)
            ON CONFLICT (key) DO NOTHING;

            -- Convert any pre-existing route-typed menu items that point at a
            -- known templated path. Once converted, the WHERE clause stops
            -- matching them, making this naturally idempotent.
            UPDATE menu_items mi
            SET item_type = 'template',
                config = jsonb_build_object('templateKey', mapping.template_key),
                updated_at_utc = NOW()
            FROM (VALUES
              ('/home', 'home'),
              ('/user-profile', 'userProfile'),
              ('/manage-users', 'manageUsers'),
              ('/bus-watcher', 'busWatcher'),
              ('/admin/roles', 'adminRoles'),
              ('/admin/groups', 'adminGroups'),
              ('/admin/grants', 'adminGrants'),
              ('/admin/hierarchy', 'adminHierarchy'),
              ('/admin/explain', 'adminExplain'),
              ('/admin/plugins', 'adminPlugins'),
              ('/admin/config/general', 'configGeneral'),
              ('/admin/config/features', 'configFeatures'),
              ('/admin/config/appearance', 'configAppearance'),
              ('/admin/config/status-appearance', 'configStatusAppearance'),
              ('/admin/config/external-connections', 'configExternalConnections'),
              ('/admin/config/pages-menus', 'configPagesMenus'),
              ('/admin/config/bus-watcher', 'configBusWatcher'),
              ('/admin/config/events', 'configEvents'),
              ('/admin/config/system-health', 'configSystemHealth'),
              ('/admin/config/users', 'configSecurityUsers'),
              ('/admin/config/groups', 'configSecurityGroups'),
              ('/admin/config/roles', 'configSecurityRoles'),
              ('/admin/config/permissions', 'configSecurityPermissions'),
              ('/admin/config/permission-checker', 'configSecurityPermissionChecker')
            ) AS mapping(path, template_key)
            WHERE mi.item_type = 'route'
              AND mi.config->>'path' = mapping.path;
        END $$;
        """;

    // Track menu items inserted by a plugin via IPluginMenus, so disable/delete
    // can sweep them. FK CASCADE handles delete; disable runs an explicit
    // DELETE WHERE created_by_plugin_id = @id (the plugins row stays put).
    //
    // Order is important: this block runs *after* PluginsSchemaSql so the
    // referenced plugins.id column exists. The column is nullable so existing
    // admin-authored menu items continue to have NULL here.
    private const string MenuItemsPluginColumnSql =
        """
        ALTER TABLE menu_items
            ADD COLUMN IF NOT EXISTS created_by_plugin_id UUID NULL;

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'menu_items_created_by_plugin_id_fkey'
            ) THEN
                ALTER TABLE menu_items
                    ADD CONSTRAINT menu_items_created_by_plugin_id_fkey
                    FOREIGN KEY (created_by_plugin_id)
                    REFERENCES plugins (id) ON DELETE CASCADE;
            END IF;
        END $$;

        CREATE INDEX IF NOT EXISTS ix_menu_items_created_by_plugin_id
            ON menu_items (created_by_plugin_id)
            WHERE created_by_plugin_id IS NOT NULL;
        """;

    private const string PluginsSchemaSql =
        """
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
        """;

    // Plugin-owned database storage. Each installed plugin is provisioned a
    // dedicated Postgres LOGIN role and schema (`plg_<code>`); the plugin
    // connects as that role so the database itself enforces "write only to my
    // own schema, read-only everywhere else." This block adds the per-plugin
    // identity columns and bootstraps the shared `plg_readers` group role that
    // every plugin role inherits read grants from.
    //
    // - `code` is the 8-char namespace identifier (`[a-z][a-z0-9]{7}`),
    //   nullable for forward compatibility with rows uploaded before this
    //   migration; new uploads always populate it.
    // - `role_password_encrypted` stores the per-plugin role password,
    //   protected with IDataProtector. Rotated on plugin re-provisioning.
    // - `plg_readers` is a NOLOGIN group role granted USAGE on `public` and
    //   SELECT on all current/future tables/sequences. Per-plugin schemas
    //   grant USAGE + SELECT-default to this same role at provisioning time,
    //   which gives every plugin read access to every other plugin's data
    //   without per-pair grants.
    private const string PluginDataIsolationSql =
        """
        ALTER TABLE plugins
            ADD COLUMN IF NOT EXISTS code TEXT NULL;

        ALTER TABLE plugins
            ADD COLUMN IF NOT EXISTS role_password_encrypted BYTEA NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_plugins_code
            ON plugins (code)
            WHERE code IS NOT NULL;

        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'plg_readers') THEN
                CREATE ROLE plg_readers NOLOGIN;
            END IF;
        END $$;

        GRANT USAGE ON SCHEMA public TO plg_readers;
        GRANT SELECT ON ALL TABLES IN SCHEMA public TO plg_readers;
        GRANT SELECT, USAGE ON ALL SEQUENCES IN SCHEMA public TO plg_readers;

        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT ON TABLES TO plg_readers;

        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT, USAGE ON SEQUENCES TO plg_readers;
        """;

    // The Plugins entry now lives in the Site Configuration menu (see
    // PluginsSiteConfigMenuSql below); remove any prior icon-menu placement.
    // Idempotent: a DELETE matching nothing is a no-op on subsequent startups.
    private const string PluginsIconMenuRemovalSql =
        """
        DO $$
        DECLARE
            icon_menu_id UUID;
        BEGIN
            SELECT id INTO icon_menu_id FROM menus WHERE key = 'icon' LIMIT 1;
            IF icon_menu_id IS NULL THEN
                RETURN;
            END IF;

            DELETE FROM menu_items
            WHERE menu_id = icon_menu_id
              AND (config->>'path' = '/admin/plugins'
                   OR config->>'templateKey' = 'adminPlugins');
        END $$;
        """;

    // Add a "Plugins" group to the Site Configuration menu, with two children:
    // "Manage Plugins" (the existing admin page mounted inside the config
    // shell as `configPlugins`) and "Documentation" (a long-form HTML doc
    // rendered by the `configPluginDocumentation` template).
    //
    // Idempotent: gated on whether a Plugins group already exists under
    // site-config. The site-config menu seed (initial DO block above) only
    // creates the menu the first time, so this block runs on existing
    // environments to add the new group without rebuilding any other section.
    private const string PluginsSiteConfigMenuSql =
        """
        DO $$
        DECLARE
            site_id UUID;
            g UUID;
            next_order INTEGER;
        BEGIN
            SELECT id INTO site_id FROM menus WHERE key = 'site-config' LIMIT 1;
            IF site_id IS NULL THEN
                RETURN;
            END IF;

            IF EXISTS (
                SELECT 1 FROM menu_items
                WHERE menu_id = site_id
                  AND parent_id IS NULL
                  AND item_type = 'group'
                  AND display_name = 'Plugins'
            ) THEN
                RETURN;
            END IF;

            SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_order
            FROM menu_items
            WHERE menu_id = site_id AND parent_id IS NULL;

            g := gen_random_uuid();
            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system,
                created_at_utc, updated_at_utc
            )
            VALUES (
                g, site_id, NULL, next_order, 'Plugins', 'fa fa-puzzle-piece',
                'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW()
            );

            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system,
                created_at_utc, updated_at_utc
            )
            VALUES (
                gen_random_uuid(), site_id, g, 0, 'Manage Plugins', 'fa fa-screwdriver-wrench',
                'template', '{{"templateKey":"configPlugins"}}'::jsonb,
                TRUE, TRUE, NOW(), NOW()
            );

            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system,
                created_at_utc, updated_at_utc
            )
            VALUES (
                gen_random_uuid(), site_id, g, 1, 'Documentation', 'fa fa-book',
                'template', '{{"templateKey":"configPluginDocumentation"}}'::jsonb,
                TRUE, TRUE, NOW(), NOW()
            );
        END $$;
        """;

    private const string WorkflowExecutionErrorsSql =
        """
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
        """;

    // Tracks who actually triggered task completion. Flowable's historic
    // task only records the assignee, so without this an admin override
    // is indistinguishable from the assignee completing the task.
    private const string WorkflowTaskCompletionsSql =
        """
        CREATE TABLE IF NOT EXISTS workflow_task_completions (
            task_id TEXT PRIMARY KEY,
            completed_by_user_id TEXT NOT NULL,
            completed_at_utc TIMESTAMPTZ NOT NULL,
            was_override BOOLEAN NOT NULL DEFAULT FALSE
        );

        CREATE INDEX IF NOT EXISTS ix_workflow_task_completions_completed_by
            ON workflow_task_completions (completed_by_user_id);
        """;

    // Generic key-value store backing the admin "Site Configuration" pages.
    // Sparse on purpose: missing rows mean "use the default value declared in
    // the SiteSettingsRegistry." Adding a new feature flag or option is just a
    // registry entry on the C# side + a UI control; no schema change.
    private const string SiteSettingsSql =
        """
        CREATE TABLE IF NOT EXISTS site_settings (
            key TEXT PRIMARY KEY,
            value_json JSONB NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );
        """;

    // Per-user notification feed for in-app alerts (record assigned, user task
    // assigned, etc). Notifications are owned by the recipient — the bell icon
    // and /notifications page each filter to user_id = current user.
    private const string NotificationsSql =
        """
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
            created_at_utc TIMESTAMPTZ NOT NULL,
            read_at_utc TIMESTAMPTZ NULL
        );

        CREATE INDEX IF NOT EXISTS ix_notifications_user_created
            ON notifications (user_id, created_at_utc DESC);

        CREATE INDEX IF NOT EXISTS ix_notifications_user_unread
            ON notifications (user_id, is_read);
        """;

    // Phase 5 of the audit-events plan: durable outbox between event publishers
    // and Dapr/NATS. EfCoreAuditEventOutbox writes one row per published event
    // in its own transaction (post-domain-commit, no atomic enqueue today —
    // that's a future refactor). AuditOutboxDispatcher polls undispatched rows
    // with FOR UPDATE SKIP LOCKED, posts to Dapr, and marks them dispatched.
    private const string AuditOutboxSchemaSql =
        """
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
        """;

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowVersioningSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowExecutionErrorsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowTaskCompletionsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsDataSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsEdgesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsCommentsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AuthorizationSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordEdgeBackfillSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordEdgeShadowBackfillSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(EntityEdgeHotIndexesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RolePermissionsToGrantsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PageTemplatesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(MenusSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(IconMenuWrapSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigStatusAppearanceSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigSiteInformationSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginDataIsolationSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(MenuItemsPluginColumnSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PageTemplatesPluginColumnsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PageTemplatesSeedSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginsIconMenuRemovalSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginsSiteConfigMenuSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigSystemHealthSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(NotificationsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AuditOutboxSchemaSql, cancellationToken);

        var authOptions = scope.ServiceProvider
            .GetService<IOptions<AuthorizationOptions>>()?.Value
            ?? new AuthorizationOptions();
        if (authOptions.AssignSuperAdminToAllExistingUsers)
        {
            await dbContext.Database.ExecuteSqlRawAsync(SuperAdminBackfillSql, cancellationToken);
        }
    }
}
