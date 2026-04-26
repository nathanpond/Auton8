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

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowVersioningSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsDataSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsEdgesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsCommentsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(AuthorizationSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordEdgeBackfillSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordEdgeShadowBackfillSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(EntityEdgeHotIndexesSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RolePermissionsToGrantsSql, cancellationToken);

        var authOptions = scope.ServiceProvider
            .GetService<IOptions<AuthorizationOptions>>()?.Value
            ?? new AuthorizationOptions();
        if (authOptions.AssignSuperAdminToAllExistingUsers)
        {
            await dbContext.Database.ExecuteSqlRawAsync(SuperAdminBackfillSql, cancellationToken);
        }
    }
}
