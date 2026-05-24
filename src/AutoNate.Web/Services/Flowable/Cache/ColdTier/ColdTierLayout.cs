using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache.ColdTier;

// Single source of truth for where cold-tier Parquet files live on disk.
// Both the archiver (write path) and the analytics runner (read path) go
// through this, so changing the layout in one place can't drift them apart.
public sealed class ColdTierLayout
{
    private readonly ColdTierOptions _options;

    public ColdTierLayout(IOptions<ColdTierOptions> options)
    {
        _options = options.Value;
    }

    public string EventLogDirectory => Path.Combine(_options.Root, "workflow_event_log");

    // One file per (year, month). Keeps each file small enough to scan
    // quickly on a single core and lets the retention janitor drop whole
    // months by deleting the file rather than rewriting it.
    public string EventLogFileFor(DateTime monthUtc)
    {
        var dir = EventLogDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{monthUtc:yyyy}-{monthUtc:MM}.parquet");
    }

    // Glob pattern fed to DuckDB's read_parquet. Returns null when no files
    // exist yet — callers should treat that as "no cold data" rather than
    // asking DuckDB to read a non-existent glob.
    public string? EventLogReadParquetGlob()
    {
        var dir = EventLogDirectory;
        if (!Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*.parquet").Any())
        {
            return null;
        }
        return Path.Combine(dir, "*.parquet");
    }
}
