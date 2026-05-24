using AutoNate.Web.Persistence;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache.ColdTier;

// Migrates aged rows from workflow_event_log_cache into per-month Parquet
// files. Runs on ArchiveSweepInterval. Each sweep iterates month buckets
// containing rows past the cutoff, streams them through an in-memory DuckDB
// staging table, writes Parquet (zstd-compressed), then deletes the rows
// from Postgres.
//
// Crash safety: Parquet writes succeed-or-don't (DuckDB COPY is atomic per
// file). If a crash happens between the file write and the PG delete, the
// next sweep picks up the same rows and writes a second Parquet file with a
// distinct timestamp suffix. The analytics reader dedupes by event_id, so
// the duplication is invisible to consumers.
public sealed class ColdTierArchiverService : BackgroundService
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly ColdTierLayout _layout;
    private readonly ColdTierOptions _options;
    private readonly ILogger<ColdTierArchiverService> _logger;

    public ColdTierArchiverService(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        ColdTierLayout layout,
        IOptions<ColdTierOptions> options,
        ILogger<ColdTierArchiverService> logger)
    {
        _dbFactory = dbFactory;
        _layout = layout;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cold-tier archiver disabled via FlowableCache:ColdTier:Enabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cold-tier archiver sweep failed; retrying after interval.");
            }

            try { await Task.Delay(_options.ArchiveSweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Public so tests can drive a single sweep without the loop. Returns the
    // total rows archived in this pass (across every month bucket touched).
    public async Task<ArchiveReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.ArchiveAfterDays);
        var floor = DateTime.UtcNow - _options.MinimumRowAge;
        if (cutoff > floor)
        {
            // MinimumRowAge wins — never archive rows newer than the floor
            // even if ArchiveAfterDays would otherwise allow it.
            cutoff = floor;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Walk distinct months that still have rows past the cutoff. Inside
        // each month we cap at MaxRowsPerArchivePass so a one-shot catch-up
        // doesn't lock the table for minutes; remaining rows roll over to
        // the next sweep.
        var months = await db.Database
            .SqlQueryRaw<DateTime>("""
                SELECT DISTINCT date_trunc('month', event_time)::timestamp AS "Value"
                FROM workflow_event_log_cache
                WHERE event_time < {0}
                ORDER BY 1 ASC
                """, cutoff)
            .ToListAsync(cancellationToken);

        var report = new ArchiveReport();
        foreach (var rawMonthStart in months)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The DISTINCT query returns the month as `timestamp` (without
            // time zone) and Npgsql surfaces it with Kind=Unspecified.
            // Stamp it back to UTC so subsequent EF queries against the
            // `timestamptz` column accept the parameter.
            var monthStart = DateTime.SpecifyKind(rawMonthStart, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            var monthCutoff = cutoff < monthEnd ? cutoff : monthEnd;
            var archivedInMonth = await ArchiveMonthAsync(db, monthStart, monthCutoff, cancellationToken);
            report.RowsArchived += archivedInMonth;
            if (archivedInMonth > 0)
            {
                report.MonthsTouched++;
            }
        }

        if (report.RowsArchived > 0)
        {
            _logger.LogInformation(
                "Cold-tier archiver wrote {Rows} rows across {Months} month(s) into {Dir}.",
                report.RowsArchived, report.MonthsTouched, _layout.EventLogDirectory);
        }

        return report;
    }

    private async Task<int> ArchiveMonthAsync(
        AutoNateDbContext db,
        DateTime monthStart,
        DateTime monthCutoff,
        CancellationToken cancellationToken)
    {
        // Source rows for this month. Streamed via EF (AsNoTracking) so a
        // huge month doesn't materialize the whole set into memory before
        // DuckDB sees it.
        var rows = db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => e.EventTime >= monthStart && e.EventTime < monthCutoff)
            .OrderBy(e => e.EventTime)
            .Take(_options.MaxRowsPerArchivePass)
            .AsAsyncEnumerable();

        var filePath = $"{Path.GetFileNameWithoutExtension(_layout.EventLogFileFor(monthStart))}." +
                       $"{DateTime.UtcNow:yyyyMMddHHmmssfff}.parquet";
        filePath = Path.Combine(_layout.EventLogDirectory, filePath);

        var (writtenCount, archivedIds) = await WriteParquetAsync(rows, filePath, cancellationToken);
        if (writtenCount == 0)
        {
            return 0;
        }

        // Delete by exact id list so we never drop a row that wasn't
        // actually written to the file. Chunk the IN clause to keep the
        // parameter count manageable.
        const int chunkSize = 1000;
        for (var i = 0; i < archivedIds.Count; i += chunkSize)
        {
            var chunk = archivedIds.GetRange(i, Math.Min(chunkSize, archivedIds.Count - i));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_event_log_cache WHERE event_id = ANY({chunk.ToArray()})",
                cancellationToken);
        }

        return writtenCount;
    }

    private static async Task<(int Count, List<string> ArchivedIds)> WriteParquetAsync(
        IAsyncEnumerable<Persistence.Scaffolded.WorkflowEventLogCache> rows,
        string filePath,
        CancellationToken cancellationToken)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);

        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = """
                CREATE TABLE staging (
                    event_id              VARCHAR NOT NULL,
                    flowable_instance_id  VARCHAR NOT NULL,
                    process_definition_key VARCHAR NOT NULL,
                    event_time            TIMESTAMP NOT NULL,
                    event_type            VARCHAR NOT NULL,
                    activity_id           VARCHAR,
                    activity_name         VARCHAR,
                    activity_type         VARCHAR,
                    task_id               VARCHAR,
                    variable_name         VARCHAR,
                    actor                 VARCHAR,
                    duration_ms           BIGINT,
                    payload               VARCHAR,
                    projection_version    INTEGER NOT NULL,
                    last_sync_at          TIMESTAMP NOT NULL
                )
                """;
            await ddl.ExecuteNonQueryAsync(cancellationToken);
        }

        var archivedIds = new List<string>();
        var count = 0;
        using (var appender = connection.CreateAppender("staging"))
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                var apRow = appender.CreateRow();
                apRow.AppendValue(row.EventId)
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
                     .AppendValue(row.PayloadJson)
                     .AppendValue(row.ProjectionVersion)
                     .AppendValue(DateTime.SpecifyKind(row.LastSyncAtUtc, DateTimeKind.Unspecified))
                     .EndRow();

                archivedIds.Add(row.EventId);
                count++;
            }
        }

        if (count == 0)
        {
            return (0, archivedIds);
        }

        await using (var copy = connection.CreateCommand())
        {
            // CODEC zstd is a defensible default — ~2x smaller than snappy
            // at modest CPU cost. For analytical scans we read a whole file
            // anyway, so block-level compression doesn't add latency.
            copy.CommandText =
                $"COPY staging TO '{filePath.Replace("'", "''")}' (FORMAT 'parquet', CODEC 'zstd')";
            await copy.ExecuteNonQueryAsync(cancellationToken);
        }

        return (count, archivedIds);
    }
}

public sealed class ArchiveReport
{
    public int RowsArchived { get; set; }
    public int MonthsTouched { get; set; }
}
