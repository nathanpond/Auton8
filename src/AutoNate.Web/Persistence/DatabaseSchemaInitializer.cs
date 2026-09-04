using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using Npgsql;
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

        -- auth_cache_version is gone (archived-43). It was bumped on every grant,
        -- role and group mutation for a process-wide auth cache that was
        -- never built: Authorizer is registered scoped, so its grant and
        -- SQL-filter caches live and die inside one request and cannot go
        -- stale across a mutation. Nothing ever SELECTed the version.
        DROP TABLE IF EXISTS auth_cache_version;

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

        -- Legacy partial index (Phase 1, page items only). Superseded by the
        -- broader ix_menu_items_config_path below which also covers template
        -- items that mount themselves at config.path. Drop so we don't
        -- maintain two overlapping indexes on inserts/updates.
        DROP INDEX IF EXISTS ix_menu_items_page_path;

        -- Lookup index for GetPageByPathAsync. Covers page + template (whose
        -- URL is in config->>'path') so a per-request page resolution is an
        -- O(log n) jsonb-path probe instead of a full scan + in-memory
        -- filter. No partial WHERE so the planner can use it for any future
        -- item_type that stores a path here; selectivity is high either way.
        CREATE INDEX IF NOT EXISTS ix_menu_items_config_path
            ON menu_items ((config->>'path'));

        -- Symmetric index for the route-alias case (config->>'aliasPath').
        -- Same lookup uses both indexes via Postgres BitmapOr when the two
        -- candidate paths overlap.
        CREATE INDEX IF NOT EXISTS ix_menu_items_config_alias_path
            ON menu_items ((config->>'aliasPath'))
            WHERE item_type = 'route';

        CREATE TABLE IF NOT EXISTS status_appearance_entries (
            id UUID PRIMARY KEY,
            status TEXT NOT NULL UNIQUE,
            color TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );

        ALTER TABLE status_appearance_entries
            ADD COLUMN IF NOT EXISTS sort_order INTEGER NOT NULL DEFAULT 0;
        -- Backfill sort_order for existing rows that landed at the default 0:
        -- give them a stable order based on creation time so prior layouts
        -- don't get scrambled the first time the new sort kicks in.
        WITH ordered AS (
            SELECT id,
                   ROW_NUMBER() OVER (ORDER BY created_at_utc, status) AS rn
              FROM status_appearance_entries
             WHERE sort_order = 0
        )
        UPDATE status_appearance_entries s
           SET sort_order = ordered.rn
          FROM ordered
         WHERE s.id = ordered.id;

        -- Auto-seed Site_Default. Lookup is case-insensitive (see SPA's
        -- lib/statusAppearance.ts) so any existing 'site_default' row counts.
        INSERT INTO status_appearance_entries (id, status, color, sort_order, created_at_utc, created_by, updated_at_utc, updated_by)
        SELECT gen_random_uuid(), 'Site_Default', '#6c757d', 0,
               NOW(), '00000000-0000-0000-0000-000000000000',
               NOW(), '00000000-0000-0000-0000-000000000000'
         WHERE NOT EXISTS (
            SELECT 1 FROM status_appearance_entries WHERE LOWER(status) = 'site_default'
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
            surface_dimmed_color TEXT NOT NULL DEFAULT '#6c757d',
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
        ALTER TABLE site_appearance_settings
            ADD COLUMN IF NOT EXISTS surface_dimmed_color TEXT NOT NULL DEFAULT '#6c757d';

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
                    surface_secondary_bg, surface_text_color, surface_dimmed_color,
                    border_color, dropdown_bg, modal_bg, secondary_button_bg,
                    secondary_button_text_color, secondary_button_border_color,
                    secondary_button_hover_bg, secondary_button_hover_text_color,
                    created_at_utc, created_by,
                    updated_at_utc, updated_by)
                VALUES (
                    appearance_id, 'Auton8', 'icon', NULL, 'fa fa-robot', 'Auton8',
                    'Sign in to continue to the automation dashboard',
                    '/assets/img/login-bg/space.jpg', '#008080',
                    '#ffffff', '#212529', '#20252a', '#a6aaac',
                    '#20252a', '#ffffff', '#20252a', '#ffffff',
                    '#ffffff', '#6c757d', '#212529', '#f1f3f5',
                    '#212529', '#212529', '#ffffff', '#5c636a',
                    '#ffffff', '#dee2e6', '#212529', '#6c757d',
                    '#ced4da', '#ffffff', '#ffffff', '#ffffff', '#495057',
                    '#6c757d', '#f1f3f5', '#212529',
                    NOW(), seed_actor, NOW(), seed_actor);
            END IF;

            UPDATE site_appearance_settings
            SET secondary_button_bg = COALESCE(secondary_button_bg, '#ffffff'),
                secondary_button_text_color = COALESCE(secondary_button_text_color, '#495057'),
                secondary_button_border_color = COALESCE(secondary_button_border_color, '#6c757d'),
                secondary_button_hover_bg = COALESCE(secondary_button_hover_bg, '#f1f3f5'),
                secondary_button_hover_text_color = COALESCE(secondary_button_hover_text_color, '#212529'),
                surface_dimmed_color = COALESCE(surface_dimmed_color, '#6c757d')
            WHERE id = appearance_id;

            -- Two shipped defaults failed WCAG and were corrected in the
            -- SPA's DEFAULT_SITE_APPEARANCE without this seed being updated,
            -- so every install still carried the failing values (archived-40):
            --   sidebar_section_color #adb5bd = 2.07:1 on the white sidebar
            --     (needs 4.5:1 — 0.78rem bold uppercase is not "large text")
            --   primary_accent_color #00acac  = 2.80:1 on the white surface
            --     (needs 3.0:1 as a UI component)
            -- Guarded on the exact old defaults so an admin's deliberate
            -- choice of either colour is left alone.
            UPDATE site_appearance_settings
            SET sidebar_section_color = '#5c636a',
                updated_at_utc = NOW(),
                updated_by = seed_actor
            WHERE id = appearance_id
              AND sidebar_section_color = '#adb5bd';

            UPDATE site_appearance_settings
            SET primary_accent_color = '#008080',
                updated_at_utc = NOW(),
                updated_by = seed_actor
            WHERE id = appearance_id
              AND primary_accent_color = '#00acac';

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
                VALUES (gen_random_uuid(), main_id, g, 0, 'Home', NULL, 'template', '{{"templateKey":"home","path":"/home"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

                g := gen_random_uuid();
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (g, main_id, NULL, 1, 'Records', 'fa fa-database', 'group', '{{"dynamicChildren":"recordTypes"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 0, 'Record Types', NULL, 'route', '{{"path":"/record-types"}}'::jsonb, TRUE, TRUE, NOW(), NOW());
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
                VALUES (gen_random_uuid(), main_id, g, 1, 'Record Relationship Types', NULL, 'route', '{{"path":"/record-relationship-types"}}'::jsonb, TRUE, TRUE, NOW(), NOW());

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
              (gen_random_uuid(), 'configPluginDocumentation', 'Plugin Documentation', 'How Auton8 plugins work and the patterns for working within them.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configForms', 'Forms (Site Config)', 'Define and manage form definitions.', TRUE, NOW(), NOW()),
              -- is_enabled FALSE: the component behind this key was a "coming soon"
              -- stub and has been removed, so the key resolves to NotFound. Kept as a
              -- disabled row rather than deleted so the path and key survive for a
              -- real implementation, and so the picker cannot offer a dead page.
              (gen_random_uuid(), 'configFormMappings', 'Form Mappings (Site Config)', 'Map forms to record types and fields.', FALSE, NOW(), NOW()),
              (gen_random_uuid(), 'configChatbotSettings', 'Chatbot Settings (Site Config)', 'Configure agent capabilities; applies to the next message.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configChatbotModels', 'Chatbot Models (Site Config)', 'LLM model catalogue used by external connections and the agent loop.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'dashboard', 'Dashboard', 'User-customizable dashboard with draggable, resizable widgets (data tables and charts).', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'query', 'Query', 'Run AQL queries against records, workflows, and other entities.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'dataStores', 'Data Stores', 'SQL and File-type data stores: schemas, ingested tables, file uploads.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'dataConnectors', 'Data Connectors', 'REST / SMB connectors that fetch external rows for Cached datasets.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'datasets', 'Datasets', 'Queryable surfaces over data stores and connectors, referenced from AQL as FROM Dataset("name").', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'pipelines', 'Pipelines', 'DAG-style data pipelines wiring dataset sources, transformers, analyzers, and sinks.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'codeTransformers', 'Code Transformers', 'User-authored JS / Python transformers and analyzers executed in the executor sidecar.', TRUE, NOW(), NOW()),
              (gen_random_uuid(), 'configProjections', 'Projections (Site Config)', 'Projection-framework cache admin: pause, resume, rebuild, retention.', TRUE, NOW(), NOW())
            ON CONFLICT (key) DO NOTHING;

            INSERT INTO menus (id, key, name, description, is_system,
                created_at_utc, created_by, updated_at_utc, updated_by)
            VALUES (standalone_id, 'standalone', 'Standalone Pages',
                'Page templates URL-reachable but not shown in any visible nav.',
                TRUE, NOW(), seed_actor, NOW(), seed_actor)
            ON CONFLICT (key) DO NOTHING;

            -- Convert any pre-existing route-typed menu items that point at a
            -- known templated path. Once converted, the WHERE clause stops
            -- matching them, making this naturally idempotent. Preserves the
            -- `path` alongside the new `templateKey` so the SPA's router still
            -- has a URL to match the menu item to — matches the shape Query and
            -- other modern template items already use ({{path, templateKey}}).
            -- Dropping path here was the cause of post-conversion 404s at /home
            -- on fresh databases.
            UPDATE menu_items mi
            SET item_type = 'template',
                config = jsonb_build_object(
                    'templateKey', mapping.template_key,
                    'path',        mapping.path),
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

            -- Surface the dashboard template under its own picker category so
            -- it groups nicely when there are more dashboard-style templates.
            UPDATE page_templates
            SET category = 'Dashboards'
            WHERE key = 'dashboard' AND category IS NULL;
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
        UPDATE page_templates SET thumbnail_url = 'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyMDAgMTUwIiB3aWR0aD0iMjAwIiBoZWlnaHQ9IjE1MCI+PHJlY3Qgd2lkdGg9IjIwMCIgaGVpZ2h0PSIxNTAiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB3aWR0aD0iMjAwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeT0iMjIiIHdpZHRoPSIyMDAiIGhlaWdodD0iMSIgZmlsbD0iI2RlZTJlNiIvPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjMiIGZpbGw9IiNmZjViNTciLz48Y2lyY2xlIGN4PSIyMiIgY3k9IjExIiByPSIzIiBmaWxsPSIjZjU5YzFhIi8+PGNpcmNsZSBjeD0iMzMiIGN5PSIxMSIgcj0iMyIgZmlsbD0iIzMyYTkzMiIvPjxyZWN0IHg9IjQ2IiB5PSI2IiB3aWR0aD0iMTIwIiBoZWlnaHQ9IjExIiByeD0iMiIgZmlsbD0iI2U5ZWNlZiIvPjxyZWN0IHg9IjEwIiB5PSIzMCIgd2lkdGg9IjcwIiBoZWlnaHQ9IjEwIiByeD0iMiIgZmlsbD0iIzM0OGZlMiIvPjxyZWN0IHg9IjE0IiB5PSIzMyIgd2lkdGg9IjQyIiBoZWlnaHQ9IjQiIHJ4PSIxIiBmaWxsPSIjZmZmZmZmIi8+PHJlY3QgeD0iODQiIHk9IjMwIiB3aWR0aD0iMjAiIGhlaWdodD0iMTAiIHJ4PSIyIiBmaWxsPSIjMzJhOTMyIi8+PHJlY3QgeD0iMTAiIHk9IjQ2IiB3aWR0aD0iODQiIGhlaWdodD0iNDYiIHJ4PSIzIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSIxNCIgeT0iNTAiIHdpZHRoPSIzNCIgaGVpZ2h0PSIzIiByeD0iMSIgZmlsbD0iIzcyN2NiNiIvPjxwb2x5bGluZSBwb2ludHM9IjE0LDgyIDI4LDcyIDQyLDc2IDU2LDYwIDcwLDY2IDg0LDU0IiBzdHJva2U9IiMwMGFjYWMiIHN0cm9rZS13aWR0aD0iMiIgZmlsbD0ibm9uZSIvPjxjaXJjbGUgY3g9IjE0IiBjeT0iODIiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjI4IiBjeT0iNzIiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjQyIiBjeT0iNzYiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjU2IiBjeT0iNjAiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9IjcwIiBjeT0iNjYiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxjaXJjbGUgY3g9Ijg0IiBjeT0iNTQiIHI9IjEuOCIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9Ijk4IiB5PSI0NiIgd2lkdGg9IjkyIiBoZWlnaHQ9IjQ2IiByeD0iMyIgZmlsbD0iI2ZmZmZmZiIgc3Ryb2tlPSIjZGVlMmU2Ii8+PHJlY3QgeD0iMTAyIiB5PSI1MCIgd2lkdGg9IjM0IiBoZWlnaHQ9IjMiIHJ4PSIxIiBmaWxsPSIjNzI3Y2I2Ii8+PHJlY3QgeD0iMTAyIiB5PSI1OCIgd2lkdGg9Ijg0IiBoZWlnaHQ9IjIiIHJ4PSIwLjUiIGZpbGw9IiNhZGI1YmQiLz48cmVjdCB4PSIxMDIiIHk9IjY0IiB3aWR0aD0iODQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSIxMDQiIHk9IjY2IiB3aWR0aD0iMjAiIGhlaWdodD0iMiIgcng9IjAuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjEzMCIgeT0iNjYiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyIiByeD0iMC41IiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTc0IiB5PSI2NiIgd2lkdGg9IjEwIiBoZWlnaHQ9IjIiIHJ4PSIwLjUiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxMDIiIHk9IjcyIiB3aWR0aD0iODQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmZmZmZmYiLz48cmVjdCB4PSIxMDQiIHk9Ijc0IiB3aWR0aD0iMjAiIGhlaWdodD0iMiIgcng9IjAuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjEzMCIgeT0iNzQiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyIiByeD0iMC41IiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTc0IiB5PSI3NCIgd2lkdGg9IjEwIiBoZWlnaHQ9IjIiIHJ4PSIwLjUiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSIxMDIiIHk9IjgwIiB3aWR0aD0iODQiIGhlaWdodD0iNiIgcng9IjEiIGZpbGw9IiNmOGY5ZmEiLz48cmVjdCB4PSIxMDQiIHk9IjgyIiB3aWR0aD0iMjAiIGhlaWdodD0iMiIgcng9IjAuNSIgZmlsbD0iIzQ5NTA1NyIvPjxyZWN0IHg9IjEzMCIgeT0iODIiIHdpZHRoPSI0MCIgaGVpZ2h0PSIyIiByeD0iMC41IiBmaWxsPSIjYWRiNWJkIi8+PHJlY3QgeD0iMTc0IiB5PSI4MiIgd2lkdGg9IjEwIiBoZWlnaHQ9IjIiIHJ4PSIwLjUiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIxMCIgeT0iOTYiIHdpZHRoPSIxODAiIGhlaWdodD0iNDYiIHJ4PSIzIiBmaWxsPSIjZmZmZmZmIiBzdHJva2U9IiNkZWUyZTYiLz48cmVjdCB4PSIxNCIgeT0iMTAwIiB3aWR0aD0iNDQiIGhlaWdodD0iMyIgcng9IjEiIGZpbGw9IiM3MjdjYjYiLz48cmVjdCB4PSIyMCIgeT0iMTIwIiB3aWR0aD0iMTAiIGhlaWdodD0iMTgiIGZpbGw9IiMwMGFjYWMiLz48cmVjdCB4PSIzNCIgeT0iMTEyIiB3aWR0aD0iMTAiIGhlaWdodD0iMjYiIGZpbGw9IiMzNDhmZTIiLz48cmVjdCB4PSI0OCIgeT0iMTE4IiB3aWR0aD0iMTAiIGhlaWdodD0iMjAiIGZpbGw9IiNmNTljMWEiLz48cmVjdCB4PSI2MiIgeT0iMTA4IiB3aWR0aD0iMTAiIGhlaWdodD0iMzAiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSI3NiIgeT0iMTI0IiB3aWR0aD0iMTAiIGhlaWdodD0iMTQiIGZpbGw9IiNmYjU1OTciLz48cmVjdCB4PSI5MCIgeT0iMTE1IiB3aWR0aD0iMTAiIGhlaWdodD0iMjMiIGZpbGw9IiMwMGFjYWMiLz48cmVjdCB4PSIxMDQiIHk9IjEyMCIgd2lkdGg9IjEwIiBoZWlnaHQ9IjE4IiBmaWxsPSIjMzQ4ZmUyIi8+PHJlY3QgeD0iMTE4IiB5PSIxMTAiIHdpZHRoPSIxMCIgaGVpZ2h0PSIyOCIgZmlsbD0iI2Y1OWMxYSIvPjxyZWN0IHg9IjEzMiIgeT0iMTE4IiB3aWR0aD0iMTAiIGhlaWdodD0iMjAiIGZpbGw9IiMzMmE5MzIiLz48cmVjdCB4PSIxNDYiIHk9IjExNiIgd2lkdGg9IjEwIiBoZWlnaHQ9IjIyIiBmaWxsPSIjZmI1NTk3Ii8+PHJlY3QgeD0iMTYwIiB5PSIxMjIiIHdpZHRoPSIxMCIgaGVpZ2h0PSIxNiIgZmlsbD0iIzAwYWNhYyIvPjxyZWN0IHg9IjE3NCIgeT0iMTEzIiB3aWR0aD0iMTAiIGhlaWdodD0iMjUiIGZpbGw9IiMzNDhmZTIiLz48L3N2Zz4=' WHERE key = 'dashboard' AND thumbnail_url IS NULL;
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

        -- Check-then-act on pg_roles is not safe here, and this is the same
        -- defect archived-192 fixed for the datastores writer role.
        --
        -- Roles are cluster-wide: pg_roles is a shared catalog and CREATE ROLE
        -- takes no lock that serialises concurrent creates. Two hosts starting
        -- at the same moment both see "not exists" and both issue CREATE, and
        -- the loser fails with 23505 on pg_authid_rolname_index. Each one owns
        -- a *different* database, so an advisory lock would not help either --
        -- pg_advisory_xact_lock's tag includes the database oid. Catching the
        -- error is what actually makes this safe.
        --
        -- Rare on a developer machine, reproducible on CI, which is where it
        -- turned up: one test in a 1666-test run, in a suite that had just
        -- passed locally.
        --
        -- The schema-initialisation advisory lock added around EnsureAsync does
        -- NOT make this handler redundant, and removing it on that basis would
        -- reintroduce the failure. Advisory lock keys are scoped to a database;
        -- pg_roles is a cluster-wide shared catalog. Two hosts owning different
        -- databases on one server take the lock independently — each succeeds
        -- immediately, because they are different locks — and then race this
        -- CREATE ROLE exactly as before. Catching the error is what makes it
        -- safe; the lock protects the per-database DDL around it.
        DO $$
        BEGIN
            CREATE ROLE plg_readers NOLOGIN;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL;
        END $$;

        GRANT USAGE ON SCHEMA public TO plg_readers;
        GRANT SELECT ON ALL TABLES IN SCHEMA public TO plg_readers;
        GRANT SELECT, USAGE ON ALL SEQUENCES IN SCHEMA public TO plg_readers;

        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT ON TABLES TO plg_readers;

        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT, USAGE ON SEQUENCES TO plg_readers;
        """;

    // Runs LAST, after every table exists (archived-62). Reading app tables is a
    // documented plugin capability (IPluginDataAccess), so plg_readers keeps a
    // broad SELECT — but "app tables" was never meant to include password
    // hashes, DataProtection-encrypted provider secrets, every other plugin's
    // role password, or share-link token hashes. Any uploaded plugin
    // authenticates as its own role, which inherits plg_readers, so without
    // these revokes one plugin could read them all.
    //
    // This cannot live beside the GRANT: several of these tables are created
    // later in the sequence, and ALTER DEFAULT PRIVILEGES would then hand them
    // straight back. Running it at the end, on every startup, also means a
    // re-grant or a newly added table cannot silently re-open access.
    private const string PluginReaderLockdownSql =
        """
        DO $$
        DECLARE
            t TEXT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'plg_readers') THEN
                RETURN;
            END IF;
            FOREACH t IN ARRAY ARRAY[
                'local_users',                -- password hashes, lockout state
                'external_connections',       -- encrypted provider secrets
                'plugins',                    -- role_password_encrypted for every plugin
                'saved_query_share_tokens'    -- share-link token hashes
            ]
            LOOP
                IF to_regclass('public.' || t) IS NOT NULL THEN
                    EXECUTE format('REVOKE SELECT ON public.%I FROM plg_readers', t);
                END IF;
            END LOOP;
        END $$;
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

    // The seeded "Features" item pointed at configFeatures, and
    // SettingGroup.Features has no settings defined — so the nav item led to
    // a form reading "No settings in this group yet." (archived-48). Removing the
    // seeded row rather than the template: the group is a declared extension
    // point (SiteSettingsRegistry's own "adding a new feature flag"
    // instructions name it), so the page and route stay available for
    // whoever adds the first flag. What is wrong today is only that the
    // navigation promises something that is not there.
    //
    // Idempotent: a DELETE matching nothing is a no-op on later startups.
    private const string EmptyFeaturesMenuRemovalSql =
        """
        DO $$
        DECLARE
            site_menu_id UUID;
        BEGIN
            SELECT id INTO site_menu_id FROM menus WHERE key = 'site-config' LIMIT 1;
            IF site_menu_id IS NULL THEN
                RETURN;
            END IF;

            DELETE FROM menu_items
            WHERE menu_id = site_menu_id
              AND is_system = TRUE
              AND config->>'templateKey' = 'configFeatures';
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
                            -- is_visible FALSE: same reason as the page_templates
                            -- row. The seed still creates it so an administrator
                            -- can switch it on the day the page is real.
                            FALSE, TRUE, NOW(), NOW()
                        );
                    END IF;
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('site_config_forms_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;

            -- Retire the Form Mappings stub on installs seeded before it was
            -- disabled above. The guards above are IF NOT EXISTS, so they will
            -- not revisit a row that already exists; without this, every
            -- existing deployment keeps a visible nav item pointing at a
            -- template key the SPA no longer registers.
            --
            -- Guarded on is_system so an item an administrator has taken over
            -- is left alone, and one-shot via auth_seed_state so switching it
            -- back on is not undone by the next restart.
            -- Rename Auto Nate -> Auton8 on installs seeded before the rename.
            --
            -- The seed rows are guarded by IF NOT EXISTS, so they will not
            -- revisit an existing install: without this, every deployment that
            -- has already run keeps the old name in the header, the login page
            -- and the browser tab, while a fresh one shows the new one. That
            -- drift is exactly what left the accent colour disagreeing between
            -- the SPA default and the seed.
            --
            -- Guarded on the old value, per column, so an administrator who has
            -- set their own site name or logo text keeps it. site_name and
            -- logo_text are updated independently because an install may have
            -- customised one and not the other.
            -- One-shot, like every other migration here. Guarding on the old
            -- value alone would still be *correct* on each boot, but it would
            -- also mean an administrator who deliberately sets the site name
            -- back to 'Auto Nate' has it renamed again by the next restart.
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'rebrand_auton8_v1') THEN
                UPDATE site_appearance_settings
                SET site_name = 'Auton8'
                WHERE site_name = 'Auto Nate';

                UPDATE site_appearance_settings
                SET logo_text = 'Auton8'
                WHERE logo_text = 'Auto Nate';

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('rebrand_auton8_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;

            -- Repoint installs seeded with the old login cover URL.
            --
            -- The seed pointed at '/spa/assets/img/login-bg/login-bg-17.jpg'.
            -- Nothing serves a '/spa' request path — static files are served at
            -- the root — so that URL 404'd on every install that took the
            -- default, and the SPA-side default disagreed with it. The image it
            -- named has also been removed: it carried the filename of a paid
            -- theme's demo asset, and this repository is going public.
            --
            -- Guarded on the exact old value so an administrator's own choice
            -- of cover image is never overwritten.
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'login_cover_url_fix_v1') THEN
                UPDATE site_appearance_settings
                SET login_cover_image_url = '/assets/img/login-bg/space.jpg'
                WHERE login_cover_image_url = '/spa/assets/img/login-bg/login-bg-17.jpg';

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('login_cover_url_fix_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'retire_form_mappings_stub_v1') THEN
                UPDATE menu_items
                SET is_visible = FALSE, updated_at_utc = NOW()
                WHERE item_type = 'template'
                  AND is_system = TRUE
                  AND is_visible = TRUE
                  AND config->>'templateKey' = 'configFormMappings';

                UPDATE page_templates
                SET is_enabled = FALSE, updated_at_utc = NOW()
                WHERE key = 'configFormMappings' AND is_enabled = TRUE;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('retire_form_mappings_stub_v1', NOW())
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

    // Content hierarchy: projects → cabinets → notebooks → pages → notes.
    // Pages are self-nesting (parent_page_id). Notes are leaves with one of
    // three content kinds (richtext / drawing / diagram). Pages and notes
    // are versioned via append-only *_versions tables modelled on form_versions.
    // Pages may carry binary attachments (metadata here, bytes on disk via
    // IContentAttachmentStore). content_ancestors materialises the closure
    // across the four permissionable kinds so IContentAuthorizer can resolve
    // inheritance in a single indexed lookup; maintained by ContentTreeService.
    private const string ContentHierarchySchemaSql =
        """
        CREATE TABLE IF NOT EXISTS projects (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            deletions_locked BOOLEAN NOT NULL DEFAULT FALSE,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_projects_updated_at_utc
            ON projects (updated_at_utc DESC);

        CREATE TABLE IF NOT EXISTS project_members (
            project_id UUID NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
            user_id UUID NOT NULL,
            role TEXT NOT NULL CHECK (role IN ('owner','contributor','viewer')),
            added_at_utc TIMESTAMPTZ NOT NULL,
            added_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL,
            PRIMARY KEY (project_id, user_id)
        );
        CREATE INDEX IF NOT EXISTS ix_project_members_user_id
            ON project_members (user_id);

        CREATE TABLE IF NOT EXISTS cabinets (
            id UUID PRIMARY KEY,
            project_id UUID NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            description TEXT NULL,
            icon TEXT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_cabinets_project_id ON cabinets (project_id);

        CREATE TABLE IF NOT EXISTS notebooks (
            id UUID PRIMARY KEY,
            cabinet_id UUID NOT NULL REFERENCES cabinets (id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            description TEXT NULL,
            icon TEXT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_notebooks_cabinet_id ON notebooks (cabinet_id);

        CREATE TABLE IF NOT EXISTS pages (
            id UUID PRIMARY KEY,
            notebook_id UUID NOT NULL REFERENCES notebooks (id) ON DELETE CASCADE,
            parent_page_id UUID NULL REFERENCES pages (id) ON DELETE CASCADE,
            title TEXT NOT NULL,
            body_jsonb JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            current_version_number INT NOT NULL DEFAULT 1,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL,
            CONSTRAINT ck_pages_no_self_parent
                CHECK (parent_page_id IS NULL OR parent_page_id <> id)
        );
        CREATE INDEX IF NOT EXISTS ix_pages_notebook_id ON pages (notebook_id);
        CREATE INDEX IF NOT EXISTS ix_pages_parent_page_id ON pages (parent_page_id);

        CREATE TABLE IF NOT EXISTS page_versions (
            id UUID PRIMARY KEY,
            page_id UUID NOT NULL REFERENCES pages (id) ON DELETE CASCADE,
            version_number INT NOT NULL,
            title TEXT NOT NULL,
            body_jsonb JSONB NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN ('autosave','manual','restore')),
            note TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS page_versions_page_id_version_number_key
            ON page_versions (page_id, version_number);
        CREATE INDEX IF NOT EXISTS ix_page_versions_page_id
            ON page_versions (page_id);

        CREATE TABLE IF NOT EXISTS page_attachments (
            id UUID PRIMARY KEY,
            page_id UUID NOT NULL REFERENCES pages (id) ON DELETE CASCADE,
            file_name TEXT NOT NULL,
            content_type TEXT NOT NULL,
            byte_size BIGINT NOT NULL,
            sha256_hex TEXT NOT NULL,
            storage_key TEXT NOT NULL,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_page_attachments_page_id
            ON page_attachments (page_id);
        CREATE INDEX IF NOT EXISTS ix_page_attachments_sha256
            ON page_attachments (sha256_hex);

        CREATE TABLE IF NOT EXISTS notes (
            id UUID PRIMARY KEY,
            page_id UUID NOT NULL REFERENCES pages (id) ON DELETE CASCADE,
            note_kind TEXT NOT NULL CHECK (note_kind IN ('richtext','drawing','diagram')),
            title TEXT NULL,
            content_jsonb JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            current_version_number INT NOT NULL DEFAULT 1,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_notes_page_id ON notes (page_id);

        CREATE TABLE IF NOT EXISTS note_versions (
            id UUID PRIMARY KEY,
            note_id UUID NOT NULL REFERENCES notes (id) ON DELETE CASCADE,
            version_number INT NOT NULL,
            title TEXT NULL,
            note_kind TEXT NOT NULL,
            content_jsonb JSONB NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN ('autosave','manual','restore')),
            note TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS note_versions_note_id_version_number_key
            ON note_versions (note_id, version_number);
        CREATE INDEX IF NOT EXISTS ix_note_versions_note_id
            ON note_versions (note_id);

        CREATE TABLE IF NOT EXISTS content_ancestors (
            descendant_kind TEXT NOT NULL,
            descendant_id UUID NOT NULL,
            ancestor_kind TEXT NOT NULL,
            ancestor_id UUID NOT NULL,
            depth INT NOT NULL,
            PRIMARY KEY (descendant_kind, descendant_id, ancestor_kind, ancestor_id)
        );
        CREATE INDEX IF NOT EXISTS ix_content_ancestors_desc
            ON content_ancestors (descendant_kind, descendant_id);
        CREATE INDEX IF NOT EXISTS ix_content_ancestors_anc
            ON content_ancestors (ancestor_kind, ancestor_id);
        """;

    // Short, human-friendly locator id shared across every content kind. We
    // keep it numeric for v1 (e.g. /notes/42) — slugs can layer on later. A
    // single sequence backs every table so locators are globally unique and
    // we don't need a discriminator in the URL. ADD COLUMN IF NOT EXISTS +
    // DEFAULT nextval(...) ensures pre-existing rows pick up a locator on
    // first run without bespoke backfill SQL.
    private const string ContentLocatorSchemaSql =
        """
        CREATE SEQUENCE IF NOT EXISTS content_locator_seq START 1;

        ALTER TABLE projects
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS projects_locator_key
            ON projects (locator);

        ALTER TABLE cabinets
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS cabinets_locator_key
            ON cabinets (locator);

        ALTER TABLE notebooks
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS notebooks_locator_key
            ON notebooks (locator);

        ALTER TABLE pages
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS pages_locator_key
            ON pages (locator);

        ALTER TABLE notes
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS notes_locator_key
            ON notes (locator);
        """;

    // Notes get a per-page sequential index so the SPA can encode them as a
    // short second URL segment (/notes/{pageLocator}/{pageNoteIndex}). The
    // global locator on notes still exists for cross-table uniqueness; this
    // is a friendlier short ref scoped to one page. Idempotent: ADD COLUMN
    // IF NOT EXISTS + the WHERE clause on the backfill UPDATE keeps re-runs
    // a no-op.
    private const string ContentNotePageIndexSql =
        """
        ALTER TABLE notes
            ADD COLUMN IF NOT EXISTS page_note_index INTEGER NULL;

        WITH numbered AS (
            SELECT id,
                   ROW_NUMBER() OVER (
                       PARTITION BY page_id
                       ORDER BY created_at_utc, id
                   ) AS rn
            FROM notes
            WHERE page_note_index IS NULL
        )
        UPDATE notes n
        SET page_note_index = numbered.rn
        FROM numbered
        WHERE n.id = numbered.id;

        ALTER TABLE notes
            ALTER COLUMN page_note_index SET NOT NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS notes_page_id_page_note_index_key
            ON notes (page_id, page_note_index);
        """;

    // SVG snapshot for drawing/diagram notes. Written by the SPA on idle
    // (Excalidraw exportToSvg / draw.io postMessage export) via PATCH, kept
    // out of the Yjs document on purpose so the CRDT update log doesn't
    // accumulate 50–200 KB of SVG per save. The page-embed renderer reads
    // this column to render a note's preview inline in view mode without
    // mounting the source editor.
    private const string NotePreviewSvgSql =
        """
        ALTER TABLE notes
            ADD COLUMN IF NOT EXISTS preview_svg TEXT NULL;
        """;

    // Documents subsystem (Phase 1 of the Documents feature plan
    // — see docs/plans/2026-05-26-documents-feature.md). Adds:
    //   • `commenter` to the project_members.role CHECK constraint (so the
    //     new 4th role can be persisted alongside owner/contributor/viewer).
    //   • folders table — self-referential, project-scoped, unlimited nesting.
    //     Mirrors content_locator_seq for cross-kind unique locators.
    // Idempotent: drop-and-recreate of the CHECK is gated on the constraint
    // name; CREATE TABLE IF NOT EXISTS is no-op on re-run.
    //
    // Folders are wired into IContentAuthorizer / ContentTreeService via the
    // EntityKinds.Folder + ContentKinds.Folder constants; closure rows go into
    // the existing content_ancestors table so inheritance "just works."
    private const string ContentDocumentsSchemaSql =
        """
        DO $$
        BEGIN
            -- Refresh the project_members.role CHECK constraint to include
            -- 'commenter'. The original constraint was created inline so it
            -- carries the Postgres default name `project_members_role_check`.
            IF EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'project_members_role_check'
            ) THEN
                ALTER TABLE project_members
                    DROP CONSTRAINT project_members_role_check;
            END IF;
            ALTER TABLE project_members
                ADD CONSTRAINT project_members_role_check
                CHECK (role IN ('owner','contributor','commenter','viewer'));
        END $$;

        CREATE TABLE IF NOT EXISTS folders (
            id UUID PRIMARY KEY,
            project_id UUID NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
            parent_folder_id UUID NULL REFERENCES folders (id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            description TEXT NULL,
            icon TEXT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL,
            CONSTRAINT ck_folders_no_self_parent
                CHECK (parent_folder_id IS NULL OR parent_folder_id <> id)
        );
        CREATE INDEX IF NOT EXISTS ix_folders_project_id ON folders (project_id);
        CREATE INDEX IF NOT EXISTS ix_folders_parent_folder_id
            ON folders (parent_folder_id);

        ALTER TABLE folders
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS folders_locator_key
            ON folders (locator);

        -- Documents (Phase 2 of the Documents feature). One entity covers
        -- documents AND templates, distinguished by `kind`. folder_id is
        -- nullable so a document can live at the project root.
        -- template_id is a soft self-reference: documents created from a
        -- template carry that link, but they own their own body copy.
        CREATE TABLE IF NOT EXISTS documents (
            id UUID PRIMARY KEY,
            project_id UUID NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
            folder_id UUID NULL REFERENCES folders (id) ON DELETE CASCADE,
            kind TEXT NOT NULL CHECK (kind IN ('document','template')),
            template_id UUID NULL REFERENCES documents (id) ON DELETE SET NULL,
            title TEXT NOT NULL,
            description TEXT NULL,
            body_jsonb JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            current_version_number INT NOT NULL DEFAULT 1,
            sort_order INT NOT NULL DEFAULT 0,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_documents_project_id ON documents (project_id);
        CREATE INDEX IF NOT EXISTS ix_documents_folder_id ON documents (folder_id);
        CREATE INDEX IF NOT EXISTS ix_documents_template_id ON documents (template_id);

        ALTER TABLE documents
            ADD COLUMN IF NOT EXISTS locator BIGINT NOT NULL
            DEFAULT nextval('content_locator_seq');
        CREATE UNIQUE INDEX IF NOT EXISTS documents_locator_key
            ON documents (locator);

        CREATE TABLE IF NOT EXISTS document_versions (
            id UUID PRIMARY KEY,
            document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
            version_number INT NOT NULL,
            title TEXT NOT NULL,
            body_jsonb JSONB NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN ('autosave','manual','restore')),
            note TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS document_versions_document_id_version_number_key
            ON document_versions (document_id, version_number);
        CREATE INDEX IF NOT EXISTS ix_document_versions_document_id
            ON document_versions (document_id);

        -- Document comments (Phase 4). Range markers in the body Y.Doc carry
        -- only the integer `number`; metadata (author, body, replies,
        -- resolved status) lives here. `(document_id, number)` is unique
        -- because docx-editor's body markers reference it; allocation is
        -- client-side via Math.max(existing) + 1 — extremely rare conflicts
        -- handled with an HTTP 409 from the create endpoint.
        CREATE TABLE IF NOT EXISTS document_comments (
            id UUID PRIMARY KEY,
            document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
            number INT NOT NULL,
            parent_comment_id UUID NULL REFERENCES document_comments (id) ON DELETE CASCADE,
            thread_id UUID NOT NULL,
            author_id UUID NOT NULL,
            body_text TEXT NOT NULL,
            resolved_at_utc TIMESTAMPTZ NULL,
            resolved_by_user_id UUID NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            CONSTRAINT ck_document_comments_no_self_parent
                CHECK (parent_comment_id IS NULL OR parent_comment_id <> id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS document_comments_document_id_number_key
            ON document_comments (document_id, number);
        CREATE INDEX IF NOT EXISTS ix_document_comments_document_id_thread_id
            ON document_comments (document_id, thread_id);
        CREATE INDEX IF NOT EXISTS ix_document_comments_open
            ON document_comments (document_id) WHERE resolved_at_utc IS NULL;

        -- Document bindings (Phase 5). The document body carries only a
        -- placeholder `{{binding:<id>}}`; the resolved value lives here
        -- in last_resolved_value_jsonb. Snapshot-on-open semantics —
        -- per-binding refresh + a global refresh-all explicitly trigger
        -- re-resolution. CASCADE on document delete prunes orphans.
        CREATE TABLE IF NOT EXISTS document_bindings (
            id UUID PRIMARY KEY,
            document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
            kind TEXT NOT NULL CHECK (kind IN ('record-field','aql-table')),
            config_jsonb JSONB NOT NULL,
            last_resolved_value_jsonb JSONB NULL,
            last_resolved_at_utc TIMESTAMPTZ NULL,
            last_resolved_by_user_id UUID NULL,
            label TEXT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_document_bindings_document_id
            ON document_bindings (document_id);
        """;

    // Adds a 'Documents' top-level item to the seeded 'main' menu. Separate
    // from MenusSchemaSql so existing installs (where the menu was seeded
    // before this feature existed) also pick it up. Idempotent: skips if the
    // menu doesn't exist yet (fresh install path will populate it via the
    // base seed once we add Documents there in a future migration) or if an
    // item with this exact name + path already lives at the top of `main`.
    private const string DocumentsMenuItemSeedSql =
        """
        DO $$
        DECLARE
            main_id UUID;
            seed_actor UUID;
            existing_top_count INT;
            new_sort_order INT;
        BEGIN
            SELECT id INTO main_id FROM menus WHERE key = 'main';
            IF main_id IS NULL THEN
                RETURN;
            END IF;

            IF EXISTS (
                SELECT 1 FROM menu_items mi
                WHERE mi.menu_id = main_id
                  AND mi.parent_id IS NULL
                  AND mi.item_type = 'route'
                  AND mi.config->>'path' = '/documents'
            ) THEN
                RETURN;
            END IF;

            SELECT user_id INTO seed_actor
            FROM local_users
            ORDER BY created_date ASC
            LIMIT 1;
            IF seed_actor IS NULL THEN
                RETURN;
            END IF;

            SELECT COALESCE(MAX(sort_order), -1) + 1 INTO new_sort_order
            FROM menu_items
            WHERE menu_id = main_id AND parent_id IS NULL;

            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system,
                created_at_utc, updated_at_utc
            )
            VALUES (
                gen_random_uuid(), main_id, NULL, new_sort_order,
                'Documents', 'fa fa-file-lines',
                'route', '{{"path":"/documents"}}'::jsonb,
                TRUE, TRUE, NOW(), NOW()
            );

            -- Touch existing top count so the cache-version refresh is
            -- predictable; harmless if the trigger isn't installed yet.
            SELECT COUNT(*) INTO existing_top_count
            FROM menu_items WHERE menu_id = main_id AND parent_id IS NULL;
        END $$;
        """;

    // Per-user page favorites. Composite PK (page_id, user_id) makes
    // PUT idempotent via ON CONFLICT DO NOTHING. Cascade on page delete so
    // favorites disappear with the page they pointed at.
    private const string PageFavoritesSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS page_favorites (
            page_id UUID NOT NULL REFERENCES pages (id) ON DELETE CASCADE,
            user_id UUID NOT NULL,
            favorited_at_utc TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (page_id, user_id)
        );
        CREATE INDEX IF NOT EXISTS ix_page_favorites_user_id
            ON page_favorites (user_id);
        """;

    // Owned by the Hocuspocus sidecar. `name` is the document identifier
    // (e.g. "page:<guid>", "note:<guid>"); `data` is the encoded Y.Doc state
    // produced by Yjs (Uint8Array). Hocuspocus's @hocuspocus/extension-database
    // calls `fetch` on load and `store` on debounced save against this table.
    // .NET never reads or writes it during normal operation — content reads
    // continue to go through the `body_jsonb` / `content_jsonb` mirror that
    // the webhook handler keeps current.
    private const string YjsDocumentsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS yjs_documents (
            name TEXT PRIMARY KEY,
            data BYTEA NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    // Inserts a single starter project so the Notes UI has something to open
    // on first launch. Idempotent: keyed off a fixed project id, so re-running
    // the initializer never duplicates the row. Skipped if no local_users
    // exist yet (the seed needs a real user to assign as Owner + created_by).
    // The first local_users row (oldest by created_date) is the owner — on a
    // greenfield install that's the bootstrap admin.
    private const string ContentSampleProjectSeedSql =
        """
        DO $$
        DECLARE
            seed_user_id UUID;
            seed_project_id UUID := '00000000-0000-0000-0000-000000010001'::uuid;
        BEGIN
            IF EXISTS (SELECT 1 FROM projects WHERE id = seed_project_id) THEN
                RETURN;
            END IF;

            SELECT user_id INTO seed_user_id
            FROM local_users
            ORDER BY created_date ASC
            LIMIT 1;

            IF seed_user_id IS NULL THEN
                RETURN;
            END IF;

            INSERT INTO projects (
                id, name, description, deletions_locked, is_archived,
                created_at_utc, updated_at_utc, created_by, updated_by
            )
            VALUES (
                seed_project_id,
                'Sample Project',
                'Default seed project to get you started with content authoring.',
                FALSE, FALSE, NOW(), NOW(), seed_user_id, seed_user_id
            );

            INSERT INTO project_members (
                project_id, user_id, role,
                added_at_utc, added_by, updated_at_utc, updated_by
            )
            VALUES (
                seed_project_id, seed_user_id, 'owner',
                NOW(), seed_user_id, NOW(), seed_user_id
            );

            INSERT INTO content_ancestors (
                descendant_kind, descendant_id, ancestor_kind, ancestor_id, depth
            )
            VALUES (
                'project', seed_project_id, 'project', seed_project_id, 0
            )
            ON CONFLICT DO NOTHING;
        END $$;
        """;

    // User-owned dashboards: a small content-style hierarchy of dashboards →
    // widgets, plus a `dashboard_shares` table that is created in v1 for the
    // future "share with user/group/role" feature but never written to yet.
    // Block is idempotent (CREATE IF NOT EXISTS + ALTER…IF NOT EXISTS) so it
    // is safe to run on every boot.
    private const string DashboardsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS dashboards (
            id UUID PRIMARY KEY,
            owner_user_id UUID NOT NULL,
            name TEXT NOT NULL,
            description TEXT NULL,
            visibility TEXT NOT NULL DEFAULT 'private',
            scope TEXT NOT NULL DEFAULT 'user',
            source TEXT NOT NULL DEFAULT 'user',
            template_key TEXT NULL,
            settings_jsonb JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            is_archived BOOLEAN NOT NULL DEFAULT FALSE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL,
            CONSTRAINT dashboards_visibility_check
                CHECK (visibility IN ('private', 'shared', 'public')),
            CONSTRAINT dashboards_scope_check
                CHECK (scope IN ('user', 'team', 'site')),
            CONSTRAINT dashboards_source_check
                CHECK (source IN ('user', 'template'))
        );

        CREATE INDEX IF NOT EXISTS ix_dashboards_owner_user_id_updated_at_utc
            ON dashboards (owner_user_id, updated_at_utc DESC);

        CREATE INDEX IF NOT EXISTS ix_dashboards_visibility_scope
            ON dashboards (visibility, scope);

        CREATE TABLE IF NOT EXISTS dashboard_widgets (
            id UUID PRIMARY KEY,
            dashboard_id UUID NOT NULL REFERENCES dashboards (id) ON DELETE CASCADE,
            widget_type TEXT NOT NULL,
            title TEXT NULL,
            config_jsonb JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            grid_x INT NOT NULL DEFAULT 0,
            grid_y INT NOT NULL DEFAULT 0,
            grid_w INT NOT NULL DEFAULT 4,
            grid_h INT NOT NULL DEFAULT 3,
            sort_order INT NOT NULL DEFAULT 0,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_dashboard_widgets_dashboard_id
            ON dashboard_widgets (dashboard_id);

        CREATE TABLE IF NOT EXISTS dashboard_shares (
            dashboard_id UUID NOT NULL REFERENCES dashboards (id) ON DELETE CASCADE,
            principal_type TEXT NOT NULL,
            principal_id UUID NOT NULL,
            role TEXT NOT NULL,
            granted_at_utc TIMESTAMPTZ NOT NULL,
            granted_by UUID NOT NULL,
            PRIMARY KEY (dashboard_id, principal_type, principal_id),
            CONSTRAINT dashboard_shares_principal_type_check
                CHECK (principal_type IN ('user', 'group', 'role')),
            CONSTRAINT dashboard_shares_role_check
                CHECK (role IN ('viewer', 'editor'))
        );

        CREATE INDEX IF NOT EXISTS ix_dashboard_shares_principal
            ON dashboard_shares (principal_type, principal_id);
        """;

    // User-saved AQL queries (name, optional description, query text, shared
    // flag). Owner-only edits/deletes; shared rows are visible to every
    // authenticated user. Per-owner name uniqueness — different users can use
    // the same name. Idempotent via CREATE IF NOT EXISTS and CREATE INDEX IF
    // NOT EXISTS so re-running on each boot is safe.
    private const string SavedQueriesSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS saved_queries (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            query_text TEXT NOT NULL,
            is_shared BOOLEAN NOT NULL DEFAULT FALSE,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_saved_queries_owner_user_id
            ON saved_queries (owner_user_id);

        CREATE INDEX IF NOT EXISTS ix_saved_queries_is_shared
            ON saved_queries (is_shared) WHERE is_shared = TRUE;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_saved_queries_owner_name
            ON saved_queries (owner_user_id, LOWER(name));
        """;

    // Adds a top-level "Query" menu item to the main menu so the AQL query
    // page is reachable from every install. Idempotent via auth_seed_state.
    // The original 'main' menu seed at line 941 only runs when the menu
    // doesn't exist yet, so existing databases need this separate block to
    // pick up the new item without re-seeding the whole nav.
    private const string QueryMenuSeedSql =
        """
        DO $$
        DECLARE
            main_id UUID := '00000000-0000-0000-0001-000000000001';
            next_sort INT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'query_menu_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = main_id)
                   AND NOT EXISTS (
                       SELECT 1 FROM menu_items
                       WHERE menu_id = main_id
                         AND config->>'templateKey' = 'query'
                   )
                THEN
                    SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_sort
                    FROM menu_items
                    WHERE menu_id = main_id AND parent_id IS NULL;

                    INSERT INTO menu_items (
                        id, menu_id, parent_id, sort_order, display_name, icon,
                        item_type, config, is_visible, is_system,
                        created_at_utc, updated_at_utc
                    )
                    VALUES (
                        gen_random_uuid(), main_id, NULL, next_sort,
                        'Query', 'fa fa-magnifying-glass-chart',
                        'template',
                        '{{"templateKey":"query","path":"/query"}}'::jsonb,
                        TRUE, TRUE, NOW(), NOW()
                    );
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('query_menu_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Adds a top-level "Data" group to the main menu with the five data-feature
    // pages as template children (Data Stores / Data Connectors / Datasets /
    // Pipelines / Code Transformers). The pages were previously anchored under
    // /admin/config/* and only reachable to admins via the Site Configuration
    // shell; they are permissionable user features, so they belong on the main
    // menu. Idempotent via auth_seed_state. Default paths are flat — admin can
    // rename through Pages / Menus.
    private const string DataMainMenuSeedSql =
        """
        DO $$
        DECLARE
            main_id UUID := '00000000-0000-0000-0001-000000000001';
            data_group_id UUID;
            next_sort INT;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM auth_seed_state WHERE key = 'main_menu_data_v1') THEN
                IF EXISTS (SELECT 1 FROM menus WHERE id = main_id)
                   AND NOT EXISTS (
                       SELECT 1 FROM menu_items
                       WHERE menu_id = main_id
                         AND parent_id IS NULL
                         AND display_name = 'Data'
                         AND item_type = 'group'
                   )
                THEN
                    SELECT COALESCE(MAX(sort_order), -1) + 1 INTO next_sort
                    FROM menu_items
                    WHERE menu_id = main_id AND parent_id IS NULL;

                    data_group_id := gen_random_uuid();

                    INSERT INTO menu_items (
                        id, menu_id, parent_id, sort_order, display_name, icon,
                        item_type, config, is_visible, is_system,
                        created_at_utc, updated_at_utc
                    )
                    VALUES (
                        data_group_id, main_id, NULL, next_sort,
                        'Data', 'fa fa-warehouse', 'group', '{{}}'::jsonb,
                        TRUE, TRUE, NOW(), NOW()
                    );

                    INSERT INTO menu_items (
                        id, menu_id, parent_id, sort_order, display_name, icon,
                        item_type, config, is_visible, is_system,
                        created_at_utc, updated_at_utc
                    )
                    VALUES
                        (gen_random_uuid(), main_id, data_group_id, 0,
                         'Data Stores', 'fa fa-database', 'template',
                         '{{"templateKey":"dataStores","path":"/datastores"}}'::jsonb,
                         TRUE, TRUE, NOW(), NOW()),
                        (gen_random_uuid(), main_id, data_group_id, 1,
                         'Data Connectors', 'fa fa-plug', 'template',
                         '{{"templateKey":"dataConnectors","path":"/dataconnectors"}}'::jsonb,
                         TRUE, TRUE, NOW(), NOW()),
                        (gen_random_uuid(), main_id, data_group_id, 2,
                         'Datasets', 'fa fa-table', 'template',
                         '{{"templateKey":"datasets","path":"/datasets"}}'::jsonb,
                         TRUE, TRUE, NOW(), NOW()),
                        (gen_random_uuid(), main_id, data_group_id, 3,
                         'Pipelines', 'fa fa-diagram-project', 'template',
                         '{{"templateKey":"pipelines","path":"/pipelines"}}'::jsonb,
                         TRUE, TRUE, NOW(), NOW()),
                        (gen_random_uuid(), main_id, data_group_id, 4,
                         'Code Transformers', 'fa fa-code', 'template',
                         '{{"templateKey":"codeTransformers","path":"/code-transformers"}}'::jsonb,
                         TRUE, TRUE, NOW(), NOW());
                END IF;

                INSERT INTO auth_seed_state (key, applied_at_utc)
                VALUES ('main_menu_data_v1', NOW())
                ON CONFLICT (key) DO NOTHING;
            END IF;
        END $$;
        """;

    // Projection framework bookkeeping. projection_versions tracks active vs.
    // shadow rows during reprojection; projection_watermarks holds per-feed
    // poll cursors so a restart doesn't replay history.
    private const string ProjectionFrameworkSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS projection_versions (
            name TEXT NOT NULL,
            version INT NOT NULL,
            status TEXT NOT NULL CHECK (status IN ('active', 'shadow', 'retired')),
            started_at_utc TIMESTAMPTZ NOT NULL,
            completed_at_utc TIMESTAMPTZ NULL,
            PRIMARY KEY (name, version)
        );

        CREATE INDEX IF NOT EXISTS ix_projection_versions_name_status
            ON projection_versions (name, status);

        CREATE TABLE IF NOT EXISTS projection_watermarks (
            feed_name TEXT PRIMARY KEY,
            watermark_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL
        );
        """;

    // Flowable workflow cache — three tables that AQL queries hit directly,
    // populated by the projection framework from the Flowable event bridge +
    // polling sweeper. auth_tags is a JSONB bag the selector compilers turn
    // into row predicates ({startedby, processkey, definitionkey, ...}).
    //
    // Time-partitioning is deferred to a later migration; at the data volumes
    // we hit before partitioning matters, BRIN over start_time/created_time
    // is enough and avoids the operational burden of monthly children.
    private const string WorkflowCacheSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS workflow_execution_cache (
            flowable_instance_id TEXT PRIMARY KEY,
            process_definition_key TEXT NOT NULL,
            process_definition_id TEXT NOT NULL,
            process_definition_version INT NULL,
            business_key TEXT NULL,
            tenant_id TEXT NULL,
            status TEXT NOT NULL,
            start_time TIMESTAMPTZ NOT NULL,
            end_time TIMESTAMPTZ NULL,
            duration_ms BIGINT NULL,
            started_by TEXT NULL,
            current_activity_id TEXT NULL,
            current_activity_name TEXT NULL,
            record_id BIGINT NULL,
            auth_tags JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            projection_version INT NOT NULL DEFAULT 1,
            last_sync_at TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_workflow_execution_cache_started_by
            ON workflow_execution_cache (started_by);

        CREATE INDEX IF NOT EXISTS ix_workflow_execution_cache_def_status
            ON workflow_execution_cache (process_definition_key, status);

        CREATE INDEX IF NOT EXISTS ix_workflow_execution_cache_record_id
            ON workflow_execution_cache (record_id) WHERE record_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS ix_workflow_execution_cache_auth_tags
            ON workflow_execution_cache USING GIN (auth_tags jsonb_path_ops);

        CREATE INDEX IF NOT EXISTS ix_workflow_execution_cache_start_time_brin
            ON workflow_execution_cache USING BRIN (start_time);

        CREATE TABLE IF NOT EXISTS workflow_task_cache (
            flowable_task_id TEXT PRIMARY KEY,
            flowable_instance_id TEXT NOT NULL,
            process_definition_key TEXT NOT NULL,
            task_definition_key TEXT NULL,
            name TEXT NULL,
            assignee TEXT NULL,
            owner TEXT NULL,
            candidate_users TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
            candidate_groups TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
            due_date TIMESTAMPTZ NULL,
            created_time TIMESTAMPTZ NOT NULL,
            claim_time TIMESTAMPTZ NULL,
            completed_time TIMESTAMPTZ NULL,
            form_key TEXT NULL,
            priority INT NULL,
            status TEXT NOT NULL,
            auth_tags JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            projection_version INT NOT NULL DEFAULT 1,
            last_sync_at TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_assignee_status
            ON workflow_task_cache (assignee, status);

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_instance
            ON workflow_task_cache (flowable_instance_id);

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_candidate_users
            ON workflow_task_cache USING GIN (candidate_users);

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_candidate_groups
            ON workflow_task_cache USING GIN (candidate_groups);

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_auth_tags
            ON workflow_task_cache USING GIN (auth_tags jsonb_path_ops);

        CREATE INDEX IF NOT EXISTS ix_workflow_task_cache_created_time_brin
            ON workflow_task_cache USING BRIN (created_time);

        -- Current-value snapshot of process variables per instance. History
        -- of variable changes is owned by the (Phase 2) event log table; this
        -- one is just "what's true right now."
        CREATE TABLE IF NOT EXISTS workflow_variable_cache (
            flowable_instance_id TEXT NOT NULL,
            name TEXT NOT NULL,
            value_text TEXT NULL,
            value_long BIGINT NULL,
            value_double DOUBLE PRECISION NULL,
            value_bool BOOLEAN NULL,
            value_json JSONB NULL,
            type TEXT NOT NULL,
            updated_time TIMESTAMPTZ NOT NULL,
            projection_version INT NOT NULL DEFAULT 1,
            last_sync_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (flowable_instance_id, name)
        );

        CREATE INDEX IF NOT EXISTS ix_workflow_variable_cache_value_json
            ON workflow_variable_cache USING GIN (value_json jsonb_path_ops)
            WHERE value_json IS NOT NULL;
        """;

    // Phase 2 — append-only history event log. One row per Flowable engine
    // event (activity start/end, task lifecycle, variable change). Drives
    // process-mining / time-series AQL queries. event_id is a stable hash
    // composed by the history projection from (instance, activity_instance_id,
    // kind, occurred_at) so re-emission from the polling feed is idempotent.
    //
    // BRIN on event_time keeps inserts cheap (no btree maintenance) and
    // accelerates the time-range scans that dominate analytical queries.
    private const string WorkflowEventLogSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS workflow_event_log_cache (
            event_id TEXT PRIMARY KEY,
            flowable_instance_id TEXT NOT NULL,
            process_definition_key TEXT NOT NULL,
            event_time TIMESTAMPTZ NOT NULL,
            event_type TEXT NOT NULL,
            activity_id TEXT NULL,
            activity_name TEXT NULL,
            activity_type TEXT NULL,
            task_id TEXT NULL,
            variable_name TEXT NULL,
            actor TEXT NULL,
            duration_ms BIGINT NULL,
            payload JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            projection_version INT NOT NULL DEFAULT 1,
            last_sync_at TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_workflow_event_log_instance_time
            ON workflow_event_log_cache (flowable_instance_id, event_time DESC);

        CREATE INDEX IF NOT EXISTS ix_workflow_event_log_def_type_time
            ON workflow_event_log_cache (process_definition_key, event_type, event_time DESC);

        CREATE INDEX IF NOT EXISTS ix_workflow_event_log_event_time_brin
            ON workflow_event_log_cache USING BRIN (event_time);

        CREATE INDEX IF NOT EXISTS ix_workflow_event_log_actor
            ON workflow_event_log_cache (actor) WHERE actor IS NOT NULL;
        """;

    // Per-process retention overrides for the cache janitor. Missing rows
    // mean "use the default" (7 years). Operators set this through the admin
    // surface; the janitor reads it on every sweep.
    private const string ProcessRetentionConfigSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS process_retention_config (
            process_definition_key TEXT PRIMARY KEY,
            retain_days INT NOT NULL CHECK (retain_days > 0),
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NULL
        );
        """;

    // First non-Flowable consumer of the projection framework. Aggregates
    // the records table on a (record_type_id, bucket_day) grain so dashboard
    // widgets can read activity stats without re-aggregating per request.
    // Filled by a polling feed that runs hourly; the row's last_sync_at
    // tells the admin page how fresh the rollup is.
    private const string RecordActivityRollupSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS record_activity_rollup_cache (
            record_type_id UUID NOT NULL,
            bucket_day DATE NOT NULL,
            records_created INT NOT NULL DEFAULT 0,
            records_updated INT NOT NULL DEFAULT 0,
            records_archived INT NOT NULL DEFAULT 0,
            projection_version INT NOT NULL DEFAULT 1,
            last_sync_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (record_type_id, bucket_day)
        );

        CREATE INDEX IF NOT EXISTS ix_record_activity_rollup_day
            ON record_activity_rollup_cache (bucket_day DESC);
        """;

    // Data Stores + DataConnectors metadata tables in the primary AutoNate DB
    // (docs/plans/2026-05-30-data-stores-implementation.md). The actual per-
    // datastore SQL schemas + read-only roles live in the second cluster DB
    // `autonate_datastores`, provisioned by DatastoresDatabaseInitializer in
    // a follow-up commit. File bytes live on disk under DataPaths.DatastoresRoot.
    // Names are globally unique (LOWER) so the Phase 2 AQL `Dataset("name")`
    // lookups have a stable single handle.
    private const string DataStoresSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS datastores (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            kind SMALLINT NOT NULL,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_datastores_name
            ON datastores (LOWER(name));

        CREATE INDEX IF NOT EXISTS ix_datastores_owner_user_id
            ON datastores (owner_user_id);

        CREATE INDEX IF NOT EXISTS ix_datastores_kind
            ON datastores (kind);

        CREATE TABLE IF NOT EXISTS dataconnectors (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            kind TEXT NOT NULL,
            config JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            last_fetched_at_utc TIMESTAMPTZ NULL,
            cursor TEXT NULL,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_dataconnectors_name
            ON dataconnectors (LOWER(name));

        CREATE INDEX IF NOT EXISTS ix_dataconnectors_owner_user_id
            ON dataconnectors (owner_user_id);

        CREATE INDEX IF NOT EXISTS ix_dataconnectors_kind
            ON dataconnectors (kind);

        CREATE TABLE IF NOT EXISTS datastore_files (
            id UUID PRIMARY KEY,
            datastore_id UUID NOT NULL REFERENCES datastores (id) ON DELETE CASCADE,
            folder_path TEXT NOT NULL DEFAULT '/',
            filename TEXT NOT NULL,
            storage_key TEXT NOT NULL,
            size_bytes BIGINT NOT NULL,
            content_type TEXT NULL,
            uploaded_by UUID NOT NULL,
            uploaded_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_datastore_files_datastore_id
            ON datastore_files (datastore_id);

        CREATE INDEX IF NOT EXISTS ix_datastore_files_datastore_folder
            ON datastore_files (datastore_id, folder_path);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_datastore_files_path
            ON datastore_files (datastore_id, folder_path, LOWER(filename));

        CREATE TABLE IF NOT EXISTS datastore_tables (
            id UUID PRIMARY KEY,
            datastore_id UUID NOT NULL REFERENCES datastores (id) ON DELETE CASCADE,
            schema_name TEXT NOT NULL,
            table_name TEXT NOT NULL,
            column_schema JSONB NOT NULL DEFAULT '[]'::jsonb,
            row_count BIGINT NOT NULL DEFAULT 0,
            created_by UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_datastore_tables_datastore_id
            ON datastore_tables (datastore_id);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_datastore_tables_datastore_schema_table
            ON datastore_tables (datastore_id, schema_name, LOWER(table_name));

        CREATE TABLE IF NOT EXISTS connector_runs (
            id UUID PRIMARY KEY,
            dataconnector_id UUID NOT NULL REFERENCES dataconnectors (id) ON DELETE CASCADE,
            started_at_utc TIMESTAMPTZ NOT NULL,
            completed_at_utc TIMESTAMPTZ NULL,
            status TEXT NOT NULL,
            rows_fetched BIGINT NOT NULL DEFAULT 0,
            error_message TEXT NULL,
            cursor_before TEXT NULL,
            cursor_after TEXT NULL,
            triggered_by UUID NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_connector_runs_dataconnector_id
            ON connector_runs (dataconnector_id);

        CREATE INDEX IF NOT EXISTS ix_connector_runs_started_at_utc
            ON connector_runs (started_at_utc DESC);
        """;

    // Datasets metadata table (Phase 2 of the Data Stores plan). The actual
    // cached rows live in `autonate_datastores.cache_<datasetid>` schemas,
    // provisioned by CachedDatasetStore on first refresh. Names are
    // case-insensitively unique so AQL `Dataset("name")` lookups have a
    // single stable handle.
    private const string DatasetsSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS datasets (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            mode SMALLINT NOT NULL,
            column_schema JSONB NOT NULL DEFAULT '[]'::jsonb,
            refresh_cron TEXT NULL,
            last_refreshed_at_utc TIMESTAMPTZ NULL,
            source_kind TEXT NOT NULL,
            source_id UUID NOT NULL,
            source_table_name TEXT NULL,
            file_scope_kind TEXT NULL,
            file_scope_path TEXT NULL,
            parser_kind TEXT NULL,
            parser_options JSONB NULL,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        ALTER TABLE datasets
            ADD COLUMN IF NOT EXISTS file_scope_kind TEXT NULL;
        ALTER TABLE datasets
            ADD COLUMN IF NOT EXISTS file_scope_path TEXT NULL;
        ALTER TABLE datasets
            ADD COLUMN IF NOT EXISTS parser_kind TEXT NULL;
        ALTER TABLE datasets
            ADD COLUMN IF NOT EXISTS parser_options JSONB NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_datasets_name
            ON datasets (LOWER(name));

        CREATE INDEX IF NOT EXISTS ix_datasets_owner_user_id
            ON datasets (owner_user_id);

        CREATE INDEX IF NOT EXISTS ix_datasets_source
            ON datasets (source_kind, source_id);
        """;

    // Phase 3 of the Data Stores plan — anonymous share tokens for saved
    // AQL queries. Stored as SHA-256 hex of the token so a DB read can't
    // reconstruct a working URL. ON DELETE CASCADE from saved_queries
    // sweeps every token when the underlying query is removed.
    private const string SavedQueryShareTokensSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS saved_query_share_tokens (
            id UUID PRIMARY KEY,
            saved_query_id UUID NOT NULL REFERENCES saved_queries (id) ON DELETE CASCADE,
            token_hash TEXT NOT NULL,
            issued_by UUID NOT NULL,
            issued_at_utc TIMESTAMPTZ NOT NULL,
            expires_at_utc TIMESTAMPTZ NULL,
            revoked_at_utc TIMESTAMPTZ NULL,
            max_uses INTEGER NULL,
            use_count INTEGER NOT NULL DEFAULT 0,
            last_used_at_utc TIMESTAMPTZ NULL,
            label TEXT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_saved_query_share_tokens_token_hash
            ON saved_query_share_tokens (token_hash);

        CREATE INDEX IF NOT EXISTS ix_saved_query_share_tokens_saved_query_id
            ON saved_query_share_tokens (saved_query_id);
        """;

    // Analytics pipelines (Phase 5 of the Data Stores plan). The DAG lives
    // in `pipelines.graph` (jsonb). pipeline_runs captures a snapshot of
    // the graph at enqueue time so a concurrent edit can't mutate a
    // queued run. pipeline_run_steps records per-node status/timings/row
    // counts as the orchestrator walks the topologically-sorted graph.
    // ExecuteSqlRawAsync runs the SQL through `String.Format`, which treats
    // `{`/`}` as format-token delimiters. JSON literals embedded as column
    // defaults have to double the braces or the EF format pass throws
    // FormatException before the statement ever reaches Postgres. Other
    // jsonb defaults in this file (search for `'{{}}'::jsonb`) follow the
    // same convention.
    private const string PipelinesSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS pipelines (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            graph JSONB NOT NULL DEFAULT '{{"nodes":[],"edges":[]}}'::jsonb,
            schedule_cron TEXT NULL,
            last_run_at_utc TIMESTAMPTZ NULL,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_pipelines_name
            ON pipelines (LOWER(name));

        CREATE INDEX IF NOT EXISTS ix_pipelines_owner_user_id
            ON pipelines (owner_user_id);

        CREATE TABLE IF NOT EXISTS pipeline_runs (
            id UUID PRIMARY KEY,
            pipeline_id UUID NOT NULL REFERENCES pipelines (id) ON DELETE CASCADE,
            status TEXT NOT NULL DEFAULT 'Queued',
            graph_snapshot JSONB NOT NULL DEFAULT '{{}}'::jsonb,
            queued_at_utc TIMESTAMPTZ NOT NULL,
            started_at_utc TIMESTAMPTZ NULL,
            completed_at_utc TIMESTAMPTZ NULL,
            error_message TEXT NULL,
            triggered_by UUID NOT NULL,
            trigger_kind TEXT NOT NULL DEFAULT 'manual'
        );

        CREATE INDEX IF NOT EXISTS ix_pipeline_runs_pipeline_id
            ON pipeline_runs (pipeline_id);

        CREATE INDEX IF NOT EXISTS ix_pipeline_runs_status
            ON pipeline_runs (status);

        CREATE INDEX IF NOT EXISTS ix_pipeline_runs_queued_at_utc
            ON pipeline_runs (queued_at_utc DESC);

        CREATE TABLE IF NOT EXISTS pipeline_run_steps (
            id UUID PRIMARY KEY,
            pipeline_run_id UUID NOT NULL REFERENCES pipeline_runs (id) ON DELETE CASCADE,
            node_key TEXT NOT NULL,
            node_kind TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'Queued',
            started_at_utc TIMESTAMPTZ NULL,
            completed_at_utc TIMESTAMPTZ NULL,
            row_count BIGINT NULL,
            error_message TEXT NULL,
            -- Per-step log buffer (audit fix archived-11). JSONB array of log
            -- entries (timestampUtc, level, message) the orchestrator
            -- accumulates during execution and writes on step
            -- completion. Default `[]` so existing rows + concurrent
            -- writers stay consistent.
            logs_json JSONB NOT NULL DEFAULT '[]'::jsonb
        );

        -- Idempotent migration for databases provisioned before fix archived-11.
        ALTER TABLE pipeline_run_steps
            ADD COLUMN IF NOT EXISTS logs_json JSONB NOT NULL DEFAULT '[]'::jsonb;

        CREATE INDEX IF NOT EXISTS ix_pipeline_run_steps_pipeline_run_id
            ON pipeline_run_steps (pipeline_run_id);
        """;

    // User-authored transformers / analyzers (Phase 6 of the Data Stores
    // plan). The code itself executes in `services/executor/` under V8 or
    // Pyodide isolates unless `is_unsafe` flips the runtime to host-side
    // CPython (which the `transformer:executeunsafe` permission gates).
    // Identity providers (#87). One table with a `kind` discriminator rather
    // than one per protocol: OIDC and SAML share display name, enabled state,
    // the encrypted secret and the audit columns, and differ in three or four
    // fields each. The login page needs the union of both, which two tables
    // would force every read path to reassemble.
    //
    // Deliberately NOT reusing `external_connections`, whose own comment
    // anticipates an "identity provider" kind. That table's secrets are
    // protected under AutoNate.ExternalConnections.v1, and #87 requires a
    // dedicated purpose so a rotation forced by one secret class does not
    // force re-entry of the other's.
    //
    // Nothing is seeded here. Project invariant 1: configuring nothing creates
    // nothing, and an install with no provider behaves exactly as it does today.
    // Makes the Identity Providers admin screen reachable (#87).
    //
    // A separate batch rather than an edit to the original template/menu seeds:
    // those are recorded in schema_versions and will not re-run, so editing
    // them would add the row on fresh installs only and leave every existing
    // one with an unreachable page. Written idempotently so it is safe on both.
    //
    // Note the doubled braces in the jsonb literal — inline batches go through
    // EF's string.Format pass, which collapses them to single. See the
    // add-schema-change skill.
    private const string IdentityProvidersMenuSeedSql =
        """
        INSERT INTO page_templates (id, key, name, description, is_enabled, created_at_utc, updated_at_utc)
        SELECT gen_random_uuid(), 'configIdentityProviders', 'Identity Providers (Site Config)',
               'Federated sign-in: OIDC and SAML providers.', TRUE, NOW(), NOW()
        WHERE NOT EXISTS (SELECT 1 FROM page_templates WHERE key = 'configIdentityProviders');

        DO $$
        DECLARE
            site_menu_id UUID;
            config_group_id UUID;
        BEGIN
            -- Hang it off the same Site Config group External Connections is in,
            -- found by that template key rather than by display name so a
            -- renamed menu item does not orphan this.
            SELECT mi.menu_id, mi.parent_id INTO site_menu_id, config_group_id
            FROM menu_items mi
            WHERE mi.config->>'templateKey' = 'configExternalConnections'
            LIMIT 1;

            IF site_menu_id IS NULL THEN
                -- No Site Config menu on this install; nothing to attach to.
                RETURN;
            END IF;

            IF EXISTS (
                SELECT 1 FROM menu_items
                WHERE config->>'templateKey' = 'configIdentityProviders'
            ) THEN
                RETURN;
            END IF;

            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system, created_at_utc, updated_at_utc)
            VALUES (
                gen_random_uuid(), site_menu_id, config_group_id, 6,
                'Identity Providers', 'fa fa-id-card', 'template',
                -- Both keys, matching every other template menu item. The
                -- migration that normalised the existing ones builds
                -- jsonb_build_object('templateKey', ..., 'path', ...), and a
                -- template item carrying only the key is a shape nothing else
                -- in the table has.
                '{{"templateKey":"configIdentityProviders","path":"/admin/config/identity-providers"}}'::jsonb,
                TRUE, TRUE, NOW(), NOW());
        END $$;
        """;

    private const string IdentityProviderGroupMappingsSchemaSql =
        """
        -- #92. An administrator says which IdP claim value corresponds to which
        -- Auton8 group. Nothing here invents a new authorization concept: groups
        -- already hold role assignments, so the group→role path stays the single
        -- place authorization is reasoned about.
        --
        -- An unmapped IdP group grants nothing. Mapping is the whole gate: a
        -- group created in the identity provider has no effect until someone
        -- here decides it should.
        CREATE TABLE IF NOT EXISTS identity_provider_group_mappings (
            id UUID PRIMARY KEY,
            provider_id UUID NOT NULL REFERENCES identity_providers (id) ON DELETE CASCADE,
            claim_type TEXT NOT NULL,
            claim_value TEXT NOT NULL,
            group_id UUID NOT NULL REFERENCES groups (id) ON DELETE CASCADE,
            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );

        -- Cascades from both parents on purpose. A mapping that outlived its
        -- group would point at nothing, and reconciliation would have to decide
        -- what a dangling grant means — a question better not to have.

        -- The same claim may grant several groups, and several claims may grant
        -- one group; what must not exist twice is the same edge.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_idp_group_mappings_edge
            ON identity_provider_group_mappings (provider_id, claim_type, claim_value, group_id);

        -- Reconciliation loads every mapping for one provider on each sign-in.
        CREATE INDEX IF NOT EXISTS ix_idp_group_mappings_provider
            ON identity_provider_group_mappings (provider_id);

        -- Provenance on the membership itself. Without it reconciliation cannot
        -- tell what it is allowed to remove, and the first claim to disappear
        -- would take an administrator's manual grant with it.
        --
        -- The default is 'manual', which is not a convenience — it is true.
        -- Every row in this table before this migration was put there by a
        -- person, and none of them may be revoked by a claim going missing.
        ALTER TABLE group_members
            ADD COLUMN IF NOT EXISTS source TEXT NOT NULL DEFAULT 'manual';

        -- Which provider owns an idp-derived row. Two identity providers
        -- configured against one Auton8 must not be able to revoke each other's
        -- grants, and without this column a sign-in through either would
        -- reconcile away the other's memberships.
        ALTER TABLE group_members
            ADD COLUMN IF NOT EXISTS source_provider_id UUID NULL;

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'ck_group_members_source'
            ) THEN
                ALTER TABLE group_members
                    ADD CONSTRAINT ck_group_members_source
                    CHECK (
                        (source = 'manual' AND source_provider_id IS NULL)
                        OR (source = 'idp' AND source_provider_id IS NOT NULL)
                    );
            END IF;
        END $$;

        -- Reconciliation asks "which of this user's memberships does this
        -- provider own?" on every federated sign-in.
        CREATE INDEX IF NOT EXISTS ix_group_members_source
            ON group_members (user_id, source, source_provider_id);
        """;

    private const string IdentityProvidersSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS identity_providers (
            id UUID PRIMARY KEY,
            kind TEXT NOT NULL,
            display_name TEXT NOT NULL,
            slug TEXT NOT NULL,
            is_enabled BOOLEAN NOT NULL DEFAULT FALSE,

            -- OIDC: the authority (issuer or discovery base) and client id.
            -- The client secret lives in secret_ciphertext below.
            oidc_authority TEXT NULL,
            oidc_client_id TEXT NULL,
            oidc_scopes TEXT NULL,

            -- SAML: the IdP entity id plus either a metadata URL to fetch or
            -- pasted metadata, and the signing certificate used to validate
            -- assertions.
            saml_entity_id TEXT NULL,
            saml_metadata_url TEXT NULL,
            saml_metadata_xml TEXT NULL,
            saml_signing_certificate TEXT NULL,

            -- Shared secret storage, same shape as external_connections:
            -- ciphertext is never returned by any read endpoint, and the
            -- fingerprint is the redacted value safe to show in admin UI and
            -- audit events.
            secret_ciphertext BYTEA NULL,
            secret_fingerprint TEXT NULL,

            created_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            updated_by UUID NOT NULL
        );

        -- The slug is what appears in a callback path and identifies the
        -- provider on the login page, so it has to be unique and stable.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_providers_slug
            ON identity_providers (LOWER(slug));

        -- The login page asks "which providers are enabled?" on every render.
        CREATE INDEX IF NOT EXISTS ix_identity_providers_enabled
            ON identity_providers (is_enabled);
        """;

    private const string CodeTransformersSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS code_transformers (
            id UUID PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NULL,
            kind TEXT NOT NULL,
            language TEXT NOT NULL,
            code TEXT NOT NULL DEFAULT '',
            is_unsafe BOOLEAN NOT NULL DEFAULT FALSE,
            owner_user_id UUID NOT NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            updated_at_utc TIMESTAMPTZ NOT NULL,
            created_by UUID NOT NULL,
            updated_by UUID NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_code_transformers_name
            ON code_transformers (LOWER(name));

        CREATE INDEX IF NOT EXISTS ix_code_transformers_owner_user_id
            ON code_transformers (owner_user_id);

        CREATE INDEX IF NOT EXISTS ix_code_transformers_kind
            ON code_transformers (kind);
        """;

    // The schema ledger. Created before anything else so every batch below can
    // record itself.
    //
    // Deliberately separate from auth_seed_state: those keys gate one-shot DATA
    // migrations with their own semantics (a backfill that must never run
    // twice), while this records which SCHEMA batch has been applied. Merging
    // them would conflate "this data was migrated" with "this DDL ran".
    private const string SchemaVersionsSql =
        """
        CREATE TABLE IF NOT EXISTS schema_versions (
            step_name TEXT PRIMARY KEY,
            app_version TEXT NOT NULL,
            applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    // Serialises schema initialisation across hosts.
    //
    // The batches below are individually idempotent, which is not the same as
    // concurrency-safe: two sessions issuing `CREATE INDEX IF NOT EXISTS` or
    // `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` against the same relation
    // deadlock or fail on a duplicate object rather than one waiting for the
    // other. Two hosts starting together — a restart under a supervisor, a
    // rolling replacement, two developers sharing a database — is therefore a
    // race without this.
    //
    // The value is arbitrary but must never change: it is the identity two
    // hosts agree on. 0x4175746F_6E387631 spells "Auto" "n8v1" in ASCII, which
    // makes it recognisable in pg_locks when someone is working out what is
    // holding a database up.
    private const long SchemaInitLockKey = 0x4175746F6E387631L;

    // How long to wait for another host to finish before giving up. Long
    // enough for a full first-boot initialisation on a slow machine, short
    // enough that a stuck holder produces a diagnosable failure rather than a
    // process that never finishes starting.
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();

        var logger = scope.ServiceProvider
            .GetService<ILoggerFactory>()
            ?.CreateLogger("AutoNate.Web.Persistence.DatabaseSchemaInitializer");

        // The lock is taken on the DbContext's OWN connection, explicitly
        // opened for the duration.
        //
        // Session-level rather than pg_advisory_xact_lock, because the
        // transactional form means wrapping the ~90 DDL batches below in one
        // enclosing transaction — a much larger behaviour change than the race
        // it fixes. A session lock lives with the connection, so that
        // connection has to stay open until the work is done; explicitly
        // opening it here is what guarantees EF does not hand the batches a
        // different physical connection part-way through.
        //
        // It used to open a SECOND, dedicated connection. That cost one extra
        // long-held connection per concurrently-initialising database, and the
        // test suite — which builds a database per test class, in parallel —
        // exhausted PostgreSQL's default max_connections of 100:
        // "53300: sorry, too many clients already", 903 failures. Anything
        // running several instances against one server would have hit the same
        // wall, so this is not merely a test-harness concern.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var lockConnection = dbContext.Database.GetDbConnection();

        try
        {
            await AcquireSchemaLockAsync(lockConnection, logger, cancellationToken);

            try
            {
                await RunSchemaBatchesAsync(scope, dbContext, cancellationToken);
            }
            finally
            {
                // Release on the exception path too. Closing the connection
                // would release a session lock anyway, but doing it explicitly
                // means a failure here does not leave the next host waiting out
                // the full timeout for a lock nobody holds.
                await ReleaseSchemaLockAsync(lockConnection, logger);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task AcquireSchemaLockAsync(
        System.Data.Common.DbConnection connection, ILogger? logger, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + LockWaitTimeout;
        var announcedWait = false;

        while (true)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT pg_try_advisory_lock(@key);";
                var keyParam = command.CreateParameter();
                keyParam.ParameterName = "key";
                keyParam.Value = SchemaInitLockKey;
                command.Parameters.Add(keyParam);

                var acquired = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false;
                if (acquired)
                {
                    if (announcedWait)
                    {
                        logger?.LogInformation(
                            "Acquired the schema-initialisation lock; continuing startup.");
                    }

                    return;
                }
            }

            if (!announcedWait)
            {
                // Without this, a host blocked here looks hung. This line is
                // the difference between "the app is slow to start" and "the
                // app is waiting for another host to finish initialising".
                logger?.LogInformation(
                    "Another host holds the schema-initialisation lock ({LockKey}); waiting up to {Timeout} for it.",
                    SchemaInitLockKey, LockWaitTimeout);
                announcedWait = true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {LockWaitTimeout} waiting for the schema-initialisation advisory "
                    + $"lock ({SchemaInitLockKey}). Another host is initialising this database and has not "
                    + "finished, or a previous run left the lock held. Inspect pg_locks for an advisory "
                    + $"lock with objid {SchemaInitLockKey}.");
            }

            await Task.Delay(LockPollInterval, cancellationToken);
        }
    }

    private static async Task ReleaseSchemaLockAsync(
        System.Data.Common.DbConnection connection, ILogger? logger)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key);";
            var keyParam = command.CreateParameter();
            keyParam.ParameterName = "key";
            keyParam.Value = SchemaInitLockKey;
            command.Parameters.Add(keyParam);
            await command.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            // Closing the connection releases a session-level lock regardless,
            // so this is not fatal — but it should not be silent either.
            logger?.LogWarning(
                ex, "Failed to release the schema-initialisation lock explicitly; "
                    + "it will be released when the connection closes.");
        }
    }

    private static async Task RunSchemaBatchesAsync(
        AsyncServiceScope scope, AutoNateDbContext dbContext, CancellationToken cancellationToken)
    {
        // The ledger has to exist before anything can record itself in it.
        await dbContext.Database.ExecuteSqlRawAsync(SchemaVersionsSql, cancellationToken);

        var applied = await LoadAppliedStepsAsync(dbContext, cancellationToken);

        await GuardAgainstNewerSchemaAsync(dbContext, cancellationToken);

        // First, and before every other batch: they all assume these tables
        // exist. WorkflowVersioningSql immediately below opens with
        // `ALTER TABLE workflow_models`, which fails on a database where
        // nothing has created it.
        await ApplyStepAsync(
            dbContext, applied, "BaseSchemaSql", ReadBaseSchemaSql(), cancellationToken,
            bypassFormatting: true);

        await ApplyStepAsync(dbContext, applied, nameof(WorkflowVersioningSql), WorkflowVersioningSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(WorkflowDefaultVariablesSql), WorkflowDefaultVariablesSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(WorkflowExecutionErrorsSql), WorkflowExecutionErrorsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(WorkflowTaskCompletionsSql), WorkflowTaskCompletionsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordsSchemaSql), RecordsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordsDataSchemaSql), RecordsDataSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordsEdgesSchemaSql), RecordsEdgesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordsCommentsSchemaSql), RecordsCommentsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordWatchesSchemaSql), RecordWatchesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AuthorizationSchemaSql), AuthorizationSchemaSql, cancellationToken);

        // Before the seeds, not after them.
        //
        // DocumentsMenuItemSeedSql and ContentSampleProjectSeedSql both pick
        // the oldest local_users row as the actor they attribute their seeded
        // rows to, and both `RETURN` silently when there is none. That was
        // invisible while the init script seeded `admin`; with the seed gone,
        // running the bootstrap after them left a fresh install with no
        // Documents nav item and no sample project, and nothing failed to say
        // so. It has to run after AuthorizationSchemaSql (which creates
        // role_assignments, where the SuperAdmin grant lands) and before the
        // first seed that needs an actor.
        await EnsureBootstrapAdminAsync(scope.ServiceProvider, dbContext, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordEdgeBackfillSql), RecordEdgeBackfillSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordEdgeShadowBackfillSql), RecordEdgeShadowBackfillSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(EntityEdgeHotIndexesSql), EntityEdgeHotIndexesSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RolePermissionsToGrantsSql), RolePermissionsToGrantsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PageTemplatesSchemaSql), PageTemplatesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(MenusSchemaSql), MenusSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(IconMenuWrapSettingsSql), IconMenuWrapSettingsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigStatusAppearanceSql), SiteConfigStatusAppearanceSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigSiteInformationSql), SiteConfigSiteInformationSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PluginsSchemaSql), PluginsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PluginDataIsolationSql), PluginDataIsolationSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(MenuItemsPluginColumnSql), MenuItemsPluginColumnSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PageTemplatesPluginColumnsSql), PageTemplatesPluginColumnsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PageTemplatesSeedSql), PageTemplatesSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PageTemplatesThumbnailSeedSql), PageTemplatesThumbnailSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PluginsIconMenuRemovalSql), PluginsIconMenuRemovalSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(EmptyFeaturesMenuRemovalSql), EmptyFeaturesMenuRemovalSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PluginsSiteConfigMenuSql), PluginsSiteConfigMenuSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigSystemHealthSql), SiteConfigSystemHealthSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigSystemIssuesSql), SiteConfigSystemIssuesSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigFormsSql), SiteConfigFormsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigChatbotSettingsSql), SiteConfigChatbotSettingsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(FormsSchemaSql), FormsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(NotificationsSql), NotificationsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteSettingsSql), SiteSettingsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AuditOutboxSchemaSql), AuditOutboxSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AuditOutboxDeadLettersSchemaSql), AuditOutboxDeadLettersSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SystemIssuesSchemaSql), SystemIssuesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(LocalUserLockoutSql), LocalUserLockoutSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ExternalConnectionsSchemaSql), ExternalConnectionsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AgentConversationsSchemaSql), AgentConversationsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AgentMessageSummaryColumnsSql), AgentMessageSummaryColumnsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AgentModelCatalogSchemaSql), AgentModelCatalogSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AgentModelCatalogSeedSql), AgentModelCatalogSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(AgentModelDefaultAvailableColumnsSql), AgentModelDefaultAvailableColumnsSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SiteConfigChatbotModelsMenuSql), SiteConfigChatbotModelsMenuSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ContentHierarchySchemaSql), ContentHierarchySchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ContentLocatorSchemaSql), ContentLocatorSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ContentDocumentsSchemaSql), ContentDocumentsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(DocumentsMenuItemSeedSql), DocumentsMenuItemSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ContentNotePageIndexSql), ContentNotePageIndexSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(NotePreviewSvgSql), NotePreviewSvgSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PageFavoritesSchemaSql), PageFavoritesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(YjsDocumentsSchemaSql), YjsDocumentsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ContentSampleProjectSeedSql), ContentSampleProjectSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(DashboardsSchemaSql), DashboardsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SavedQueriesSchemaSql), SavedQueriesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(QueryMenuSeedSql), QueryMenuSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ProjectionFrameworkSchemaSql), ProjectionFrameworkSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(WorkflowCacheSchemaSql), WorkflowCacheSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(WorkflowEventLogSchemaSql), WorkflowEventLogSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(ProcessRetentionConfigSchemaSql), ProcessRetentionConfigSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(RecordActivityRollupSchemaSql), RecordActivityRollupSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(DataStoresSchemaSql), DataStoresSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(DatasetsSchemaSql), DatasetsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(SavedQueryShareTokensSchemaSql), SavedQueryShareTokensSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(PipelinesSchemaSql), PipelinesSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(CodeTransformersSchemaSql), CodeTransformersSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(IdentityProvidersSchemaSql), IdentityProvidersSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(IdentityProvidersMenuSeedSql), IdentityProvidersMenuSeedSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(IdentityProviderGroupMappingsSchemaSql), IdentityProviderGroupMappingsSchemaSql, cancellationToken);
        await ApplyStepAsync(dbContext, applied, nameof(DataMainMenuSeedSql), DataMainMenuSeedSql, cancellationToken);

        // Before the SuperAdmin backfill on purpose: on a first boot the
        // account created here is the one that needs to come up administrable.
        var authOptions = scope.ServiceProvider
            .GetService<IOptions<AuthorizationOptions>>()?.Value
            ?? new AuthorizationOptions();
        if (authOptions.AssignSuperAdminToAllExistingUsers)
        {
            await ApplyStepAsync(dbContext, applied, nameof(SuperAdminBackfillSql), SuperAdminBackfillSql, cancellationToken);
        }

        // Last: every table above now exists, so the credential tables can be
        // taken back off plg_readers (archived-62).
        await ApplyStepAsync(dbContext, applied, nameof(PluginReaderLockdownSql), PluginReaderLockdownSql, cancellationToken);
    }



    /// <summary>The base schema, read from the embedded resource.</summary>
    /// <remarks>
    /// The single copy in the repository. Both test fixtures read the same
    /// resource, so what they set up and what the application applies cannot
    /// diverge — which they could when this was a file on disk that one fixture
    /// read by path and another had build-copied into its output.
    /// </remarks>
    internal static string ReadBaseSchemaSql()
    {
        const string ResourceName = "AutoNate.Web.Persistence.Sql.BaseSchema.sql";

        var assembly = typeof(DatabaseSchemaInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found in {assembly.GetName().Name}. "
                + "The base schema is required to initialise a database; check the EmbeddedResource "
                + "item in AutoNate.Web.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The product version, used to stamp ledger rows.</summary>
    /// <remarks>
    /// Read from the assembly's informational version, which
    /// Directory.Build.props sets from a single &lt;Version&gt; element. Used
    /// only for reporting and for the newer-schema guard — the ledger keys on
    /// step name, not on version, so a version that fails to parse degrades to
    /// a cosmetic problem rather than a correctness one.
    /// </remarks>
    internal static string AppVersion { get; } =
        typeof(DatabaseSchemaInitializer).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(DatabaseSchemaInitializer).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static async Task<HashSet<string>> LoadAppliedStepsAsync(
        AutoNateDbContext dbContext, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT step_name FROM schema_versions;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// Applies one schema batch unless the ledger already records it.
    /// </summary>
    /// <remarks>
    /// On a database that predates the ledger, every step runs once and is
    /// recorded as it goes. That is deliberate and is NOT the same as writing
    /// the ledger rows without running anything: for an existing install we
    /// cannot know which batches were applied, and marking an un-applied step
    /// as applied would skip it permanently, leaving a silently half-migrated
    /// schema. The batches are idempotent, which is what makes running them
    /// once safe. The guarantee is therefore "no schema work after the first
    /// boot", not "no schema work on the boot that introduces the ledger".
    /// </remarks>
    private static async Task ApplyStepAsync(
        AutoNateDbContext dbContext,
        HashSet<string> applied,
        string stepName,
        string sql,
        CancellationToken cancellationToken,
        bool bypassFormatting = false)
    {
        // A batch that consults auth_seed_state carries its own re-run gate, and
        // that gate — not this ledger — owns when it may run again. Skipping it
        // here would make the ledger a second, wrong gate: clearing an
        // auth_seed_state marker to re-enable a data migration would silently
        // do nothing, because the ledger would still record the step as done.
        //
        // Found by RebrandMigrationTests, which rewinds an install by clearing
        // `rebrand_auton8_v1` and restarting, and expects the rename to run
        // again. Ledger-gating that step broke it.
        //
        // These batches are cheap to re-enter — each opens with a
        // NOT EXISTS check against auth_seed_state and returns immediately —
        // so running them every boot costs a query, not work.
        var ownsItsOwnGate = sql.Contains("auth_seed_state", StringComparison.Ordinal);

        if (applied.Contains(stepName) && !ownsItsOwnGate)
        {
            return;
        }

        // Two execution paths, and the difference is load-bearing.
        //
        // EF's ExecuteSqlRawAsync runs the SQL through string.Format first.
        // The inline batches in this file are written FOR that: there are 34
        // occurrences of `'{{}}'::jsonb`, doubled so the format pass collapses
        // them to `{}`. Executing those without the format pass sends `{{}}`
        // to Postgres and fails with `22P02: invalid input syntax for type
        // json`.
        //
        // The base schema is the opposite. It is an external .sql file that
        // must stay valid SQL on its own — it is read by tooling and by
        // people, not just by C# — so it contains single braces, and putting
        // it through string.Format fails with "Failure to parse near offset
        // 4891. Expected an ASCII digit."
        //
        // Hence the flag. Both directions were observed as failures before
        // this comment existed; neither is theoretical.
        if (bypassFormatting)
        {
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await dbContext.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var batch = connection.CreateCommand();
            batch.CommandText = sql;
            batch.CommandTimeout = 0;
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO schema_versions (step_name, app_version, applied_at_utc) "
            + "VALUES ({0}, {1}, NOW()) ON CONFLICT (step_name) DO NOTHING;",
            [stepName, AppVersion],
            cancellationToken);

        applied.Add(stepName);
    }


    /// <summary>
    /// Refuses to start against a database initialised by a newer build.
    /// </summary>
    /// <remarks>
    /// Rolling an application back is a legitimate operational action; running
    /// it against a schema it does not understand is not. Without this, an
    /// older build starts happily and then fails in scattered, confusing ways
    /// as it meets columns and tables it has no model for. Failing at startup
    /// with both versions named is the difference between a five-minute
    /// rollback and an afternoon.
    ///
    /// v1.0 makes no upgrade promise (see #59), but the ledger and this guard
    /// ship anyway: clean upgrade paths after 1.0 are only possible if 1.0
    /// recorded what it applied.
    /// </remarks>
    private static async Task GuardAgainstNewerSchemaAsync(
        AutoNateDbContext dbContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT step_name, app_version
            FROM schema_versions
            ORDER BY applied_at_utc DESC;
            """;

        string? newestStep = null;
        Version? newestVersion = null;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Version.TryParse(reader.GetString(1), out var recorded))
                {
                    // An unparseable version is not evidence of anything; the
                    // ledger's job is step names, and versions are advisory.
                    continue;
                }

                if (newestVersion is null || recorded > newestVersion)
                {
                    newestVersion = recorded;
                    newestStep = reader.GetString(0);
                }
            }
        }

        if (newestVersion is null || !Version.TryParse(AppVersion, out var running))
        {
            return;
        }

        if (newestVersion > running)
        {
            throw new InvalidOperationException(
                $"This database was initialised by Auton8 {newestVersion}, which is newer than the "
                + $"running build ({running}). Step '{newestStep}' was applied by that version. "
                + "Refusing to start rather than run against a schema this build does not understand. "
                + $"Deploy {newestVersion} or later, or restore a database matching this build.");
        }
    }

    // Creates the first administrator on an otherwise empty install.
    //
    // Runs only while `local_users` has no rows, so it cannot touch an
    // existing deployment, and only when both a username and a password are
    // configured. An operator who configures neither gets a loud message
    // naming the two settings rather than a default account — the whole point
    // of replacing the committed `admin`/`admin` seed is that no credential
    // ships in the repository.
    private static async Task EnsureBootstrapAdminAsync(
        IServiceProvider services,
        AutoNateDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("BootstrapAdmin");

        var anyUser = await dbContext.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM local_users LIMIT 1")
            .ToArrayAsync(cancellationToken);
        if (anyUser.Length > 0) return;

        var options = services.GetService<IOptions<BootstrapAdminOptions>>()?.Value
            ?? new BootstrapAdminOptions();

        if (!options.IsConfigured)
        {
            logger?.LogWarning(
                "No users exist and no bootstrap administrator is configured, so nobody can sign in. "
                + "Set {Section}__AdminUsername and {Section}__AdminPassword and restart to create the "
                + "first administrator.",
                BootstrapAdminOptions.SectionName,
                BootstrapAdminOptions.SectionName);
            return;
        }

        var (hash, salt) = Services.Auth.PasswordHasher.HashPassword(options.AdminPassword!);
        var userId = options.AdminUserId ?? Guid.NewGuid();
        var email = string.IsNullOrWhiteSpace(options.AdminEmail)
            ? $"{options.AdminUsername}@localhost"
            : options.AdminEmail;

        // ON CONFLICT DO NOTHING rather than a bare INSERT: two instances
        // starting against the same empty database would otherwise race the
        // emptiness check, and the loser would fail startup on the username
        // unique index. Same shape as the CREATE DATABASE / CREATE ROLE races.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO local_users (
                username, password_hash, password_salt, email,
                first_name, last_name, user_id, created_date, last_login_date, idp_key)
            VALUES (
                {options.AdminUsername}, {hash}, {salt}, {email},
                'Admin', 'User', {userId}, NOW(), NULL, 'local-admin')
            ON CONFLICT (username) DO NOTHING;
            """, cancellationToken);

        if (!options.GrantSuperAdmin)
        {
            logger?.LogInformation(
                "Created the bootstrap administrator '{Username}' without SuperAdmin, as configured.",
                options.AdminUsername);
            return;
        }

        // Grant SuperAdmin to this account specifically.
        //
        // Until now the only thing that made the first admin privileged was
        // Authorization:AssignSuperAdminToAllExistingUsers, whose backfill
        // grants SuperAdmin to *every* row in local_users the first time it
        // runs. That is right for a greenfield install with one user and wrong
        // for an existing deployment upgrading into this version, where it
        // promotes the entire user table at once. Granting the account we just
        // created means the flag is no longer load-bearing and ships false.
        //
        // NOT EXISTS mirrors the backfill's own guard, so the two cannot
        // produce a duplicate assignment if both run.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO role_assignments (
                id, role_id, principal_kind, principal_id,
                scope_string, scope_ast, created_at_utc, created_by)
            SELECT
                gen_random_uuid(),
                '00000000-0000-0000-0000-000000000001'::uuid,
                'user',
                {userId}::text,
                NULL, NULL, NOW(),
                '00000000-0000-0000-0000-000000000000'::uuid
            WHERE NOT EXISTS (
                SELECT 1 FROM role_assignments r
                WHERE r.role_id = '00000000-0000-0000-0000-000000000001'::uuid
                  AND r.principal_kind = 'user'
                  AND r.principal_id = {userId}::text
            );
            """, cancellationToken);

        logger?.LogInformation(
            "Created the bootstrap administrator '{Username}' and granted it SuperAdmin. "
            + "Change this password after first sign-in.",
            options.AdminUsername);
    }

}
