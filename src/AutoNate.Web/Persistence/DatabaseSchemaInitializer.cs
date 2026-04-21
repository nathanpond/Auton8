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
            wm.id,
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
        WHERE wm.last_process_definition_version IS NOT NULL
          AND wm.last_deployment_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1
              FROM workflow_model_versions version
              WHERE version.workflow_model_id = wm.id
                AND version.version_number = wm.last_process_definition_version
          );
        """;

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(WorkflowVersioningSql, cancellationToken);
    }
}
