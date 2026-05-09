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

    private const string WorkflowDefaultVariablesSql =
        """
        ALTER TABLE workflow_models
            ADD COLUMN IF NOT EXISTS default_variables JSONB NULL;
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

    private const string RecordWatchesSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS record_watches (
            user_id UUID NOT NULL,
            record_id UUID NOT NULL REFERENCES records (id) ON DELETE CASCADE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (user_id, record_id)
        );

        CREATE INDEX IF NOT EXISTS ix_record_watches_user
            ON record_watches (user_id, created_at_utc DESC);

        CREATE INDEX IF NOT EXISTS ix_record_watches_record
            ON record_watches (record_id);
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
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );

        -- Older DBs created when the entity still had DefaultPath have a
        -- default_path TEXT NOT NULL UNIQUE column. The entity dropped it in
        -- eb87a53d (every template menu item now owns its URL), so the
        -- column has to go before the seed INSERT runs without it.
        ALTER TABLE page_templates
            DROP COLUMN IF EXISTS default_path;

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
            INSERT INTO page_templates (id, key, name, description, is_enabled, created_at_utc, updated_at_utc)
            VALUES
              (gen_random_uuid(), 'home', 'Home', 'The main dashboard.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'userProfile', 'User Profile', 'View and edit the signed-in user''s profile.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'manageUsers', 'Manage Users', 'Top-level user management page.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'busWatcher', 'Bus Watcher', 'Live event bus inspector.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminRoles', 'Roles & Permissions', 'Manage roles and assigned permissions.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminGroups', 'Groups', 'Manage user groups.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminGrants', 'Permission Grants', 'Direct permission grants for principals.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminHierarchy', 'Hierarchy', 'View role / group hierarchy.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminExplain', 'Effective Permissions', 'Explain why a principal has a permission.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'adminPlugins', 'Plugins', 'Manage installed plugins.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configGeneral', 'General (Site Config)', 'General sitewide configuration.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configFeatures', 'Features (Site Config)', 'Feature toggles.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configAppearance', 'Site Appearance (Site Config)', 'Sitewide appearance settings.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configStatusAppearance', 'Status Appearance (Site Config)', 'Status colour mapping.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configExternalConnections', 'External Connections (Site Config)', 'External service connections.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPagesMenus', 'Pages / Menus (Site Config)', 'Pages and menus admin.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configBusWatcher', 'Bus Watcher (Site Config)', 'Bus watcher mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configEvents', 'Events (Site Config)', 'Event subscriptions and topics.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSystemHealth', 'System Health (Site Config)', 'Live status of every component and its connections.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSystemIssues', 'System Issues (Site Config)', 'Persistent log of issues detectors have surfaced; ack/resolve/auto-remediate.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityUsers', 'Manage Users (Site Config)', 'User management mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityGroups', 'Manage Groups (Site Config)', 'Group management mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityRoles', 'Manage Roles (Site Config)', 'Role management mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityPermissions', 'Set Permissions (Site Config)', 'Set permissions mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configSecurityPermissionChecker', 'Permission Checker (Site Config)', 'Effective-permission checker.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPlugins', 'Manage Plugins (Site Config)', 'Plugin management mounted inside Site Config.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configPluginDocumentation', 'Plugin Documentation', 'How AutoNate plugins work and the patterns for working within them.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configForms', 'Forms (Site Config)', 'Define and manage form definitions.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configFormMappings', 'Form Mappings (Site Config)', 'Map forms to record types and fields.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configChatbotSettings', 'Chatbot Settings (Site Config)', 'Configure agent capabilities; applies to the next message.', TRUE, NOW(), NOW())
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
              ('/admin/config/system-issues', 'configSystemIssues'),
              ('/admin/config/users', 'configSecurityUsers'),
              ('/admin/config/groups', 'configSecurityGroups'),
              ('/admin/config/roles', 'configSecurityRoles'),
              ('/admin/config/permissions', 'configSecurityPermissions'),
              ('/admin/config/permission-checker', 'configSecurityPermissionChecker'),
              ('/admin/config/forms', 'configForms'),
              ('/admin/config/form-mappings', 'configFormMappings')
            ) AS mapping(path, template_key)
            WHERE mi.item_type = 'route'
              AND mi.config->>'path' = mapping.path;
        END $$;
        """;

    // Cartoonish 200x150 SVG thumbnails (one per built-in template,
    // base64-encoded as data: URIs) — generated by /tmp/gen-thumbs.mjs and
    // embedded inline so fresh installs and CI tests get the same picker
    // visuals. Each UPDATE only fills NULLs, so any admin-edited
    // thumbnail_url is preserved across restarts.
    private const string PageTemplatesThumbnailSeedSql =
        """
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjEwIiB5PSIzMiIgd2lkdGg9IjQyIiBoZWlnaHQ9IjI4IiByeD0iMyIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjU2IiB5PSIzMiIgd2lkdGg9IjQyIiBoZWlnaHQ9IjI4IiByeD0iMyIgZmlsbD0iIzM0OGZlMiIvPjxyZWN0IHg9IjEwMiIgeT0iMzIiIHdpZHRoPSI0MiIgaGVpZ2h0PSIyOCIgcng9IjMiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIxNDgiIHk9IjMyIiB3aWR0aD0iNDIiIGhlaWdodD0iMjgiIHJ4PSIzIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTQiIHk9IjM4IiB3aWR0aD0iMjAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNzUiLz48cmVjdCB4PSIxNCIgeT0iNDYiIHdpZHRoPSIxNCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIvPjxyZWN0IHg9IjYwIiB5PSIzOCIgd2lkdGg9IjIwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjc1Ii8+PHJlY3QgeD0iNjAiIHk9IjQ2IiB3aWR0aD0iMTQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSIxMDYiIHk9IjM4IiB3aWR0aD0iMjAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNzUiLz48cmVjdCB4PSIxMDYiIHk9IjQ2IiB3aWR0aD0iMTQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSIxNTIiIHk9IjM4IiB3aWR0aD0iMjAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNzUiLz48cmVjdCB4PSIxNTIiIHk9IjQ2IiB3aWR0aD0iMTQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSIxMCIgeT0iNjYiIHdpZHRoPSIxODAiIGhlaWdodD0iNzYiIHJ4PSI0IiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cG9seWxpbmUgcG9pbnRzPSIxOCwxMjggNDgsMTEyIDc4LDEyMCAxMDgsOTAgMTM4LDEwNCAxNjgsODAgMTg0LDk0IiBzdHJva2U9IiMwMGFjYWMiIHN0cm9rZS13aWR0aD0iMiIgZmlsbD0ibm9uZSIvPjxwb2x5bGluZSBwb2ludHM9IjE4LDEzNiA0OCwxMjYgNzgsMTMyIDEwOCwxMTIgMTM4LDEyMiAxNjgsMTA0IDE4NCwxMTYiIHN0cm9rZT0iIzM0OGZlMiIgc3Ryb2tlLXdpZHRoPSIxLjUiIGZpbGw9Im5vbmUiIHN0cm9rZS1kYXNoYXJyYXk9IjMgMiIvPjxjaXJjbGUgY3g9IjE4IiBjeT0iMTI4IiByPSIyIiBmaWxsPSIjMDBhY2FjIi8+PGNpcmNsZSBjeD0iNDgiIGN5PSIxMTIiIHI9IjIiIGZpbGw9IiMwMGFjYWMiLz48Y2lyY2xlIGN4PSI3OCIgY3k9IjEyMCIgcj0iMiIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjEwOCIgY3k9IjkwIiByPSIyIiBmaWxsPSIjMDBhY2FjIi8+PGNpcmNsZSBjeD0iMTM4IiBjeT0iMTA0IiByPSIyIiBmaWxsPSIjMDBhY2FjIi8+PGNpcmNsZSBjeD0iMTY4IiBjeT0iODAiIHI9IjIiIGZpbGw9IiMwMGFjYWMiLz48Y2lyY2xlIGN4PSIxODQiIGN5PSI5NCIgcj0iMiIgZmlsbD0iIzAwYWNhYyIvPjwvc3ZnPg==' WHERE key = 'home' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlYWY0ZmIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxjaXJjbGUgY3g9IjU2IiBjeT0iNTgiIHI9IjIwIiBmaWxsPSIjMzQ4ZmUyIi8+PGNpcmNsZSBjeD0iNTYiIGN5PSI1MiIgcj0iNi41IiBmaWxsPSIjZmZmZmZmIi8+PHBhdGggZD0iTSA0MCA3MiBRIDU2IDYwIDcyIDcyIEwgNzIgNzggTCA0MCA3OCBaIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeD0iODYiIHk9IjQ0IiB3aWR0aD0iODAiIGhlaWdodD0iNiIgcng9IjIiIGZpbGw9IiMyZDM1M2MiLz48cmVjdCB4PSI4NiIgeT0iNTYiIHdpZHRoPSI2MCIgaGVpZ2h0PSI0IiByeD0iMS41IiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9IjY0IiB3aWR0aD0iNzAiIGhlaWdodD0iNCIgcng9IjEuNSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0IiB5PSI5NCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNCIgeT0iMTA0IiB3aWR0aD0iMTcyIiBoZWlnaHQ9IjEyIiByeD0iMyIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTQiIHk9IjEyMyIgd2lkdGg9IjM1IiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNCIgeT0iMTMzIiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMyIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTQyIiB5PSIxMzMiIHdpZHRoPSI0NCIgaGVpZ2h0PSIxMSIgcng9IjMiIGZpbGw9IiMwMGFjYWMiLz48L3N2Zz4=' WHERE key = 'userProfile' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjEwIiB5PSIzMiIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiM3MjdjYjYiIG9wYWNpdHk9IjAuMTgiLz48cmVjdCB4PSIxNCIgeT0iMzYiIHdpZHRoPSI0MCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjYwIiB5PSIzNiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTAiIHk9IjUyIiB3aWR0aD0iMTgwIiBoZWlnaHQ9IjEzIiByeD0iMS41IiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSIyMCIgY3k9IjU4LjUiIHI9IjQuNSIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjI5IiB5PSI1NiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjkiIHk9IjYxIiB3aWR0aD0iMzgiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9Ijg2IiB5PSI1NyIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTUyIiB5PSI1Ni41IiB3aWR0aD0iMzIiIGhlaWdodD0iNSIgcng9IjIuNSIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwIiB5PSI2NiIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxMyIgcng9IjEuNSIgZmlsbD0iI2U5ZWNlZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iMjAiIGN5PSI3Mi41IiByPSI0LjUiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIyOSIgeT0iNzAiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSI3NSIgd2lkdGg9IjM4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSI4NiIgeT0iNzEiIHdpZHRoPSI1NSIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE1MiIgeT0iNzAuNSIgd2lkdGg9IjMyIiBoZWlnaHQ9IjUiIHJ4PSIyLjUiIGZpbGw9IiNmNTljMWEiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIxMCIgeT0iODAiIHdpZHRoPSIxODAiIGhlaWdodD0iMTMiIHJ4PSIxLjUiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iODYuNSIgcj0iNC41IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMjkiIHk9Ijg0IiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSIyOSIgeT0iODkiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9Ijg1IiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9Ijg0LjUiIHdpZHRoPSIzMiIgaGVpZ2h0PSI1IiByeD0iMi41IiBmaWxsPSIjYWRiNWJkIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTAiIHk9Ijk0IiB3aWR0aD0iMTgwIiBoZWlnaHQ9IjEzIiByeD0iMS41IiBmaWxsPSIjZTllY2VmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSIyMCIgY3k9IjEwMC41IiByPSI0LjUiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIyOSIgeT0iOTgiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSIxMDMiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9Ijk5IiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9Ijk4LjUiIHdpZHRoPSIzMiIgaGVpZ2h0PSI1IiByeD0iMi41IiBmaWxsPSIjMzJhOTMyIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTAiIHk9IjEwOCIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxMyIgcng9IjEuNSIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iMjAiIGN5PSIxMTQuNSIgcj0iNC41IiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMjkiIHk9IjExMiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjkiIHk9IjExNyIgd2lkdGg9IjM4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSI4NiIgeT0iMTEzIiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9IjExMi41IiB3aWR0aD0iMzIiIGhlaWdodD0iNSIgcng9IjIuNSIgZmlsbD0iI2Y1OWMxYSIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwIiB5PSIxMjIiIHdpZHRoPSIxODAiIGhlaWdodD0iMTMiIHJ4PSIxLjUiIGZpbGw9IiNlOWVjZWYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iMTI4LjUiIHI9IjQuNSIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjI5IiB5PSIxMjYiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSIxMzEiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9IjEyNyIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTUyIiB5PSIxMjYuNSIgd2lkdGg9IjMyIiBoZWlnaHQ9IjUiIHJ4PSIyLjUiIGZpbGw9IiNhZGI1YmQiIG9wYWNpdHk9IjAuODUiLz48L3N2Zz4=' WHERE key = 'manageUsers' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjYiIHk9IjMwIiB3aWR0aD0iMTg4IiBoZWlnaHQ9IjExNiIgcng9IjQiIGZpbGw9IiMwZjE3MmEiLz48cmVjdCB4PSIxNCIgeT0iNDAiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5YjZkNiIvPjxyZWN0IHg9IjYyIiB5PSI0MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTQiIHk9IjQ2IiB3aWR0aD0iMTYwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iNTUiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZDI0ZCIvPjxyZWN0IHg9IjYyIiB5PSI1NSIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDliNmQ2Ii8+PHJlY3QgeD0iMTQiIHk9IjYxIiB3aWR0aD0iMTMwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iNzAiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjYyIiB5PSI3MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZjU5YzFhIi8+PHJlY3QgeD0iMTQiIHk9Ijc2IiB3aWR0aD0iMTQ1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iODUiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5YjZkNiIvPjxyZWN0IHg9IjYyIiB5PSI4NSIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTQiIHk9IjkxIiB3aWR0aD0iMTE1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTAwIiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSI2MiIgeT0iMTAwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZjViNTciLz48cmVjdCB4PSIxNCIgeT0iMTA2IiB3aWR0aD0iMTU1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTE1IiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmQyNGQiLz48cmVjdCB4PSI2MiIgeT0iMTE1IiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmQyNGQiLz48cmVjdCB4PSIxNCIgeT0iMTIxIiB3aWR0aD0iMTI1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTMwIiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OWI2ZDYiLz48cmVjdCB4PSI2MiIgeT0iMTMwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNCIgeT0iMTM2IiB3aWR0aD0iMTQwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48L3N2Zz4=' WHERE key = 'busWatcher' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlOGY4ZWUiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjYwIiB5PSIzMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjEwIiByeD0iMiIgZmlsbD0iIzAwYWNhYyIgb3BhY2l0eT0iMC4yIi8+PHJlY3QgeD0iOTgiIHk9IjMyIiB3aWR0aD0iMzQiIGhlaWdodD0iMTAiIHJ4PSIyIiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjIiLz48cmVjdCB4PSIxMzYiIHk9IjMyIiB3aWR0aD0iMzQiIGhlaWdodD0iMTAiIHJ4PSIyIiBmaWxsPSIjZjU5YzFhIiBvcGFjaXR5PSIwLjIiLz48cmVjdCB4PSIxNCIgeT0iNTAiIHdpZHRoPSI0MiIgaGVpZ2h0PSI2IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iNjAiIHk9IjQ2IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSI5OCIgeT0iNDYiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjEzNiIgeT0iNDYiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9Ijc3IiBjeT0iNTUiIHI9IjYiIGZpbGw9IiMwMGFjYWMiLz48cGF0aCBkPSJNIDc0IDU1IEwgNzYgNTcgTCA4MCA1MyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PGNpcmNsZSBjeD0iMTUzIiBjeT0iNTUiIHI9IjYiIGZpbGw9IiNmNTljMWEiLz48cGF0aCBkPSJNIDE1MCA1NSBMIDE1MiA1NyBMIDE1NiA1MyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PHJlY3QgeD0iMTQiIHk9IjcyIiB3aWR0aD0iNDIiIGhlaWdodD0iNiIgcng9IjEuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjYwIiB5PSI2OCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iOTgiIHk9IjY4IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSIxMzYiIHk9IjY4IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSI3NyIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjMDBhY2FjIi8+PHBhdGggZD0iTSA3NCA3NyBMIDc2IDc5IEwgODAgNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxjaXJjbGUgY3g9IjExNSIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjMzQ4ZmUyIi8+PHBhdGggZD0iTSAxMTIgNzcgTCAxMTQgNzkgTCAxMTggNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxjaXJjbGUgY3g9IjE1MyIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjZjU5YzFhIi8+PHBhdGggZD0iTSAxNTAgNzcgTCAxNTIgNzkgTCAxNTYgNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxyZWN0IHg9IjE0IiB5PSI5NCIgd2lkdGg9IjQyIiBoZWlnaHQ9IjYiIHJ4PSIxLjUiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI2MCIgeT0iOTAiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9Ijk4IiB5PSI5MCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTM2IiB5PSI5MCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iNzciIGN5PSI5OSIgcj0iNiIgZmlsbD0iIzAwYWNhYyIvPjxwYXRoIGQ9Ik0gNzQgOTkgTCA3NiAxMDEgTCA4MCA5NyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PGNpcmNsZSBjeD0iMTE1IiBjeT0iOTkiIHI9IjYiIGZpbGw9IiMzNDhmZTIiLz48cGF0aCBkPSJNIDExMiA5OSBMIDExNCAxMDEgTCAxMTggOTciIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxyZWN0IHg9IjE0IiB5PSIxMTYiIHdpZHRoPSI0MiIgaGVpZ2h0PSI2IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iNjAiIHk9IjExMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iOTgiIHk9IjExMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTM2IiB5PSIxMTIiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExNSIgY3k9IjEyMSIgcj0iNiIgZmlsbD0iIzM0OGZlMiIvPjxwYXRoIGQ9Ik0gMTEyIDEyMSBMIDExNCAxMjMgTCAxMTggMTE5IiBzdHJva2U9IiNmZmZmZmYiIHN0cm9rZS13aWR0aD0iMS42IiBmaWxsPSJub25lIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48Y2lyY2xlIGN4PSIxNTMiIGN5PSIxMjEiIHI9IjYiIGZpbGw9IiNmNTljMWEiLz48cGF0aCBkPSJNIDE1MCAxMjEgTCAxNTIgMTIzIEwgMTU2IDExOSIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PC9zdmc+' WHERE key = 'adminRoles' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlZWVlZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxjaXJjbGUgY3g9IjY1IiBjeT0iODAiIHI9IjMyIiBmaWxsPSIjMDBhY2FjIiBvcGFjaXR5PSIwLjE4Ii8+PGNpcmNsZSBjeD0iMTIwIiBjeT0iNjgiIHI9IjI4IiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjIwIi8+PGNpcmNsZSBjeD0iMTM1IiBjeT0iMTE1IiByPSIyNiIgZmlsbD0iI2ZiNTU5NyIgb3BhY2l0eT0iMC4yMCIvPjxjaXJjbGUgY3g9IjU1IiBjeT0iNzUiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjU1IiBjeT0iNzMuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjcwIiBjeT0iOTAiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjcwIiBjeT0iODguNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjgwIiBjeT0iNzAiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjgwIiBjeT0iNjguNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjExNSIgY3k9IjYwIiByPSI1LjUiIGZpbGw9IiMzNDhmZTIiLz48Y2lyY2xlIGN4PSIxMTUiIGN5PSI1OC41IiByPSIyIiBmaWxsPSIjZmZmZmZmIi8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iNzUiIHI9IjUuNSIgZmlsbD0iIzM0OGZlMiIvPjxjaXJjbGUgY3g9IjEyOCIgY3k9IjczLjUiIHI9IjIiIGZpbGw9IiNmZmZmZmYiLz48Y2lyY2xlIGN4PSIxMDgiIGN5PSI3OCIgcj0iNS41IiBmaWxsPSIjMzQ4ZmUyIi8+PGNpcmNsZSBjeD0iMTA4IiBjeT0iNzYuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjEyOCIgY3k9IjExMCIgcj0iNS41IiBmaWxsPSIjZmI1NTk3Ii8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iMTA4LjUiIHI9IjIiIGZpbGw9IiNmZmZmZmYiLz48Y2lyY2xlIGN4PSIxNDAiIGN5PSIxMjIiIHI9IjUuNSIgZmlsbD0iI2ZiNTU5NyIvPjxjaXJjbGUgY3g9IjE0MCIgY3k9IjEyMC41IiByPSIyIiBmaWxsPSIjZmZmZmZmIi8+PGNpcmNsZSBjeD0iMTQ4IiBjeT0iMTA1IiByPSI1LjUiIGZpbGw9IiNmYjU1OTciLz48Y2lyY2xlIGN4PSIxNDgiIGN5PSIxMDMuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxyZWN0IHg9IjE0IiB5PSIxMzUiIHdpZHRoPSIxNzAiIGhlaWdodD0iOCIgcng9IjIiIGZpbGw9IiNlOWVjZWYiLz48L3N2Zz4=' WHERE key = 'adminGroups' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmZmY4ZWIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxnIHRyYW5zZm9ybT0idHJhbnNsYXRlKDQwLCA2MCkgcm90YXRlKC0yMCkiPjxjaXJjbGUgY3g9IjAiIGN5PSIwIiByPSIxOCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZjU5YzFhIiBzdHJva2Utd2lkdGg9IjYiLz48cmVjdCB4PSIxNCIgeT0iLTQiIHdpZHRoPSI0OCIgaGVpZ2h0PSI4IiBmaWxsPSIjZjU5YzFhIi8+PHJlY3QgeD0iNDgiIHk9Ii00IiB3aWR0aD0iNiIgaGVpZ2h0PSIxNCIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjU4IiB5PSItNCIgd2lkdGg9IjQiIGhlaWdodD0iMTAiIGZpbGw9IiNmNTljMWEiLz48L2c+PHJlY3QgeD0iMTAwIiB5PSI0MiIgd2lkdGg9Ijg2IiBoZWlnaHQ9IjE0IiByeD0iMiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjEwNCIgeT0iNDYiIHdpZHRoPSI2MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwNCIgeT0iNTEiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC42NSIvPjxyZWN0IHg9IjEwMCIgeT0iNjIiIHdpZHRoPSI4NiIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiMzNDhmZTIiLz48cmVjdCB4PSIxMDQiIHk9IjY2IiB3aWR0aD0iNDgiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIxMDQiIHk9IjcxIiB3aWR0aD0iMzQiIGhlaWdodD0iMiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIxMDAiIHk9IjgyIiB3aWR0aD0iODYiIGhlaWdodD0iMTQiIHJ4PSIyIiBmaWxsPSIjNzI3Y2I2Ii8+PHJlY3QgeD0iMTA0IiB5PSI4NiIgd2lkdGg9IjU2IiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTA0IiB5PSI5MSIgd2lkdGg9IjM2IiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjY1Ii8+PHJlY3QgeD0iMTQiIHk9IjExOCIgd2lkdGg9IjE3MiIgaGVpZ2h0PSIyMiIgcng9IjMiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjIwIiB5PSIxMjQiIHdpZHRoPSI2MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjIwIiB5PSIxMzEiIHdpZHRoPSIxMDAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48L3N2Zz4=' WHERE key = 'adminGrants' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlYWY0ZmIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjgwIiB5PSIzNiIgd2lkdGg9IjQwIiBoZWlnaHQ9IjE2IiByeD0iMyIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9Ijg0IiB5PSI0MiIgd2lkdGg9IjMyIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PGxpbmUgeDE9IjEwMCIgeTE9IjUyIiB4Mj0iMTAwIiB5Mj0iNjQiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjUiLz48bGluZSB4MT0iNDAiIHkxPSI2NCIgeDI9IjE2MCIgeTI9IjY0IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS41Ii8+PGxpbmUgeDE9IjQwIiB5MT0iNjQiIHgyPSI0MCIgeTI9Ijc2IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS41Ii8+PGxpbmUgeDE9IjEwMCIgeTE9IjY0IiB4Mj0iMTAwIiB5Mj0iNzYiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjUiLz48bGluZSB4MT0iMTYwIiB5MT0iNjQiIHgyPSIxNjAiIHkyPSI3NiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuNSIvPjxyZWN0IHg9IjIwIiB5PSI3NiIgd2lkdGg9IjQwIiBoZWlnaHQ9IjE2IiByeD0iMyIgZmlsbD0iIzM0OGZlMiIvPjxyZWN0IHg9IjgwIiB5PSI3NiIgd2lkdGg9IjQwIiBoZWlnaHQ9IjE2IiByeD0iMyIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjE0MCIgeT0iNzYiIHdpZHRoPSI0MCIgaGVpZ2h0PSIxNiIgcng9IjMiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIyNCIgeT0iODIiIHdpZHRoPSIzMiIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9Ijg0IiB5PSI4MiIgd2lkdGg9IjMyIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTQ0IiB5PSI4MiIgd2lkdGg9IjMyIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PGxpbmUgeDE9IjEwMCIgeTE9IjkyIiB4Mj0iMTAwIiB5Mj0iMTA0IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS41Ii8+PGxpbmUgeDE9IjgwIiB5MT0iMTA0IiB4Mj0iMTIwIiB5Mj0iMTA0IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS41Ii8+PGxpbmUgeDE9IjgwIiB5MT0iMTA0IiB4Mj0iODAiIHkyPSIxMTQiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjUiLz48bGluZSB4MT0iMTIwIiB5MT0iMTA0IiB4Mj0iMTIwIiB5Mj0iMTE0IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS41Ii8+PHJlY3QgeD0iNjIiIHk9IjExNCIgd2lkdGg9IjM2IiBoZWlnaHQ9IjE0IiByeD0iMyIgZmlsbD0iIzQ5YjZkNiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwMiIgeT0iMTE0IiB3aWR0aD0iMzYiIGhlaWdodD0iMTQiIHJ4PSIzIiBmaWxsPSIjMzJhOTMyIiBvcGFjaXR5PSIwLjg1Ii8+PC9zdmc+' WHERE key = 'adminHierarchy' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlZWVlZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNCIgd2lkdGg9IjEyMCIgaGVpZ2h0PSIxMCIgcng9IjIiIGZpbGw9IiNlOWVjZWYiLz48cmVjdCB4PSIxNCIgeT0iNDgiIHdpZHRoPSI4MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0IiB5PSI2MiIgd2lkdGg9IjYiIGhlaWdodD0iNiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjI2IiB5PSI2MiIgd2lkdGg9IjYwIiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjAiIHk9IjcyIiB3aWR0aD0iNiIgaGVpZ2h0PSI2IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMzIiIHk9IjcyIiB3aWR0aD0iNTAiIGhlaWdodD0iNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIyMCIgeT0iODIiIHdpZHRoPSI2IiBoZWlnaHQ9IjYiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIzMiIgeT0iODIiIHdpZHRoPSI0NCIgaGVpZ2h0PSI1IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0IiB5PSI5NCIgd2lkdGg9IjYiIGhlaWdodD0iNiIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjI2IiB5PSI5NCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PGcgdHJhbnNmb3JtPSJ0cmFuc2xhdGUoMTQwLCA5MCkiPjxjaXJjbGUgY3g9IjAiIGN5PSIwIiByPSIyMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjNzI3Y2I2IiBzdHJva2Utd2lkdGg9IjQiLz48bGluZSB4MT0iMTYiIHkxPSIxNiIgeDI9IjMyIiB5Mj0iMzIiIHN0cm9rZT0iIzcyN2NiNiIgc3Ryb2tlLXdpZHRoPSI2IiBzdHJva2UtbGluZWNhcD0icm91bmQiLz48cGF0aCBkPSJNIC04IDAgTCAtMiA2IEwgOCAtNiIgc3Ryb2tlPSIjMzJhOTMyIiBzdHJva2Utd2lkdGg9IjMiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvZz48L3N2Zz4=' WHERE key = 'adminExplain' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNCIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQ4IiByeD0iNCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjY5IiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48Y2lyY2xlIGN4PSIxNCIgY3k9IjU4IiByPSI2IiBmaWxsPSIjZjhmOWZhIi8+PHJlY3QgeD0iMjAiIHk9IjQyIiB3aWR0aD0iMzAiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOSIvPjxyZWN0IHg9IjIwIiB5PSI1MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjY1Ii8+PHJlY3QgeD0iNzMiIHk9IjM0IiB3aWR0aD0iNTUiIGhlaWdodD0iNDgiIHJ4PSI0IiBmaWxsPSIjNzI3Y2I2Ii8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSI3OSIgeT0iNDIiIHdpZHRoPSIzMCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45Ii8+PHJlY3QgeD0iNzkiIHk9IjUwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIxMzIiIHk9IjM0IiB3aWR0aD0iNTUiIGhlaWdodD0iNDgiIHJ4PSI0IiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMTMyIiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSIxMzgiIHk9IjQyIiB3aWR0aD0iMzAiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOSIvPjxyZWN0IHg9IjEzOCIgeT0iNTAiIHdpZHRoPSIyMiIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC42NSIvPjxyZWN0IHg9IjE0IiB5PSI5MiIgd2lkdGg9Ijg0IiBoZWlnaHQ9IjUwIiByeD0iNCIgZmlsbD0iI2ZiNTU5NyIvPjxyZWN0IHg9IjIwIiB5PSIxMDAiIHdpZHRoPSI0MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45Ii8+PHJlY3QgeD0iMjAiIHk9IjEwOCIgd2lkdGg9IjYwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIyMCIgeT0iMTE1IiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNyIvPjxyZWN0IHg9IjIwIiB5PSIxMjgiIHdpZHRoPSIzMCIgaGVpZ2h0PSI5IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwMiIgeT0iOTIiIHdpZHRoPSI4NCIgaGVpZ2h0PSI1MCIgcng9IjQiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxMDgiIHk9IjEwMCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjkiLz48cmVjdCB4PSIxMDgiIHk9IjEwOCIgd2lkdGg9IjYwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIxMDgiIHk9IjExNSIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIxMDgiIHk9IjEyOCIgd2lkdGg9IjMwIiBoZWlnaHQ9IjkiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PC9zdmc+' WHERE key = 'adminPlugins' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxnIHRyYW5zZm9ybT0idHJhbnNsYXRlKDUwLCA3MCkiPjxnIGZpbGw9IiMwMGFjYWMiPjxyZWN0IHg9Ii0zIiB5PSItMjYiIHdpZHRoPSI2IiBoZWlnaHQ9IjEwIiByeD0iMS41IiB0cmFuc2Zvcm09InJvdGF0ZSgwKSIvPjxyZWN0IHg9Ii0zIiB5PSItMjYiIHdpZHRoPSI2IiBoZWlnaHQ9IjEwIiByeD0iMS41IiB0cmFuc2Zvcm09InJvdGF0ZSg0NSkiLz48cmVjdCB4PSItMyIgeT0iLTI2IiB3aWR0aD0iNiIgaGVpZ2h0PSIxMCIgcng9IjEuNSIgdHJhbnNmb3JtPSJyb3RhdGUoOTApIi8+PHJlY3QgeD0iLTMiIHk9Ii0yNiIgd2lkdGg9IjYiIGhlaWdodD0iMTAiIHJ4PSIxLjUiIHRyYW5zZm9ybT0icm90YXRlKDEzNSkiLz48cmVjdCB4PSItMyIgeT0iLTI2IiB3aWR0aD0iNiIgaGVpZ2h0PSIxMCIgcng9IjEuNSIgdHJhbnNmb3JtPSJyb3RhdGUoMTgwKSIvPjxyZWN0IHg9Ii0zIiB5PSItMjYiIHdpZHRoPSI2IiBoZWlnaHQ9IjEwIiByeD0iMS41IiB0cmFuc2Zvcm09InJvdGF0ZSgyMjUpIi8+PHJlY3QgeD0iLTMiIHk9Ii0yNiIgd2lkdGg9IjYiIGhlaWdodD0iMTAiIHJ4PSIxLjUiIHRyYW5zZm9ybT0icm90YXRlKDI3MCkiLz48cmVjdCB4PSItMyIgeT0iLTI2IiB3aWR0aD0iNiIgaGVpZ2h0PSIxMCIgcng9IjEuNSIgdHJhbnNmb3JtPSJyb3RhdGUoMzE1KSIvPjwvZz48Y2lyY2xlIGN4PSIwIiBjeT0iMCIgcj0iMTgiIGZpbGw9IiMwMGFjYWMiLz48Y2lyY2xlIGN4PSIwIiBjeT0iMCIgcj0iOCIgZmlsbD0iI2Y4ZjlmYSIvPjwvZz48cmVjdCB4PSIxMDAiIHk9IjQyIiB3aWR0aD0iODYiIGhlaWdodD0iNiIgcng9IjEuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjEwMCIgeT0iNTIiIHdpZHRoPSI4NiIgaGVpZ2h0PSIxMiIgcng9IjMiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjEwMCIgeT0iNzAiIHdpZHRoPSI1MCIgaGVpZ2h0PSI1IiByeD0iMS41IiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTAwIiB5PSI4MCIgd2lkdGg9Ijg2IiBoZWlnaHQ9IjEyIiByeD0iMyIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTQiIHk9IjExNiIgd2lkdGg9IjE3MiIgaGVpZ2h0PSIyNiIgcng9IjMiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjIwIiB5PSIxMjQiIHdpZHRoPSI4MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjIwIiB5PSIxMzIiIHdpZHRoPSIxMjAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNDgiIHk9IjEyNCIgd2lkdGg9IjMyIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iIzAwYWNhYyIvPjwvc3ZnPg==' WHERE key = 'configGeneral' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmZmY4ZWIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzOCIgd2lkdGg9IjEwMCIgaGVpZ2h0PSI1IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMTQiIHk9IjQ2IiB3aWR0aD0iODAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNDYiIHk9IjM3IiB3aWR0aD0iMzYiIGhlaWdodD0iMTYiIHJ4PSI4IiBmaWxsPSIjMDBhY2FjIi8+PGNpcmNsZSBjeD0iMTc0IiBjeT0iNDUiIHI9IjYiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSIxNCIgeT0iNTkiIHdpZHRoPSIxMDAiIGhlaWdodD0iNSIgcng9IjEuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjE0IiB5PSI2NyIgd2lkdGg9IjgwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTQ2IiB5PSI1OCIgd2lkdGg9IjM2IiBoZWlnaHQ9IjE2IiByeD0iOCIgZmlsbD0iI2U5ZWNlZiIvPjxjaXJjbGUgY3g9IjE1NCIgY3k9IjY2IiByPSI2IiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeD0iMTQiIHk9IjgwIiB3aWR0aD0iMTAwIiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSIxNCIgeT0iODgiIHdpZHRoPSI4MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0NiIgeT0iNzkiIHdpZHRoPSIzNiIgaGVpZ2h0PSIxNiIgcng9IjgiIGZpbGw9IiNmNTljMWEiLz48Y2lyY2xlIGN4PSIxNzQiIGN5PSI4NyIgcj0iNiIgZmlsbD0iI2ZmZmZmZiIvPjxyZWN0IHg9IjE0IiB5PSIxMDEiIHdpZHRoPSIxMDAiIGhlaWdodD0iNSIgcng9IjEuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjE0IiB5PSIxMDkiIHdpZHRoPSI4MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0NiIgeT0iMTAwIiB3aWR0aD0iMzYiIGhlaWdodD0iMTYiIHJ4PSI4IiBmaWxsPSIjZmI1NTk3Ii8+PGNpcmNsZSBjeD0iMTc0IiBjeT0iMTA4IiByPSI2IiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeD0iMTQiIHk9IjEyMiIgd2lkdGg9IjEwMCIgaGVpZ2h0PSI1IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMTQiIHk9IjEzMCIgd2lkdGg9IjgwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTQ2IiB5PSIxMjEiIHdpZHRoPSIzNiIgaGVpZ2h0PSIxNiIgcng9IjgiIGZpbGw9IiNlOWVjZWYiLz48Y2lyY2xlIGN4PSIxNTQiIGN5PSIxMjkiIHI9IjYiIGZpbGw9IiNmZmZmZmYiLz48L3N2Zz4=' WHERE key = 'configFeatures' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmZmY4ZWIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNCIgd2lkdGg9IjI4IiBoZWlnaHQ9IjQwIiByeD0iMyIgZmlsbD0iI2ZmNWI1NyIvPjxyZWN0IHg9IjUwIiB5PSIzNCIgd2lkdGg9IjI4IiBoZWlnaHQ9IjQwIiByeD0iMyIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9Ijg2IiB5PSIzNCIgd2lkdGg9IjI4IiBoZWlnaHQ9IjQwIiByeD0iMyIgZmlsbD0iI2ZmZDI0ZCIvPjxyZWN0IHg9IjEyMiIgeT0iMzQiIHdpZHRoPSIyOCIgaGVpZ2h0PSI0MCIgcng9IjMiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNTgiIHk9IjM0IiB3aWR0aD0iMjgiIGhlaWdodD0iNDAiIHJ4PSIzIiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMTQiIHk9Ijc4IiB3aWR0aD0iMjgiIGhlaWdodD0iNDAiIHJ4PSIzIiBmaWxsPSIjNDliNmQ2Ii8+PHJlY3QgeD0iNTAiIHk9Ijc4IiB3aWR0aD0iMjgiIGhlaWdodD0iNDAiIHJ4PSIzIiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iODYiIHk9Ijc4IiB3aWR0aD0iMjgiIGhlaWdodD0iNDAiIHJ4PSIzIiBmaWxsPSIjNzI3Y2I2Ii8+PHJlY3QgeD0iMTIyIiB5PSI3OCIgd2lkdGg9IjI4IiBoZWlnaHQ9IjQwIiByeD0iMyIgZmlsbD0iI2ZiNTU5NyIvPjxyZWN0IHg9IjE1OCIgeT0iNzgiIHdpZHRoPSIyOCIgaGVpZ2h0PSI0MCIgcng9IjMiIGZpbGw9IiMwZjE3MmEiLz48ZyB0cmFuc2Zvcm09InRyYW5zbGF0ZSgxMTgsMTI0KSByb3RhdGUoMTUpIj48cmVjdCB4PSIwIiB5PSIwIiB3aWR0aD0iNDIiIGhlaWdodD0iNiIgcng9IjEuNSIgZmlsbD0iIzJkMzUzYyIvPjxyZWN0IHg9IjQyIiB5PSItMiIgd2lkdGg9IjIwIiBoZWlnaHQ9IjEwIiByeD0iMiIgZmlsbD0iI2Y1OWMxYSIvPjxwYXRoIGQ9Ik0gNjIgMyBMIDc2IDAgTCA3NiA2IFoiIGZpbGw9IiNmYjU1OTciLz48L2c+PC9zdmc+' WHERE key = 'configAppearance' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlYWY0ZmIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNiIgd2lkdGg9IjgwIiBoZWlnaHQ9IjEzIiByeD0iNi41IiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMjIiIHk9IjQwIiB3aWR0aD0iNTAiIGhlaWdodD0iNSIgcng9IjEuNSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45NSIvPjxyZWN0IHg9IjEwNiIgeT0iMzciIHdpZHRoPSI3OCIgaGVpZ2h0PSIxMSIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjExMCIgeT0iNDAiIHdpZHRoPSI0MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC41Ii8+PHJlY3QgeD0iMTQiIHk9IjU0IiB3aWR0aD0iODAiIGhlaWdodD0iMTMiIHJ4PSI2LjUiIGZpbGw9IiMzNDhmZTIiLz48cmVjdCB4PSIyMiIgeT0iNTgiIHdpZHRoPSI1MCIgaGVpZ2h0PSI1IiByeD0iMS41IiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjk1Ii8+PHJlY3QgeD0iMTA2IiB5PSI1NSIgd2lkdGg9Ijc4IiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTEwIiB5PSI1OCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjUiLz48cmVjdCB4PSIxNCIgeT0iNzIiIHdpZHRoPSI4MCIgaGVpZ2h0PSIxMyIgcng9IjYuNSIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjIyIiB5PSI3NiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOTUiLz48cmVjdCB4PSIxMDYiIHk9IjczIiB3aWR0aD0iNzgiIGhlaWdodD0iMTEiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSIxMTAiIHk9Ijc2IiB3aWR0aD0iMzAiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmNTljMWEiIG9wYWNpdHk9IjAuNSIvPjxyZWN0IHg9IjE0IiB5PSI5MCIgd2lkdGg9IjgwIiBoZWlnaHQ9IjEzIiByeD0iNi41IiBmaWxsPSIjZmY1YjU3Ii8+PHJlY3QgeD0iMjIiIHk9Ijk0IiB3aWR0aD0iNTAiIGhlaWdodD0iNSIgcng9IjEuNSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45NSIvPjxyZWN0IHg9IjEwNiIgeT0iOTEiIHdpZHRoPSI3OCIgaGVpZ2h0PSIxMSIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjExMCIgeT0iOTQiIHdpZHRoPSI2MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmNWI1NyIgb3BhY2l0eT0iMC41Ii8+PHJlY3QgeD0iMTQiIHk9IjEwOCIgd2lkdGg9IjgwIiBoZWlnaHQ9IjEzIiByeD0iNi41IiBmaWxsPSIjNzI3Y2I2Ii8+PHJlY3QgeD0iMjIiIHk9IjExMiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOTUiLz48cmVjdCB4PSIxMDYiIHk9IjEwOSIgd2lkdGg9Ijc4IiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTEwIiB5PSIxMTIiIHdpZHRoPSIzNSIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzcyN2NiNiIgb3BhY2l0eT0iMC41Ii8+PHJlY3QgeD0iMTQiIHk9IjEyNiIgd2lkdGg9IjgwIiBoZWlnaHQ9IjEzIiByeD0iNi41IiBmaWxsPSIjMDBhY2FjIi8+PHJlY3QgeD0iMjIiIHk9IjEzMCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjUiIHJ4PSIxLjUiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOTUiLz48cmVjdCB4PSIxMDYiIHk9IjEyNyIgd2lkdGg9Ijc4IiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTEwIiB5PSIxMzAiIHdpZHRoPSI1NSIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzAwYWNhYyIgb3BhY2l0eT0iMC41Ii8+PC9zdmc+' WHERE key = 'configStatusAppearance' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlYWY0ZmIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjIwIiB5PSI1OCIgd2lkdGg9IjQ4IiBoZWlnaHQ9IjM2IiByeD0iNiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjI2IiB5PSI2NCIgd2lkdGg9IjM2IiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjk1Ii8+PHJlY3QgeD0iMjYiIHk9IjcyIiB3aWR0aD0iMjgiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIyNiIgeT0iODAiIHdpZHRoPSIyMCIgaGVpZ2h0PSI5IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45NSIvPjxyZWN0IHg9IjEzMiIgeT0iNTgiIHdpZHRoPSI0OCIgaGVpZ2h0PSIzNiIgcng9IjYiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxMzgiIHk9IjY0IiB3aWR0aD0iMzYiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOTUiLz48cmVjdCB4PSIxMzgiIHk9IjcyIiB3aWR0aD0iMjgiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIxMzgiIHk9IjgwIiB3aWR0aD0iMjAiIGhlaWdodD0iOSIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOTUiLz48cGF0aCBkPSJNIDY4IDc2IEMgOTAgNTAgMTEwIDEwMCAxMzIgNzYiIHN0cm9rZT0iI2Y1OWMxYSIgc3Ryb2tlLXdpZHRoPSIzIiBmaWxsPSJub25lIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1kYXNoYXJyYXk9IjQgMyIvPjxjaXJjbGUgY3g9IjEwMCIgY3k9Ijc2IiByPSI2IiBmaWxsPSIjZjU5YzFhIi8+PHBhdGggZD0iTSA5NiA3NiBMIDk5IDc5IEwgMTA1IDczIiBzdHJva2U9IiNmZmZmZmYiIHN0cm9rZS13aWR0aD0iMS41IiBmaWxsPSJub25lIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48cmVjdCB4PSIyMCIgeT0iMTE1IiB3aWR0aD0iMzQiIGhlaWdodD0iMjQiIHJ4PSI0IiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iODAiIHk9IjExNSIgd2lkdGg9IjM0IiBoZWlnaHQ9IjI0IiByeD0iNCIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjE0MCIgeT0iMTE1IiB3aWR0aD0iMzQiIGhlaWdodD0iMjQiIHJ4PSI0IiBmaWxsPSIjZmI1NTk3IiBvcGFjaXR5PSIwLjg1Ii8+PC9zdmc+' WHERE key = 'configExternalConnections' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlZWVlZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjgwIiB5PSIzNCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjE0IiByeD0iMiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9Ijg0IiB5PSI0MCIgd2lkdGg9IjMyIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuODUiLz48bGluZSB4MT0iMTAwIiB5MT0iNDgiIHgyPSIxMDAiIHkyPSI1OCIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxsaW5lIHgxPSI0MCIgeTE9IjU4IiB4Mj0iMTYwIiB5Mj0iNTgiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjIiLz48bGluZSB4MT0iMjAiIHkxPSI1OCIgeDI9IjIwIiB5Mj0iNjgiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjIiLz48cmVjdCB4PSIyMCIgeT0iNjgiIHdpZHRoPSI0MCIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiMzNDhmZTIiLz48cmVjdCB4PSIyNCIgeT0iNzQiIHdpZHRoPSIzMiIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PGxpbmUgeDE9IjYwIiB5MT0iNTgiIHgyPSI2MCIgeTI9IjY4IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS4yIi8+PHJlY3QgeD0iNjAiIHk9IjY4IiB3aWR0aD0iNDAiIGhlaWdodD0iMTQiIHJ4PSIyIiBmaWxsPSIjZjU5YzFhIi8+PHJlY3QgeD0iNjQiIHk9Ijc0IiB3aWR0aD0iMzIiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxsaW5lIHgxPSI2MCIgeTE9IjgyIiB4Mj0iNjAiIHkyPSI5MiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxsaW5lIHgxPSI2NiIgeTE9IjkyIiB4Mj0iOTQiIHkyPSI5MiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxsaW5lIHgxPSI2NiIgeTE9IjkyIiB4Mj0iNjYiIHkyPSIxMDIiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjIiLz48bGluZSB4MT0iOTQiIHkxPSI5MiIgeDI9Ijk0IiB5Mj0iMTAyIiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS4yIi8+PHJlY3QgeD0iNTIiIHk9IjEwMiIgd2lkdGg9IjI4IiBoZWlnaHQ9IjEyIiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZjU5YzFhIiBzdHJva2Utd2lkdGg9IjEuNSIvPjxyZWN0IHg9IjgyIiB5PSIxMDIiIHdpZHRoPSIyOCIgaGVpZ2h0PSIxMiIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2Y1OWMxYSIgc3Ryb2tlLXdpZHRoPSIxLjUiLz48bGluZSB4MT0iMTAwIiB5MT0iNTgiIHgyPSIxMDAiIHkyPSI2OCIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxyZWN0IHg9IjEwMCIgeT0iNjgiIHdpZHRoPSI0MCIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIxMDQiIHk9Ijc0IiB3aWR0aD0iMzIiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxsaW5lIHgxPSIxNDAiIHkxPSI1OCIgeDI9IjE0MCIgeTI9IjY4IiBzdHJva2U9IiNhZGI1YmQiIHN0cm9rZS13aWR0aD0iMS4yIi8+PHJlY3QgeD0iMTQwIiB5PSI2OCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjE0IiByeD0iMiIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjE0NCIgeT0iNzQiIHdpZHRoPSIzMiIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PGxpbmUgeDE9IjE0MCIgeTE9IjgyIiB4Mj0iMTQwIiB5Mj0iOTIiIHN0cm9rZT0iI2FkYjViZCIgc3Ryb2tlLXdpZHRoPSIxLjIiLz48bGluZSB4MT0iMTQ2IiB5MT0iOTIiIHgyPSIxNzQiIHkyPSI5MiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxsaW5lIHgxPSIxNDYiIHkxPSI5MiIgeDI9IjE0NiIgeTI9IjEwMiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxsaW5lIHgxPSIxNzQiIHkxPSI5MiIgeDI9IjE3NCIgeTI9IjEwMiIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxyZWN0IHg9IjEzMiIgeT0iMTAyIiB3aWR0aD0iMjgiIGhlaWdodD0iMTIiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiM3MjdjYjYiIHN0cm9rZS13aWR0aD0iMS41Ii8+PHJlY3QgeD0iMTYyIiB5PSIxMDIiIHdpZHRoPSIyOCIgaGVpZ2h0PSIxMiIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iIzcyN2NiNiIgc3Ryb2tlLXdpZHRoPSIxLjUiLz48bGluZSB4MT0iMTgwIiB5MT0iNTgiIHgyPSIxODAiIHkyPSI2OCIgc3Ryb2tlPSIjYWRiNWJkIiBzdHJva2Utd2lkdGg9IjEuMiIvPjxyZWN0IHg9IjE4MCIgeT0iNjgiIHdpZHRoPSI0MCIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiM0OWI2ZDYiLz48cmVjdCB4PSIxODQiIHk9Ijc0IiB3aWR0aD0iMzIiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjE0IiB5PSIxMjgiIHdpZHRoPSIxNzIiIGhlaWdodD0iMTQiIHJ4PSIzIiBmaWxsPSIjZTllY2VmIi8+PC9zdmc+' WHERE key = 'configPagesMenus' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjYiIHk9IjMwIiB3aWR0aD0iMTg4IiBoZWlnaHQ9IjExNiIgcng9IjQiIGZpbGw9IiMwZjE3MmEiLz48cmVjdCB4PSIxNCIgeT0iNDAiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5YjZkNiIvPjxyZWN0IHg9IjYyIiB5PSI0MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTQiIHk9IjQ2IiB3aWR0aD0iMTYwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iNTUiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZDI0ZCIvPjxyZWN0IHg9IjYyIiB5PSI1NSIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDliNmQ2Ii8+PHJlY3QgeD0iMTQiIHk9IjYxIiB3aWR0aD0iMTMwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iNzAiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjYyIiB5PSI3MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZjU5YzFhIi8+PHJlY3QgeD0iMTQiIHk9Ijc2IiB3aWR0aD0iMTQ1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iODUiIHdpZHRoPSI0NCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5YjZkNiIvPjxyZWN0IHg9IjYyIiB5PSI4NSIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTQiIHk9IjkxIiB3aWR0aD0iMTE1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTAwIiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSI2MiIgeT0iMTAwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZjViNTciLz48cmVjdCB4PSIxNCIgeT0iMTA2IiB3aWR0aD0iMTU1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTE1IiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmQyNGQiLz48cmVjdCB4PSI2MiIgeT0iMTE1IiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmQyNGQiLz48cmVjdCB4PSIxNCIgeT0iMTIxIiB3aWR0aD0iMTI1IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48cmVjdCB4PSIxNCIgeT0iMTMwIiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OWI2ZDYiLz48cmVjdCB4PSI2MiIgeT0iMTMwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNCIgeT0iMTM2IiB3aWR0aD0iMTQwIiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNjYmQ1ZTEiIG9wYWNpdHk9IjAuNTUiLz48L3N2Zz4=' WHERE key = 'configBusWatcher' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlOGY4ZWUiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxjaXJjbGUgY3g9IjEwMCIgY3k9IjkyIiByPSIxNCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjEwMCIgY3k9IjkyIiByPSIyMiIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjMDBhY2FjIiBzdHJva2Utd2lkdGg9IjEuNSIgb3BhY2l0eT0iMC41IiBzdHJva2UtZGFzaGFycmF5PSIzIDMiLz48Y2lyY2xlIGN4PSIxMDAiIGN5PSI5MiIgcj0iMzIiIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzAwYWNhYyIgc3Ryb2tlLXdpZHRoPSIxIiBvcGFjaXR5PSIwLjMiIHN0cm9rZS1kYXNoYXJyYXk9IjMgMyIvPjxsaW5lIHgxPSI4OC41MzA3NTMxMTI3MzM0NCIgeTE9IjgzLjk3MTUyNzE3ODkxMzQiIHgyPSI0Ni41NTM4NTUzNjQxNTIzMyIgeTI9IjU0LjU4NzY5ODc1NDkwNjYyNiIgc3Ryb2tlPSIjMzQ4ZmUyIiBzdHJva2Utd2lkdGg9IjEuNCIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PHJlY3QgeD0iMjYiIHk9IjQyIiB3aWR0aD0iMjgiIGhlaWdodD0iMTYiIHJ4PSIzIiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMzEiIHk9IjQ2IiB3aWR0aD0iMTgiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjMxIiB5PSI1MSIgd2lkdGg9IjEyIiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjYiLz48bGluZSB4MT0iMTExLjQ2OTI0Njg4NzI2NjU2IiB5MT0iODMuOTcxNTI3MTc4OTEzNCIgeDI9IjE1My40NDYxNDQ2MzU4NDc2NyIgeTI9IjU0LjU4NzY5ODc1NDkwNjYyNiIgc3Ryb2tlPSIjZjU5YzFhIiBzdHJva2Utd2lkdGg9IjEuNCIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PHJlY3QgeD0iMTQ2IiB5PSI0MiIgd2lkdGg9IjI4IiBoZWlnaHQ9IjE2IiByeD0iMyIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjE1MSIgeT0iNDYiIHdpZHRoPSIxOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTUxIiB5PSI1MSIgd2lkdGg9IjEyIiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjYiLz48bGluZSB4MT0iODguMTcyNTMyOTQyMjQwNzkiIHkxPSI5OS40OTA3MjkxMzY1ODA4MiIgeDI9IjQ2Ljc1ODU1MjYwNDQzMzgzIiB5Mj0iMTI1LjcxOTU4MzM1MDUyNTI0IiBzdHJva2U9IiNmYjU1OTciIHN0cm9rZS13aWR0aD0iMS40IiBzdHJva2UtZGFzaGFycmF5PSIyIDIiLz48cmVjdCB4PSIyNiIgeT0iMTIyIiB3aWR0aD0iMjgiIGhlaWdodD0iMTYiIHJ4PSIzIiBmaWxsPSIjZmI1NTk3Ii8+PHJlY3QgeD0iMzEiIHk9IjEyNiIgd2lkdGg9IjE4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIzMSIgeT0iMTMxIiB3aWR0aD0iMTIiIGhlaWdodD0iMiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNiIvPjxsaW5lIHgxPSIxMTEuODI3NDY3MDU3NzU5MjEiIHkxPSI5OS40OTA3MjkxMzY1ODA4MiIgeDI9IjE1My4yNDE0NDczOTU1NjYxOCIgeTI9IjEyNS43MTk1ODMzNTA1MjUyNCIgc3Ryb2tlPSIjNzI3Y2I2IiBzdHJva2Utd2lkdGg9IjEuNCIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PHJlY3QgeD0iMTQ2IiB5PSIxMjIiIHdpZHRoPSIyOCIgaGVpZ2h0PSIxNiIgcng9IjMiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxNTEiIHk9IjEyNiIgd2lkdGg9IjE4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIxNTEiIHk9IjEzMSIgd2lkdGg9IjEyIiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjYiLz48bGluZSB4MT0iMTAwIiB5MT0iNzgiIHgyPSIxMDAiIHkyPSI0NCIgc3Ryb2tlPSIjMzJhOTMyIiBzdHJva2Utd2lkdGg9IjEuNCIgc3Ryb2tlLWRhc2hhcnJheT0iMiAyIi8+PHJlY3QgeD0iODYiIHk9IjI4IiB3aWR0aD0iMjgiIGhlaWdodD0iMTYiIHJ4PSIzIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iOTEiIHk9IjMyIiB3aWR0aD0iMTgiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjkxIiB5PSIzNyIgd2lkdGg9IjEyIiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjYiLz48L3N2Zz4=' WHERE key = 'configEvents' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlOGY4ZWUiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjEwIiB5PSIzMiIgd2lkdGg9IjE4MCIgaGVpZ2h0PSI1MCIgcng9IjQiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxwb2x5bGluZSBwb2ludHM9IjE0LDU4IDM2LDU4IDQyLDQ2IDUwLDcyIDU4LDQwIDY2LDY4IDcyLDU4IDk2LDU4IDEwMiw1MiAxMTAsNjQgMTE4LDU4IDE4Niw1OCIgc3Ryb2tlPSIjMzJhOTMyIiBzdHJva2Utd2lkdGg9IjIiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iOTIiIHI9IjQiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIzMCIgeT0iOTAiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzLjUiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iODYiIHk9IjkwLjUiIHdpZHRoPSI2MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE2MCIgeT0iOTAiIHdpZHRoPSIyMiIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC4yNSIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iMTAyIiByPSI0IiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMzAiIHk9IjEwMCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMuNSIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI4NiIgeT0iMTAwLjUiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE2MCIgeT0iMTAwIiB3aWR0aD0iMjIiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiMzMmE5MzIiIG9wYWNpdHk9IjAuMjUiLz48Y2lyY2xlIGN4PSIyMCIgY3k9IjExMiIgcj0iNCIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjMwIiB5PSIxMTAiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzLjUiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iODYiIHk9IjExMC41IiB3aWR0aD0iNzAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNjAiIHk9IjExMCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZjU5YzFhIiBvcGFjaXR5PSIwLjI1Ii8+PGNpcmNsZSBjeD0iMjAiIGN5PSIxMjIiIHI9IjQiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIzMCIgeT0iMTIwIiB3aWR0aD0iNTAiIGhlaWdodD0iMy41IiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9Ijg2IiB5PSIxMjAuNSIgd2lkdGg9IjQwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTYwIiB5PSIxMjAiIHdpZHRoPSIyMiIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC4yNSIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iMTMyIiByPSI0IiBmaWxsPSIjZmY1YjU3Ii8+PHJlY3QgeD0iMzAiIHk9IjEzMCIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMuNSIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI4NiIgeT0iMTMwLjUiIHdpZHRoPSIzMCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE2MCIgeT0iMTMwIiB3aWR0aD0iMjIiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZjViNTciIG9wYWNpdHk9IjAuMjUiLz48L3N2Zz4=' WHERE key = 'configSystemHealth' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjEwIiB5PSIzMiIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiM3MjdjYjYiIG9wYWNpdHk9IjAuMTgiLz48cmVjdCB4PSIxNCIgeT0iMzYiIHdpZHRoPSI0MCIgaGVpZ2h0PSI2IiByeD0iMSIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjYwIiB5PSIzNiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTAiIHk9IjUyIiB3aWR0aD0iMTgwIiBoZWlnaHQ9IjEzIiByeD0iMS41IiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSIyMCIgY3k9IjU4LjUiIHI9IjQuNSIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjI5IiB5PSI1NiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjkiIHk9IjYxIiB3aWR0aD0iMzgiIGhlaWdodD0iMi41IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9Ijg2IiB5PSI1NyIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTUyIiB5PSI1Ni41IiB3aWR0aD0iMzIiIGhlaWdodD0iNSIgcng9IjIuNSIgZmlsbD0iIzMyYTkzMiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwIiB5PSI2NiIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxMyIgcng9IjEuNSIgZmlsbD0iI2U5ZWNlZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iMjAiIGN5PSI3Mi41IiByPSI0LjUiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIyOSIgeT0iNzAiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSI3NSIgd2lkdGg9IjM4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSI4NiIgeT0iNzEiIHdpZHRoPSI1NSIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE1MiIgeT0iNzAuNSIgd2lkdGg9IjMyIiBoZWlnaHQ9IjUiIHJ4PSIyLjUiIGZpbGw9IiNmNTljMWEiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIxMCIgeT0iODAiIHdpZHRoPSIxODAiIGhlaWdodD0iMTMiIHJ4PSIxLjUiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iODYuNSIgcj0iNC41IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMjkiIHk9Ijg0IiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSIyOSIgeT0iODkiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9Ijg1IiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9Ijg0LjUiIHdpZHRoPSIzMiIgaGVpZ2h0PSI1IiByeD0iMi41IiBmaWxsPSIjYWRiNWJkIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTAiIHk9Ijk0IiB3aWR0aD0iMTgwIiBoZWlnaHQ9IjEzIiByeD0iMS41IiBmaWxsPSIjZTllY2VmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSIyMCIgY3k9IjEwMC41IiByPSI0LjUiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIyOSIgeT0iOTgiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSIxMDMiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9Ijk5IiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9Ijk4LjUiIHdpZHRoPSIzMiIgaGVpZ2h0PSI1IiByeD0iMi41IiBmaWxsPSIjMzJhOTMyIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTAiIHk9IjEwOCIgd2lkdGg9IjE4MCIgaGVpZ2h0PSIxMyIgcng9IjEuNSIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iMjAiIGN5PSIxMTQuNSIgcj0iNC41IiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMjkiIHk9IjExMiIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjkiIHk9IjExNyIgd2lkdGg9IjM4IiBoZWlnaHQ9IjIuNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSI4NiIgeT0iMTEzIiB3aWR0aD0iNTUiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxNTIiIHk9IjExMi41IiB3aWR0aD0iMzIiIGhlaWdodD0iNSIgcng9IjIuNSIgZmlsbD0iI2Y1OWMxYSIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwIiB5PSIxMjIiIHdpZHRoPSIxODAiIGhlaWdodD0iMTMiIHJ4PSIxLjUiIGZpbGw9IiNlOWVjZWYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjIwIiBjeT0iMTI4LjUiIHI9IjQuNSIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjI5IiB5PSIxMjYiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjI5IiB5PSIxMzEiIHdpZHRoPSIzOCIgaGVpZ2h0PSIyLjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iODYiIHk9IjEyNyIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTUyIiB5PSIxMjYuNSIgd2lkdGg9IjMyIiBoZWlnaHQ9IjUiIHJ4PSIyLjUiIGZpbGw9IiNhZGI1YmQiIG9wYWNpdHk9IjAuODUiLz48L3N2Zz4=' WHERE key = 'configSecurityUsers' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlZWVlZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxjaXJjbGUgY3g9IjY1IiBjeT0iODAiIHI9IjMyIiBmaWxsPSIjMDBhY2FjIiBvcGFjaXR5PSIwLjE4Ii8+PGNpcmNsZSBjeD0iMTIwIiBjeT0iNjgiIHI9IjI4IiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjIwIi8+PGNpcmNsZSBjeD0iMTM1IiBjeT0iMTE1IiByPSIyNiIgZmlsbD0iI2ZiNTU5NyIgb3BhY2l0eT0iMC4yMCIvPjxjaXJjbGUgY3g9IjU1IiBjeT0iNzUiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjU1IiBjeT0iNzMuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjcwIiBjeT0iOTAiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjcwIiBjeT0iODguNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjgwIiBjeT0iNzAiIHI9IjUuNSIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjgwIiBjeT0iNjguNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjExNSIgY3k9IjYwIiByPSI1LjUiIGZpbGw9IiMzNDhmZTIiLz48Y2lyY2xlIGN4PSIxMTUiIGN5PSI1OC41IiByPSIyIiBmaWxsPSIjZmZmZmZmIi8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iNzUiIHI9IjUuNSIgZmlsbD0iIzM0OGZlMiIvPjxjaXJjbGUgY3g9IjEyOCIgY3k9IjczLjUiIHI9IjIiIGZpbGw9IiNmZmZmZmYiLz48Y2lyY2xlIGN4PSIxMDgiIGN5PSI3OCIgcj0iNS41IiBmaWxsPSIjMzQ4ZmUyIi8+PGNpcmNsZSBjeD0iMTA4IiBjeT0iNzYuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxjaXJjbGUgY3g9IjEyOCIgY3k9IjExMCIgcj0iNS41IiBmaWxsPSIjZmI1NTk3Ii8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iMTA4LjUiIHI9IjIiIGZpbGw9IiNmZmZmZmYiLz48Y2lyY2xlIGN4PSIxNDAiIGN5PSIxMjIiIHI9IjUuNSIgZmlsbD0iI2ZiNTU5NyIvPjxjaXJjbGUgY3g9IjE0MCIgY3k9IjEyMC41IiByPSIyIiBmaWxsPSIjZmZmZmZmIi8+PGNpcmNsZSBjeD0iMTQ4IiBjeT0iMTA1IiByPSI1LjUiIGZpbGw9IiNmYjU1OTciLz48Y2lyY2xlIGN4PSIxNDgiIGN5PSIxMDMuNSIgcj0iMiIgZmlsbD0iI2ZmZmZmZiIvPjxyZWN0IHg9IjE0IiB5PSIxMzUiIHdpZHRoPSIxNzAiIGhlaWdodD0iOCIgcng9IjIiIGZpbGw9IiNlOWVjZWYiLz48L3N2Zz4=' WHERE key = 'configSecurityGroups' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlOGY4ZWUiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjYwIiB5PSIzMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjEwIiByeD0iMiIgZmlsbD0iIzAwYWNhYyIgb3BhY2l0eT0iMC4yIi8+PHJlY3QgeD0iOTgiIHk9IjMyIiB3aWR0aD0iMzQiIGhlaWdodD0iMTAiIHJ4PSIyIiBmaWxsPSIjMzQ4ZmUyIiBvcGFjaXR5PSIwLjIiLz48cmVjdCB4PSIxMzYiIHk9IjMyIiB3aWR0aD0iMzQiIGhlaWdodD0iMTAiIHJ4PSIyIiBmaWxsPSIjZjU5YzFhIiBvcGFjaXR5PSIwLjIiLz48cmVjdCB4PSIxNCIgeT0iNTAiIHdpZHRoPSI0MiIgaGVpZ2h0PSI2IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iNjAiIHk9IjQ2IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSI5OCIgeT0iNDYiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjEzNiIgeT0iNDYiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9Ijc3IiBjeT0iNTUiIHI9IjYiIGZpbGw9IiMwMGFjYWMiLz48cGF0aCBkPSJNIDc0IDU1IEwgNzYgNTcgTCA4MCA1MyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PGNpcmNsZSBjeD0iMTUzIiBjeT0iNTUiIHI9IjYiIGZpbGw9IiNmNTljMWEiLz48cGF0aCBkPSJNIDE1MCA1NSBMIDE1MiA1NyBMIDE1NiA1MyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PHJlY3QgeD0iMTQiIHk9IjcyIiB3aWR0aD0iNDIiIGhlaWdodD0iNiIgcng9IjEuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjYwIiB5PSI2OCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iOTgiIHk9IjY4IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSIxMzYiIHk9IjY4IiB3aWR0aD0iMzQiIGhlaWdodD0iMTgiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48Y2lyY2xlIGN4PSI3NyIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjMDBhY2FjIi8+PHBhdGggZD0iTSA3NCA3NyBMIDc2IDc5IEwgODAgNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxjaXJjbGUgY3g9IjExNSIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjMzQ4ZmUyIi8+PHBhdGggZD0iTSAxMTIgNzcgTCAxMTQgNzkgTCAxMTggNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxjaXJjbGUgY3g9IjE1MyIgY3k9Ijc3IiByPSI2IiBmaWxsPSIjZjU5YzFhIi8+PHBhdGggZD0iTSAxNTAgNzcgTCAxNTIgNzkgTCAxNTYgNzUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxyZWN0IHg9IjE0IiB5PSI5NCIgd2lkdGg9IjQyIiBoZWlnaHQ9IjYiIHJ4PSIxLjUiIGZpbGw9IiM0OTUwNTciLz48cmVjdCB4PSI2MCIgeT0iOTAiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9Ijk4IiB5PSI5MCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTM2IiB5PSI5MCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PGNpcmNsZSBjeD0iNzciIGN5PSI5OSIgcj0iNiIgZmlsbD0iIzAwYWNhYyIvPjxwYXRoIGQ9Ik0gNzQgOTkgTCA3NiAxMDEgTCA4MCA5NyIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PGNpcmNsZSBjeD0iMTE1IiBjeT0iOTkiIHI9IjYiIGZpbGw9IiMzNDhmZTIiLz48cGF0aCBkPSJNIDExMiA5OSBMIDExNCAxMDEgTCAxMTggOTciIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSIxLjYiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjxyZWN0IHg9IjE0IiB5PSIxMTYiIHdpZHRoPSI0MiIgaGVpZ2h0PSI2IiByeD0iMS41IiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iNjAiIHk9IjExMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iOTgiIHk9IjExMiIgd2lkdGg9IjM0IiBoZWlnaHQ9IjE4IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTM2IiB5PSIxMTIiIHdpZHRoPSIzNCIgaGVpZ2h0PSIxOCIgcng9IjIiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExNSIgY3k9IjEyMSIgcj0iNiIgZmlsbD0iIzM0OGZlMiIvPjxwYXRoIGQ9Ik0gMTEyIDEyMSBMIDExNCAxMjMgTCAxMTggMTE5IiBzdHJva2U9IiNmZmZmZmYiIHN0cm9rZS13aWR0aD0iMS42IiBmaWxsPSJub25lIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiLz48Y2lyY2xlIGN4PSIxNTMiIGN5PSIxMjEiIHI9IjYiIGZpbGw9IiNmNTljMWEiLz48cGF0aCBkPSJNIDE1MCAxMjEgTCAxNTIgMTIzIEwgMTU2IDExOSIgc3Ryb2tlPSIjZmZmZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIgZmlsbD0ibm9uZSIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+PC9zdmc+' WHERE key = 'configSecurityRoles' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmZmY4ZWIiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxnIHRyYW5zZm9ybT0idHJhbnNsYXRlKDQwLCA2MCkgcm90YXRlKC0yMCkiPjxjaXJjbGUgY3g9IjAiIGN5PSIwIiByPSIxOCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZjU5YzFhIiBzdHJva2Utd2lkdGg9IjYiLz48cmVjdCB4PSIxNCIgeT0iLTQiIHdpZHRoPSI0OCIgaGVpZ2h0PSI4IiBmaWxsPSIjZjU5YzFhIi8+PHJlY3QgeD0iNDgiIHk9Ii00IiB3aWR0aD0iNiIgaGVpZ2h0PSIxNCIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjU4IiB5PSItNCIgd2lkdGg9IjQiIGhlaWdodD0iMTAiIGZpbGw9IiNmNTljMWEiLz48L2c+PHJlY3QgeD0iMTAwIiB5PSI0MiIgd2lkdGg9Ijg2IiBoZWlnaHQ9IjE0IiByeD0iMiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjEwNCIgeT0iNDYiIHdpZHRoPSI2MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwNCIgeT0iNTEiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC42NSIvPjxyZWN0IHg9IjEwMCIgeT0iNjIiIHdpZHRoPSI4NiIgaGVpZ2h0PSIxNCIgcng9IjIiIGZpbGw9IiMzNDhmZTIiLz48cmVjdCB4PSIxMDQiIHk9IjY2IiB3aWR0aD0iNDgiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuODUiLz48cmVjdCB4PSIxMDQiIHk9IjcxIiB3aWR0aD0iMzQiIGhlaWdodD0iMiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIxMDAiIHk9IjgyIiB3aWR0aD0iODYiIGhlaWdodD0iMTQiIHJ4PSIyIiBmaWxsPSIjNzI3Y2I2Ii8+PHJlY3QgeD0iMTA0IiB5PSI4NiIgd2lkdGg9IjU2IiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PHJlY3QgeD0iMTA0IiB5PSI5MSIgd2lkdGg9IjM2IiBoZWlnaHQ9IjIiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjY1Ii8+PHJlY3QgeD0iMTQiIHk9IjExOCIgd2lkdGg9IjE3MiIgaGVpZ2h0PSIyMiIgcng9IjMiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjIwIiB5PSIxMjQiIHdpZHRoPSI2MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjIwIiB5PSIxMzEiIHdpZHRoPSIxMDAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48L3N2Zz4=' WHERE key = 'configSecurityPermissions' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNlZWVlZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNCIgd2lkdGg9IjEyMCIgaGVpZ2h0PSIxMCIgcng9IjIiIGZpbGw9IiNlOWVjZWYiLz48cmVjdCB4PSIxNCIgeT0iNDgiIHdpZHRoPSI4MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0IiB5PSI2MiIgd2lkdGg9IjYiIGhlaWdodD0iNiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjI2IiB5PSI2MiIgd2lkdGg9IjYwIiBoZWlnaHQ9IjYiIHJ4PSIxIiBmaWxsPSIjNDk1MDU3Ii8+PHJlY3QgeD0iMjAiIHk9IjcyIiB3aWR0aD0iNiIgaGVpZ2h0PSI2IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMzIiIHk9IjcyIiB3aWR0aD0iNTAiIGhlaWdodD0iNSIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIyMCIgeT0iODIiIHdpZHRoPSI2IiBoZWlnaHQ9IjYiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIzMiIgeT0iODIiIHdpZHRoPSI0NCIgaGVpZ2h0PSI1IiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjE0IiB5PSI5NCIgd2lkdGg9IjYiIGhlaWdodD0iNiIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjI2IiB5PSI5NCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjUiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PGcgdHJhbnNmb3JtPSJ0cmFuc2xhdGUoMTQwLCA5MCkiPjxjaXJjbGUgY3g9IjAiIGN5PSIwIiByPSIyMiIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjNzI3Y2I2IiBzdHJva2Utd2lkdGg9IjQiLz48bGluZSB4MT0iMTYiIHkxPSIxNiIgeDI9IjMyIiB5Mj0iMzIiIHN0cm9rZT0iIzcyN2NiNiIgc3Ryb2tlLXdpZHRoPSI2IiBzdHJva2UtbGluZWNhcD0icm91bmQiLz48cGF0aCBkPSJNIC04IDAgTCAtMiA2IEwgOCAtNiIgc3Ryb2tlPSIjMzJhOTMyIiBzdHJva2Utd2lkdGg9IjMiIGZpbGw9Im5vbmUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCIvPjwvZz48L3N2Zz4=' WHERE key = 'configSecurityPermissionChecker' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjE0IiB5PSIzNCIgd2lkdGg9IjU1IiBoZWlnaHQ9IjQ4IiByeD0iNCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjY5IiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48Y2lyY2xlIGN4PSIxNCIgY3k9IjU4IiByPSI2IiBmaWxsPSIjZjhmOWZhIi8+PHJlY3QgeD0iMjAiIHk9IjQyIiB3aWR0aD0iMzAiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOSIvPjxyZWN0IHg9IjIwIiB5PSI1MCIgd2lkdGg9IjIyIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjY1Ii8+PHJlY3QgeD0iNzMiIHk9IjM0IiB3aWR0aD0iNTUiIGhlaWdodD0iNDgiIHJ4PSI0IiBmaWxsPSIjNzI3Y2I2Ii8+PGNpcmNsZSBjeD0iMTI4IiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSI3OSIgeT0iNDIiIHdpZHRoPSIzMCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45Ii8+PHJlY3QgeD0iNzkiIHk9IjUwIiB3aWR0aD0iMjIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNjUiLz48cmVjdCB4PSIxMzIiIHk9IjM0IiB3aWR0aD0iNTUiIGhlaWdodD0iNDgiIHJ4PSI0IiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMTMyIiBjeT0iNTgiIHI9IjYiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSIxMzgiIHk9IjQyIiB3aWR0aD0iMzAiIGhlaWdodD0iNCIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuOSIvPjxyZWN0IHg9IjEzOCIgeT0iNTAiIHdpZHRoPSIyMiIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC42NSIvPjxyZWN0IHg9IjE0IiB5PSI5MiIgd2lkdGg9Ijg0IiBoZWlnaHQ9IjUwIiByeD0iNCIgZmlsbD0iI2ZiNTU5NyIvPjxyZWN0IHg9IjIwIiB5PSIxMDAiIHdpZHRoPSI0MCIgaGVpZ2h0PSI0IiByeD0iMSIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC45Ii8+PHJlY3QgeD0iMjAiIHk9IjEwOCIgd2lkdGg9IjYwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIyMCIgeT0iMTE1IiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmZmZmYiIG9wYWNpdHk9IjAuNyIvPjxyZWN0IHg9IjIwIiB5PSIxMjgiIHdpZHRoPSIzMCIgaGVpZ2h0PSI5IiByeD0iMiIgZmlsbD0iI2ZmZmZmZiIgb3BhY2l0eT0iMC44NSIvPjxyZWN0IHg9IjEwMiIgeT0iOTIiIHdpZHRoPSI4NCIgaGVpZ2h0PSI1MCIgcng9IjQiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxMDgiIHk9IjEwMCIgd2lkdGg9IjQwIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjkiLz48cmVjdCB4PSIxMDgiIHk9IjEwOCIgd2lkdGg9IjYwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIxMDgiIHk9IjExNSIgd2lkdGg9IjUwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjciLz48cmVjdCB4PSIxMDgiIHk9IjEyOCIgd2lkdGg9IjMwIiBoZWlnaHQ9IjkiIHJ4PSIyIiBmaWxsPSIjZmZmZmZmIiBvcGFjaXR5PSIwLjg1Ii8+PC9zdmc+' WHERE key = 'configPlugins' AND thumbnail_url IS NULL;
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjIwIiB5PSIzMiIgd2lkdGg9IjgwIiBoZWlnaHQ9IjExMCIgcng9IjMiIGZpbGw9IiNmZmZmZmYiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjEwMCIgeT0iMzIiIHdpZHRoPSI4MCIgaGVpZ2h0PSIxMTAiIHJ4PSIzIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48bGluZSB4MT0iMTAwIiB5MT0iMzIiIHgyPSIxMDAiIHkyPSIxNDIiIHN0cm9rZT0iI2RlZTJlNiIvPjxyZWN0IHg9IjI2IiB5PSI0MCIgd2lkdGg9IjYwIiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMjgiIHk9IjUwIiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIyNiIgeT0iNjAiIHdpZHRoPSI2NSIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjI4IiB5PSI3OCIgd2lkdGg9IjU1IiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMjYiIHk9Ijg4IiB3aWR0aD0iNzAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIyOCIgeT0iOTYiIHdpZHRoPSI0NSIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjI2IiB5PSIxMTAiIHdpZHRoPSI2MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjI4IiB5PSIxMjAiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjI2IiB5PSIxMzAiIHdpZHRoPSI0MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEwNiIgeT0iNDAiIHdpZHRoPSI2OCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzcyN2NiNiIvPjxyZWN0IHg9IjEwNiIgeT0iNDgiIHdpZHRoPSI1MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEwNiIgeT0iNTYiIHdpZHRoPSI2MCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEwNiIgeT0iNjgiIHdpZHRoPSI2OCIgaGVpZ2h0PSI0OCIgcng9IjMiIGZpbGw9IiMwZjE3MmEiLz48cmVjdCB4PSIxMTAiIHk9Ijc0IiB3aWR0aD0iMTQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSIxMjYiIHk9Ijc0IiB3aWR0aD0iMzIiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OWI2ZDYiLz48cmVjdCB4PSIxMTAiIHk9IjgyIiB3aWR0aD0iNTAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNmZmQyNGQiLz48cmVjdCB4PSIxMTAiIHk9IjkwIiB3aWR0aD0iMjAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxMzIiIHk9IjkwIiB3aWR0aD0iMzQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM0OWI2ZDYiLz48cmVjdCB4PSIxMTAiIHk9Ijk4IiB3aWR0aD0iNDAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxMTAiIHk9IjEwNiIgd2lkdGg9IjU2IiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNDliNmQ2Ii8+PHJlY3QgeD0iMTA2IiB5PSIxMjIiIHdpZHRoPSI2OCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iI2FkYjViZCIvPjxyZWN0IHg9IjEwNiIgeT0iMTMwIiB3aWR0aD0iNDAiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiNhZGI1YmQiLz48L3N2Zz4=' WHERE key = 'configPluginDocumentation' AND thumbnail_url IS NULL;
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

        -- Added after initial table; ALTER ADD COLUMN IF NOT EXISTS is idempotent
        -- on fresh installs and additive on upgrades.
        ALTER TABLE workflow_execution_errors
            ADD COLUMN IF NOT EXISTS error_stack_trace TEXT NULL;
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

        -- Optional parent reference so a notification can be cleared in bulk
        -- when its container is closed out (e.g. all task-assignment rows for
        -- a workflow execution dropping when the execution completes).
        ALTER TABLE notifications
            ADD COLUMN IF NOT EXISTS parent_entity_kind TEXT NULL;

        ALTER TABLE notifications
            ADD COLUMN IF NOT EXISTS parent_entity_id TEXT NULL;

        CREATE INDEX IF NOT EXISTS ix_notifications_parent
            ON notifications (parent_entity_kind, parent_entity_id)
            WHERE parent_entity_id IS NOT NULL;
        """;

    // Lockout columns for local_users — added when the failed-login lockout
    // policy was introduced. Idempotent ALTERs so existing dev/prod databases
    // pick up the columns without a manual migration.
    private const string LocalUserLockoutSql =
        """
        ALTER TABLE local_users
            ADD COLUMN IF NOT EXISTS failed_login_attempts INTEGER NOT NULL DEFAULT 0;

        ALTER TABLE local_users
            ADD COLUMN IF NOT EXISTS is_locked BOOLEAN NOT NULL DEFAULT FALSE;

        ALTER TABLE local_users
            ADD COLUMN IF NOT EXISTS locked_at_utc TIMESTAMPTZ NULL;
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

    // Phase 4 of the self-healing plan: when AuditOutboxDeadLetterParkRemediator
    // gives up on a row (the dispatcher already abandoned it at MaxAttempts),
    // it parks the row here with a reason and removes it from audit_outbox so
    // the live table doesn't grow forever. Operators can inspect this table for
    // forensic analysis without slowing the dispatcher's hot path.
    private const string AuditOutboxDeadLettersSchemaSql =
        """
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
        """;

    // Self-healing platform: every detector writes one row per distinct issue
    // it finds. The partial unique index on `fingerprint` is the dedup contract
    // — re-detecting the same issue bumps occurrence_count instead of
    // inserting. Re-occurrence after resolution opens a fresh row because the
    // index only covers open/acknowledged states.
    private const string SystemIssuesSchemaSql =
        """
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
            facts_json JSONB NOT NULL DEFAULT '{{}}'::jsonb,
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
        """;

    // Self-installing menu item under Site Configuration → Site Information.
    // Template menu items must carry both `templateKey` and `path` in their
    // config — `path` is what the SPA's nav uses to wire the NavLink and what
    // EfCoreMenuStore exposes as the registry path. v1/v2 only set
    // templateKey, so the entry was in the DB but invisible in the nav (the
    // nav drops items whose resolved path is null). v3 fixes that and forces
    // a re-install on databases carrying the broken row by patching it in
    // place.
    //
    // Idempotency: the v3 marker gates the whole block. Within the block,
    // ON CONFLICT keeps INSERT safe; the patch path uses jsonb_set with a
    // WHERE clause that only matches incomplete rows.
    private const string SiteConfigSystemIssuesSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            site_information_group_id UUID;
            next_sort INT;
            already_present BOOLEAN := FALSE;
            inserted BOOLEAN := FALSE;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_system_issues_v3') THEN
                -- Patch any v1/v2 row that landed without a path so it
                -- becomes navigable. Safe: only matches the configSystemIssues
                -- template row that's missing or has an empty path.
                UPDATE menu_items
                SET config = jsonb_set(config, '{{path}}', '"/admin/config/system-issues"'::jsonb, true),
                    updated_at_utc = NOW()
                WHERE config->>'templateKey' = 'configSystemIssues'
                  AND COALESCE(config->>'path', '') = '';

                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO site_information_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Site Information'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF site_information_group_id IS NOT NULL THEN
                        SELECT EXISTS (
                            SELECT 1 FROM menu_items
                            WHERE menu_id = site_id
                              AND parent_id = site_information_group_id
                              AND config->>'templateKey' = 'configSystemIssues'
                        ) INTO already_present;

                        IF NOT already_present THEN
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
                                'System Issues', 'fa fa-triangle-exclamation',
                                'template',
                                '{{"templateKey":"configSystemIssues","path":"/admin/config/system-issues"}}'::jsonb,
                                TRUE, TRUE, NOW(), NOW()
                            );
                            inserted := TRUE;
                        END IF;
                    END IF;
                END IF;

                -- Only mark v3 done when the menu item is actually in place
                -- (either we just inserted it, or it was already present and
                -- has now been patched to include the path).
                IF inserted OR already_present THEN
                    INSERT INTO auth_seed_state (key, applied_at_utc)
                    VALUES ('site_config_system_issues_v3', NOW())
                    ON CONFLICT (key) DO NOTHING;
                END IF;
            END IF;
        END $$;
        """;

    // Adds a "Forms" group to the site-config left-nav with two child template
    // items: "Forms" and "Form Mappings". The group is appended at the end of
    // the existing top-level groups (sort_order = max+1) so it slots in below
    // Security without disturbing other groups. Idempotent via auth_seed_state
    // and content guards: re-running won't duplicate the group or its items.
    private const string SiteConfigFormsSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            forms_group_id UUID;
            next_sort INT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_forms_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO forms_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Forms'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF forms_group_id IS NULL THEN
                        SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_sort
                        FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id IS NULL;

                        forms_group_id := gen_random_uuid();
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            forms_group_id, site_id, NULL, next_sort,
                            'Forms', 'fa fa-list-check',
                            'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = forms_group_id
                          AND config->>'templateKey' = 'configForms'
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, forms_group_id, 0,
                            'Forms', 'fa fa-file-lines',
                            'template',
                            '{{"templateKey":"configForms","path":"/admin/config/forms"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = forms_group_id
                          AND config->>'templateKey' = 'configFormMappings'
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, forms_group_id, 1,
                            'Form Mappings', 'fa fa-arrow-right-arrow-left',
                            'template',
                            '{{"templateKey":"configFormMappings","path":"/admin/config/form-mappings"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_forms_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Adds a "Chatbot" group to the site-config left-nav with one child
    // template item: "Chatbot Settings". Mirrors the SiteConfigFormsSql shape
    // (own group at the end of top-level groups) so future chatbot config
    // items can slot in alongside without disturbing other sections.
    // Idempotent via auth_seed_state and content guards.
    private const string SiteConfigChatbotSettingsSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            chatbot_group_id UUID;
            next_sort INT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_chatbot_settings_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO chatbot_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Chatbot'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF chatbot_group_id IS NULL THEN
                        SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_sort
                        FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id IS NULL;

                        chatbot_group_id := gen_random_uuid();
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            chatbot_group_id, site_id, NULL, next_sort,
                            'Chatbot', 'fa fa-robot',
                            'group', '{{}}'::jsonb, TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = chatbot_group_id
                          AND config->>'templateKey' = 'configChatbotSettings'
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, chatbot_group_id, 0,
                            'Chatbot Settings', 'fa fa-sliders',
                            'template',
                            '{{"templateKey":"configChatbotSettings","path":"/admin/config/chatbot-settings"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_chatbot_settings_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Forms feature: admin-authored JSX bound to backend data. `forms` holds
    // the editable draft (latest FormCode + metadata); `form_versions` is an
    // append-only history written every save / publish / restore. The SPA
    // renders the published snapshot at /form/{shortCode} (when site_available)
    // and the draft at /formdev/{shortCode}.
    private const string FormsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS forms (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            short_code TEXT NOT NULL,
            form_code TEXT NOT NULL DEFAULT '',
            site_available BOOLEAN NOT NULL DEFAULT FALSE,
            is_draft BOOLEAN NOT NULL DEFAULT TRUE,
            draft_version_number INTEGER NOT NULL DEFAULT 1,
            published_version_number INTEGER NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS forms_short_code_key
            ON forms (short_code);

        CREATE INDEX IF NOT EXISTS ix_forms_updated_at_utc
            ON forms (updated_at_utc DESC);

        CREATE TABLE IF NOT EXISTS form_versions (
            id UUID PRIMARY KEY,
            form_id UUID NOT NULL REFERENCES forms (id) ON DELETE CASCADE,
            version_number INTEGER NOT NULL,
            name TEXT NOT NULL,
            short_code TEXT NOT NULL,
            form_code TEXT NOT NULL,
            site_available BOOLEAN NOT NULL,
            kind TEXT NOT NULL,
            note TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS form_versions_form_id_version_number_key
            ON form_versions (form_id, version_number);

        CREATE INDEX IF NOT EXISTS ix_form_versions_form_id
            ON form_versions (form_id);
        """;

    // Generic kind-discriminated table for outbound integrations whose
    // configuration must be admin-editable: LLM providers (Anthropic, OpenAI),
    // and future kinds like SMTP, S3, identity providers. The api key (or
    // equivalent secret) is encrypted via DataProtection — see
    // IConnectionSecretProtector. metadata_json carries kind-specific fields
    // (base url, default model, custom headers) so adding a new kind doesn't
    // require schema changes. The partial unique index makes "set as default
    // for kind X" a one-row guarantee.
    private const string ExternalConnectionsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS external_connection (
            id UUID PRIMARY KEY,
            kind TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT NULL,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            is_default BOOLEAN NOT NULL DEFAULT FALSE,
            metadata_json JSONB NOT NULL,
            secret_ciphertext BYTEA NULL,
            secret_fingerprint TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_external_connection_kind_enabled
            ON external_connection (kind, is_enabled);

        CREATE UNIQUE INDEX IF NOT EXISTS ux_external_connection_default_per_kind
            ON external_connection (kind)
            WHERE is_default;
        """;

    // Agentic-AI conversation storage. Per-user, per-page (page_key derived in
    // the SPA so the right-side chat sidebar can scope conversations to the
    // user's current route). Hard-delete on user request — the audit event
    // agent.conversation.deleted preserves the trail. content_json stores
    // provider-neutral content blocks (text / tool_use / tool_result) so a
    // conversation started against one provider can be replayed against a
    // different one without rewrites. tool calls live in their own table so
    // the agent loop can correlate provider-issued tool_use ids with our row
    // ids and persist intermediate "pending" status while a tool runs.
    private const string AgentConversationsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS agent_conversation (
            id UUID PRIMARY KEY,
            user_id UUID NOT NULL,
            page_key TEXT NOT NULL,
            title TEXT NULL,
            provider_kind TEXT NULL,
            model_id TEXT NULL,
            connection_id UUID NULL REFERENCES external_connection (id) ON DELETE SET NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            last_message_at_utc TIMESTAMPTZ NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_conversation_user_page
            ON agent_conversation (user_id, page_key, last_message_at_utc DESC);

        CREATE INDEX IF NOT EXISTS ix_agent_conversation_user
            ON agent_conversation (user_id, last_message_at_utc DESC);

        CREATE TABLE IF NOT EXISTS agent_message (
            id UUID PRIMARY KEY,
            conversation_id UUID NOT NULL REFERENCES agent_conversation (id) ON DELETE CASCADE,
            parent_message_id UUID NULL REFERENCES agent_message (id) ON DELETE SET NULL,
            role TEXT NOT NULL,
            content_json JSONB NOT NULL,
            provider_kind TEXT NULL,
            model_id TEXT NULL,
            input_tokens INTEGER NULL,
            output_tokens INTEGER NULL,
            cache_read_tokens INTEGER NULL,
            cache_write_tokens INTEGER NULL,
            stop_reason TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_message_conversation
            ON agent_message (conversation_id, created_at_utc);

        CREATE TABLE IF NOT EXISTS agent_tool_call (
            id UUID PRIMARY KEY,
            message_id UUID NOT NULL REFERENCES agent_message (id) ON DELETE CASCADE,
            tool_use_id TEXT NOT NULL,
            tool_name TEXT NOT NULL,
            args_json JSONB NOT NULL,
            result_json JSONB NULL,
            status TEXT NOT NULL,
            error_text TEXT NULL,
            started_at_utc TIMESTAMPTZ NOT NULL,
            finished_at_utc TIMESTAMPTZ NULL,
            duration_ms INTEGER NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_tool_call_message
            ON agent_tool_call (message_id);
        """;

    // Adds a "Models" entry under the existing Chatbot group in the
    // site-config left-nav. Separate seed key from the Chatbot group itself
    // so existing deployments pick up the new item on next boot without
    // overwriting any admin reorganization of the group.
    private const string SiteConfigChatbotModelsMenuSql =
        """
        DO $$
        DECLARE
            site_id UUID := '00000000-0000-0000-0001-000000000004';
            chatbot_group_id UUID;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'site_config_chatbot_models_menu_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = site_id) THEN
                    SELECT id INTO chatbot_group_id
                    FROM menu_items
                    WHERE menu_id = site_id
                      AND parent_id IS NULL
                      AND display_name = 'Chatbot'
                      AND item_type = 'group'
                    LIMIT 1;

                    IF chatbot_group_id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM menu_items
                        WHERE menu_id = site_id
                          AND parent_id = chatbot_group_id
                          AND config->>'templateKey' = 'configChatbotModels'
                    ) THEN
                        INSERT INTO menu_items (
                            id, menu_id, parent_id, sort_order, display_name, icon,
                            item_type, config, is_visible, is_system,
                            created_at_utc, updated_at_utc
                        )
                        VALUES (
                            gen_random_uuid(), site_id, chatbot_group_id, 1,
                            'Models', 'fa fa-microchip',
                            'template',
                            '{{"templateKey":"configChatbotModels","path":"/admin/config/chatbot-models"}}'::jsonb,
                            TRUE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_chatbot_models_menu_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Catalogue of LLM models AutoNate is aware of: drives the External
    // Connections model dropdown and the agent loop's per-model
    // context-window lookup. Model id is unique (e.g. "claude-sonnet-4-6").
    // Costs are stored per million tokens to keep precision sane (typical
    // values run from $0.15 to $75 / Mtok); cost_published_at_utc lets the
    // admin track how stale the snapshot is. Archive instead of delete so
    // a connection still pointing at a retired model can resolve its
    // context window without losing the row.
    private const string AgentModelCatalogSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS agent_model (
            id UUID PRIMARY KEY,
            model_id TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            provider TEXT NOT NULL,
            context_window_tokens INTEGER NOT NULL,
            input_cost_per_million_tokens NUMERIC(10, 4) NULL,
            output_cost_per_million_tokens NUMERIC(10, 4) NULL,
            cost_currency TEXT NOT NULL DEFAULT 'USD',
            cost_published_at_utc TIMESTAMPTZ NULL,
            description TEXT NULL,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_agent_model_provider
            ON agent_model (provider, sort_order, model_id)
            WHERE is_archived = FALSE;
        """;

    // Seeds the agent_model table with the models AutoNate previously kept
    // in a static in-code lookup table. ON CONFLICT DO NOTHING so re-running is
    // safe and never overwrites an admin's edits — once a row exists the
    // admin owns it, the seed is just bootstrap. Costs are per million
    // tokens; values reflect the providers' published USD pricing as of
    // 2026-05 and are timestamped accordingly so admins know how stale
    // the snapshot is.
    private const string AgentModelCatalogSeedSql =
        """
        INSERT INTO agent_model (
            id, model_id, display_name, provider, context_window_tokens,
            input_cost_per_million_tokens, output_cost_per_million_tokens,
            cost_currency, cost_published_at_utc, description, is_archived,
            sort_order, created_at_utc, updated_at_utc
        ) VALUES
            (gen_random_uuid(), 'claude-opus-4-7', 'Claude Opus 4.7', 'Anthropic', 200000, 15.00, 75.00, 'USD', '2026-05-01', 'Highest-capability Anthropic model. Best for deep reasoning, complex multi-step coding, and demanding agentic workflows. Most expensive — reserve for hard problems.', FALSE, 10, NOW(), NOW()),
            (gen_random_uuid(), 'claude-opus-4-7[1m]', 'Claude Opus 4.7 (1M context)', 'Anthropic', 1000000, 15.00, 75.00, 'USD', '2026-05-01', 'Opus 4.7 with the 1M-token extended-context beta. Ideal for very long documents, large codebases, and conversations that legitimately need more than 200K tokens.', FALSE, 11, NOW(), NOW()),
            (gen_random_uuid(), 'claude-sonnet-4-6', 'Claude Sonnet 4.6', 'Anthropic', 200000, 3.00, 15.00, 'USD', '2026-05-01', 'Balanced capability and cost — the default workhorse for most production chat and tool-use workloads. Solid coding and reasoning at a fraction of Opus pricing.', FALSE, 20, NOW(), NOW()),
            (gen_random_uuid(), 'claude-sonnet-4-6[1m]', 'Claude Sonnet 4.6 (1M context)', 'Anthropic', 1000000, 3.00, 15.00, 'USD', '2026-05-01', 'Sonnet 4.6 with the 1M-token extended-context beta. Great when long-context needs outweigh raw reasoning depth.', FALSE, 21, NOW(), NOW()),
            (gen_random_uuid(), 'claude-haiku-4-5', 'Claude Haiku 4.5', 'Anthropic', 200000, 1.00, 5.00, 'USD', '2026-05-01', 'Fastest Anthropic tier. Use for quick lookups, classification, summarization, and high-volume lightweight chat. Minimal latency.', FALSE, 30, NOW(), NOW()),
            (gen_random_uuid(), 'claude-3-5-sonnet-latest', 'Claude 3.5 Sonnet', 'Anthropic', 200000, 3.00, 15.00, 'USD', '2026-05-01', 'Prior-generation Anthropic flagship. Still strong at code and analysis; pin to it if you want stable behavior across releases.', FALSE, 40, NOW(), NOW()),
            (gen_random_uuid(), 'claude-3-5-haiku-latest', 'Claude 3.5 Haiku', 'Anthropic', 200000, 0.80, 4.00, 'USD', '2026-05-01', 'Fast prior-generation tier. Good for high-volume processing where 4.5 isn''t available.', FALSE, 50, NOW(), NOW()),
            (gen_random_uuid(), 'claude-3-opus-latest', 'Claude 3 Opus', 'Anthropic', 200000, 15.00, 75.00, 'USD', '2026-05-01', 'Older flagship retained for compatibility with workflows pinned to it.', FALSE, 60, NOW(), NOW()),
            (gen_random_uuid(), 'claude-3-haiku-20240307', 'Claude 3 Haiku', 'Anthropic', 200000, 0.25, 1.25, 'USD', '2026-05-01', 'Cheapest catalogued Anthropic model. Use for the most cost-sensitive batch workloads.', FALSE, 70, NOW(), NOW()),
            (gen_random_uuid(), 'gpt-4.1', 'GPT-4.1', 'OpenAI', 1047576, 2.00, 8.00, 'USD', '2026-05-01', 'OpenAI''s ~1M-context flagship. Best for long documents, multi-file code review, and large transcript analysis.', FALSE, 100, NOW(), NOW()),
            (gen_random_uuid(), 'gpt-4o', 'GPT-4o', 'OpenAI', 128000, 2.50, 10.00, 'USD', '2026-05-01', 'Strong multimodal generalist (vision + audio). Solid default when you want OpenAI behavior at moderate cost.', FALSE, 110, NOW(), NOW()),
            (gen_random_uuid(), 'gpt-4o-mini', 'GPT-4o mini', 'OpenAI', 128000, 0.15, 0.60, 'USD', '2026-05-01', 'Very cheap multimodal. Great for high-volume preprocessing, classification, and routing decisions.', FALSE, 120, NOW(), NOW()),
            (gen_random_uuid(), 'o1', 'o1', 'OpenAI', 200000, 15.00, 60.00, 'USD', '2026-05-01', 'OpenAI reasoning-specialist. Uses internal chain-of-thought; excels at math, planning, and rigorous multi-step analysis. High latency.', FALSE, 130, NOW(), NOW()),
            (gen_random_uuid(), 'o1-mini', 'o1-mini', 'OpenAI', 128000, 3.00, 12.00, 'USD', '2026-05-01', 'Cheaper reasoning model. Good for STEM and code reasoning when full o1 cost is hard to justify.', FALSE, 140, NOW(), NOW()),
            (gen_random_uuid(), 'o3', 'o3', 'OpenAI', 200000, 10.00, 40.00, 'USD', '2026-05-01', 'Newer reasoning specialist. Stronger than o1 on agentic and tool-use scenarios.', FALSE, 150, NOW(), NOW()),
            (gen_random_uuid(), 'o3-mini', 'o3-mini', 'OpenAI', 200000, 1.10, 4.40, 'USD', '2026-05-01', 'Cheap, fast reasoning model. Good first stop for analytical tasks before reaching for o3 or o1.', FALSE, 160, NOW(), NOW()),
            (gen_random_uuid(), 'gpt-4-turbo', 'GPT-4 Turbo', 'OpenAI', 128000, 10.00, 30.00, 'USD', '2026-05-01', 'Older 128K-context flagship. Mostly superseded by gpt-4o; kept for pinned workflows.', FALSE, 170, NOW(), NOW()),
            (gen_random_uuid(), 'gpt-3.5-turbo', 'GPT-3.5 Turbo', 'OpenAI', 16385, 0.50, 1.50, 'USD', '2026-05-01', 'Legacy budget tier. Use only for very simple chat or classification — capability is well behind the 4-series.', FALSE, 180, NOW(), NOW())
        ON CONFLICT (model_id) DO NOTHING;
        """;

    // Adds is_default and is_available flags to the model catalog.
    // is_default (global, single row): the model the chatbot picks by
    // default. is_available: gates whether the agent may pick the model
    // for autonomous task routing (UI-controlled). Idempotent ALTER:
    // existing rows backfill with available=TRUE, default=FALSE; one
    // row (claude-sonnet-4-6 by preference) is promoted as the seed
    // default so chat works out of the box.
    private const string AgentModelDefaultAvailableColumnsSql =
        """
        ALTER TABLE agent_model
            ADD COLUMN IF NOT EXISTS is_default BOOLEAN NOT NULL DEFAULT FALSE;

        ALTER TABLE agent_model
            ADD COLUMN IF NOT EXISTS is_available BOOLEAN NOT NULL DEFAULT TRUE;

        -- Bootstrap exactly one default on first run so the chatbot has a
        -- model to use when a connection doesn't pin one. Re-runs are no-
        -- ops once any default exists.
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM agent_model WHERE is_default = TRUE) THEN
                UPDATE agent_model SET is_default = TRUE
                WHERE id = (
                    SELECT id FROM agent_model
                    WHERE is_archived = FALSE
                      AND model_id = 'claude-sonnet-4-6'
                    LIMIT 1
                );
            END IF;
        END $$;

        -- Migration from the earlier per-provider unique index to a single
        -- global default. Drop the per-provider index, collapse multiple
        -- defaults down to one (preferring claude-sonnet-4-6 if present),
        -- then add the global partial-unique index. The expression
        -- ((1)) makes the index value a constant so partial-unique on
        -- is_default = TRUE allows at most one row.
        DROP INDEX IF EXISTS ux_agent_model_default_per_provider;

        DO $$
        DECLARE keep_id UUID;
        BEGIN
            SELECT id INTO keep_id FROM agent_model
            WHERE is_default = TRUE
            ORDER BY
                CASE WHEN model_id = 'claude-sonnet-4-6' THEN 0 ELSE 1 END,
                sort_order ASC,
                id ASC
            LIMIT 1;

            IF keep_id IS NOT NULL THEN
                UPDATE agent_model
                SET is_default = FALSE,
                    updated_at_utc = NOW()
                WHERE is_default = TRUE
                  AND id <> keep_id;
            END IF;
        END $$;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_model_default_global
            ON agent_model ((1))
            WHERE is_default = TRUE;
        """;

    // Adds the kind/replaces_through columns introduced when conversation
    // compaction shipped. ConversationCompactor inserts kind='summary' rows
    // with replaces_through_message_id pointing at the last message they
    // subsume; LoadMessagesAsync uses these to skip rolled-up history when
    // building the next prompt. Old conversations backfill kind='chat'.
    private const string AgentMessageSummaryColumnsSql =
        """
        ALTER TABLE agent_message
            ADD COLUMN IF NOT EXISTS kind TEXT NOT NULL DEFAULT 'chat';

        ALTER TABLE agent_message
            ADD COLUMN IF NOT EXISTS replaces_through_message_id UUID NULL
                REFERENCES agent_message (id) ON DELETE SET NULL;

        CREATE INDEX IF NOT EXISTS ix_agent_message_conversation_summary
            ON agent_message (conversation_id, created_at_utc)
            WHERE kind = 'summary';
        """;

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowVersioningSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowDefaultVariablesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowExecutionErrorsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowTaskCompletionsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsDataSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsEdgesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsCommentsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordWatchesSchemaSql, cancellationToken);
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
        await dbContext.Database.ExecuteSqlRawAsync(PageTemplatesThumbnailSeedSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginsIconMenuRemovalSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(PluginsSiteConfigMenuSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigSystemHealthSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigSystemIssuesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigFormsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigChatbotSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(FormsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(NotificationsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteSettingsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AuditOutboxSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AuditOutboxDeadLettersSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SystemIssuesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(LocalUserLockoutSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(ExternalConnectionsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AgentConversationsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AgentMessageSummaryColumnsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AgentModelCatalogSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AgentModelCatalogSeedSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AgentModelDefaultAvailableColumnsSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(SiteConfigChatbotModelsMenuSql, cancellationToken);

        var authOptions = scope.ServiceProvider
            .GetService<IOptions<AuthorizationOptions>>()?.Value
            ?? new AuthorizationOptions();
        if (authOptions.AssignSuperAdminToAllExistingUsers)
        {
            await dbContext.Database.ExecuteSqlRawAsync(SuperAdminBackfillSql, cancellationToken);
        }
    }
}
