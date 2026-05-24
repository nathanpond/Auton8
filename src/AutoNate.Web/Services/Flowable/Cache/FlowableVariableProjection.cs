using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Per-instance variable snapshot → workflow_variable_cache rows. Each Apply
// batch is one instance's worth of variables; the projection deletes any
// existing rows for that instance and inserts the fresh set in a single
// transaction so observers never see a partial state.
public sealed class FlowableVariableProjection : IProjection<FlowableInstanceVariables>
{
    private readonly FlowableCacheOptions _options;

    public FlowableVariableProjection(IOptions<FlowableCacheOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "flowable.workflow_variable_cache";

    public int Version => _options.CurrentProjectionVersion;

    public Type SourceType => typeof(FlowableInstanceVariables);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<FlowableInstanceVariables>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        // Collapse to latest snapshot per instance — repeated emissions from
        // overlapping feeds should not multiply DELETE/INSERT work.
        var latestByInstance = new Dictionary<string, ChangeEvent<FlowableInstanceVariables>>(StringComparer.Ordinal);
        var deletes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in batch)
        {
            if (change.Op == ChangeOp.Delete)
            {
                latestByInstance.Remove(change.SourceId);
                deletes.Add(change.SourceId);
            }
            else
            {
                deletes.Remove(change.SourceId);
                latestByInstance[change.SourceId] = change;
            }
        }

        var now = DateTime.UtcNow;
        foreach (var (instanceId, change) in latestByInstance)
        {
            var snapshot = change.Source!;
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_variable_cache WHERE flowable_instance_id = {instanceId}",
                cancellationToken);

            foreach (var (name, element) in snapshot.Variables)
            {
                var (type, vText, vLong, vDouble, vBool, vJson) = Classify(element);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO workflow_variable_cache (
                        flowable_instance_id, name, value_text, value_long, value_double,
                        value_bool, value_json, type, updated_time,
                        projection_version, last_sync_at)
                    VALUES (
                        {instanceId}, {name}, {vText}, {vLong}, {vDouble},
                        {vBool}, {vJson}::jsonb, {type}, {now},
                        {_options.CurrentProjectionVersion}, {now})
                    """, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }

        foreach (var instanceId in deletes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_variable_cache WHERE flowable_instance_id = {instanceId}",
                cancellationToken);
        }
    }

    // Flatten a JsonElement into the column it should populate. Only one of
    // the typed columns is non-null per row, matching the way Flowable stores
    // historic variable instances (one column per type discriminator). The
    // type string is the JsonValueKind name so downstream consumers can
    // round-trip without ambiguity.
    private static (string Type, string? Text, long? Long, double? Double, bool? Bool, string? Json) Classify(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ("string", element.GetString(), null, null, null, null),
            JsonValueKind.Number => element.TryGetInt64(out var l)
                ? ("long", null, l, null, null, null)
                : ("double", null, null, element.GetDouble(), null, null),
            JsonValueKind.True => ("bool", null, null, null, true, null),
            JsonValueKind.False => ("bool", null, null, null, false, null),
            JsonValueKind.Null => ("null", null, null, null, null, null),
            JsonValueKind.Object or JsonValueKind.Array => ("json", null, null, null, null, element.GetRawText()),
            _ => ("unknown", element.GetRawText(), null, null, null, null)
        };
    }
}
