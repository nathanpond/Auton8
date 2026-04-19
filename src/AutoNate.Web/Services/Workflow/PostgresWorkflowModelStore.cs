using AutoNate.Web.Models;
using Npgsql;
using NpgsqlTypes;

namespace AutoNate.Web.Services.Workflow;

public sealed class PostgresWorkflowModelStore(IConfiguration configuration) : IWorkflowModelStore
{
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is required to persist workflow models.");

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _isInitialized;

    public async Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                name,
                process_key,
                bpmn_xml,
                active_process_instance_id,
                created_at_utc,
                updated_at_utc,
                last_deployment_id,
                last_process_definition_id,
                last_process_definition_key,
                last_process_definition_version,
                last_deployed_at_utc
            from workflow_models
            order by updated_at_utc desc, name asc;
            """,
            connection);

        var models = new List<WorkflowModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(MapWorkflowModel(reader));
        }

        return models;
    }

    public async Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                name,
                process_key,
                bpmn_xml,
                active_process_instance_id,
                created_at_utc,
                updated_at_utc,
                last_deployment_id,
                last_process_definition_id,
                last_process_definition_key,
                last_process_definition_version,
                last_deployed_at_utc
            from workflow_models
            where id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", workflowModelId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapWorkflowModel(reader) : null;
    }

    public async Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                name,
                process_key,
                bpmn_xml,
                active_process_instance_id,
                created_at_utc,
                updated_at_utc,
                last_deployment_id,
                last_process_definition_id,
                last_process_definition_key,
                last_process_definition_version,
                last_deployed_at_utc
            from workflow_models
            order by updated_at_utc desc
            limit 1;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapWorkflowModel(reader) : null;
    }

    public async Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedModel = model with
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            Name = WorkflowBpmnXml.NormalizeWorkflowName(model.Name),
            ProcessKey = WorkflowBpmnXml.NormalizeProcessKey(model.ProcessKey),
            CreatedAtUtc = model.CreatedAtUtc == default ? now : model.CreatedAtUtc,
            UpdatedAtUtc = now
        };

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into workflow_models (
                id,
                name,
                process_key,
                bpmn_xml,
                active_process_instance_id,
                created_at_utc,
                updated_at_utc,
                last_deployment_id,
                last_process_definition_id,
                last_process_definition_key,
                last_process_definition_version,
                last_deployed_at_utc
            )
            values (
                @id,
                @name,
                @process_key,
                @bpmn_xml,
                @active_process_instance_id,
                @created_at_utc,
                @updated_at_utc,
                @last_deployment_id,
                @last_process_definition_id,
                @last_process_definition_key,
                @last_process_definition_version,
                @last_deployed_at_utc
            )
            on conflict (id) do update
            set
                name = excluded.name,
                process_key = excluded.process_key,
                bpmn_xml = excluded.bpmn_xml,
                active_process_instance_id = excluded.active_process_instance_id,
                updated_at_utc = excluded.updated_at_utc,
                last_deployment_id = excluded.last_deployment_id,
                last_process_definition_id = excluded.last_process_definition_id,
                last_process_definition_key = excluded.last_process_definition_key,
                last_process_definition_version = excluded.last_process_definition_version,
                last_deployed_at_utc = excluded.last_deployed_at_utc;
            """,
            connection);

        command.Parameters.AddWithValue("id", normalizedModel.Id);
        command.Parameters.AddWithValue("name", normalizedModel.Name);
        command.Parameters.AddWithValue("process_key", normalizedModel.ProcessKey);
        command.Parameters.AddWithValue("bpmn_xml", normalizedModel.BpmnXml);
        command.Parameters.Add(new NpgsqlParameter("active_process_instance_id", NpgsqlDbType.Text)
        {
            Value = (object?)normalizedModel.ActiveProcessInstanceId ?? DBNull.Value
        });
        command.Parameters.AddWithValue("created_at_utc", normalizedModel.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", normalizedModel.UpdatedAtUtc);
        command.Parameters.Add(new NpgsqlParameter("last_deployment_id", NpgsqlDbType.Text)
        {
            Value = (object?)normalizedModel.LastDeployment?.DeploymentId ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("last_process_definition_id", NpgsqlDbType.Text)
        {
            Value = (object?)normalizedModel.LastDeployment?.ProcessDefinitionId ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("last_process_definition_key", NpgsqlDbType.Text)
        {
            Value = (object?)normalizedModel.LastDeployment?.ProcessDefinitionKey ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("last_process_definition_version", NpgsqlDbType.Integer)
        {
            Value = (object?)normalizedModel.LastDeployment?.ProcessDefinitionVersion ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("last_deployed_at_utc", NpgsqlDbType.TimestampTz)
        {
            Value = (object?)normalizedModel.LastDeployment?.DeployedAtUtc ?? DBNull.Value
        });
        await command.ExecuteNonQueryAsync(cancellationToken);

        return normalizedModel;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                create table if not exists workflow_models (
                    id uuid primary key,
                    name text not null,
                    process_key text not null unique,
                    bpmn_xml text not null,
                    active_process_instance_id text null,
                    created_at_utc timestamptz not null,
                    updated_at_utc timestamptz not null,
                    last_deployment_id text null,
                    last_process_definition_id text null,
                    last_process_definition_key text null,
                    last_process_definition_version integer null,
                    last_deployed_at_utc timestamptz null
                );

                create index if not exists ix_workflow_models_updated_at_utc
                    on workflow_models (updated_at_utc desc);
                """,
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static WorkflowModel MapWorkflowModel(NpgsqlDataReader reader)
    {
        var lastDeployment = reader.IsDBNull(7)
            ? null
            : new WorkflowDeploymentInfo
            {
                DeploymentId = reader.GetString(7),
                ProcessDefinitionId = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                ProcessDefinitionKey = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                ProcessDefinitionVersion = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                DeployedAtUtc = reader.IsDBNull(11) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(11)
            };

        return new WorkflowModel
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            ProcessKey = reader.GetString(2),
            BpmnXml = reader.GetString(3),
            ActiveProcessInstanceId = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
            UpdatedAtUtc = reader.GetFieldValue<DateTimeOffset>(6),
            LastDeployment = lastDeployment
        };
    }
}
