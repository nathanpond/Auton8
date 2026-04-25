using Microsoft.EntityFrameworkCore;

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

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowVersioningSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsDataSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsEdgesSchemaSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(RecordsCommentsSchemaSql, cancellationToken);
    }
}
