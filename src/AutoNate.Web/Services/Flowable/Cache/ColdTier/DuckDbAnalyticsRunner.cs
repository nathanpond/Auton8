using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using DuckDB.NET.Data;

namespace AutoNate.Web.Services.Flowable.Cache.ColdTier;

// Per-request DuckDB instance that unifies hot (workflow_event_log_cache in
// Postgres) and cold (Parquet files on disk) into a single virtual view.
// The caller provides hot rows it already loaded from EF (with whatever auth
// filtering it needed); the runner stages them in DuckDB and UNIONs them
// with read_parquet over the cold glob.
//
// Connection lifetime is the duration of one runner instance. Tests create a
// runner per assertion; production builds one per analytical query. The
// in-memory backing makes setup/teardown ~10ms and avoids any shared-state
// coordination across queries.
public sealed class DuckDbAnalyticsRunner : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly ColdTierLayout _layout;
    private bool _hotLoaded;
    private bool _coldRegistered;

    public DuckDbAnalyticsRunner(ColdTierLayout layout)
    {
        _layout = layout;
        _connection = new DuckDBConnection("Data Source=:memory:");
        _connection.Open();
    }

    // Returns the SQL fragment that selects from the combined virtual view.
    // Callers wrap this in their own SELECT … GROUP BY etc.
    public const string CombinedViewSql = """
        SELECT * FROM hot_events
        UNION ALL
        SELECT * FROM cold_events
        """;

    public async Task LoadHotEventsAsync(IReadOnlyList<WorkflowEventLogCache> rows, CancellationToken cancellationToken)
    {
        await EnsureHotSchemaAsync(cancellationToken);
        if (rows.Count == 0) return;

        using var appender = _connection.CreateAppender("hot_events");
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            appender.CreateRow()
                .AppendValue(row.EventId)
                .AppendValue(row.FlowableInstanceId)
                .AppendValue(row.ProcessDefinitionKey)
                .AppendValue(DateTime.SpecifyKind(row.EventTime, DateTimeKind.Unspecified))
                .AppendValue(row.EventType)
                .AppendValue(row.ActivityId)
                .AppendValue(row.ActivityName)
                .AppendValue(row.ActivityType)
                .AppendValue(row.TaskId)
                .AppendValue(row.VariableName)
                .AppendValue(row.Actor)
                .AppendValue(row.DurationMs)
                .EndRow();
        }
    }

    // Registers a view over cold Parquet files (or an empty view when no
    // files exist yet — so SELECT against cold_events always parses).
    public async Task RegisterColdAsync(CancellationToken cancellationToken)
    {
        if (_coldRegistered) return;
        _coldRegistered = true;

        var glob = _layout.EventLogReadParquetGlob();
        await using var cmd = _connection.CreateCommand();
        if (glob is null)
        {
            cmd.CommandText = """
                CREATE VIEW cold_events AS
                SELECT
                    CAST(NULL AS VARCHAR) AS event_id,
                    CAST(NULL AS VARCHAR) AS flowable_instance_id,
                    CAST(NULL AS VARCHAR) AS process_definition_key,
                    CAST(NULL AS TIMESTAMP) AS event_time,
                    CAST(NULL AS VARCHAR) AS event_type,
                    CAST(NULL AS VARCHAR) AS activity_id,
                    CAST(NULL AS VARCHAR) AS activity_name,
                    CAST(NULL AS VARCHAR) AS activity_type,
                    CAST(NULL AS VARCHAR) AS task_id,
                    CAST(NULL AS VARCHAR) AS variable_name,
                    CAST(NULL AS VARCHAR) AS actor,
                    CAST(NULL AS BIGINT) AS duration_ms
                WHERE FALSE
                """;
        }
        else
        {
            // SELECT explicitly so a Parquet file with extra/missing columns
            // (e.g. an older write that included payload/projection_version)
            // still satisfies the union schema. read_parquet returns nulls
            // for columns absent from the file.
            cmd.CommandText = $"""
                CREATE VIEW cold_events AS
                SELECT
                    event_id, flowable_instance_id, process_definition_key,
                    event_time, event_type, activity_id, activity_name,
                    activity_type, task_id, variable_name, actor, duration_ms
                FROM read_parquet('{glob.Replace("'", "''")}', union_by_name = true)
                """;
        }
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Executes an arbitrary SELECT over the combined view and materializes
    // every row as a dictionary keyed by column name. Parameters are bound
    // positionally — DuckDB.NET expects ? placeholders in the SQL text.
    public async Task<List<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyList<(string Name, object? Value)>? parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (parameters is not null)
        {
            foreach (var (_, value) in parameters)
            {
                var p = cmd.CreateParameter();
                p.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var value = await reader.IsDBNullAsync(i, cancellationToken)
                    ? null
                    : reader.GetValue(i);
                if (value is DateTime dt)
                {
                    value = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
                row[name] = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private async Task EnsureHotSchemaAsync(CancellationToken cancellationToken)
    {
        if (_hotLoaded) return;
        _hotLoaded = true;

        await using var ddl = _connection.CreateCommand();
        ddl.CommandText = """
            CREATE TABLE hot_events (
                event_id              VARCHAR,
                flowable_instance_id  VARCHAR,
                process_definition_key VARCHAR,
                event_time            TIMESTAMP,
                event_type            VARCHAR,
                activity_id           VARCHAR,
                activity_name         VARCHAR,
                activity_type         VARCHAR,
                task_id               VARCHAR,
                variable_name         VARCHAR,
                actor                 VARCHAR,
                duration_ms           BIGINT
            )
            """;
        await ddl.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
