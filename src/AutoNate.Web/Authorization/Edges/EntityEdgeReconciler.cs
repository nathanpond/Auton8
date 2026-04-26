using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Authorization.Edges;

// Diagnostics for the Phase 2 dual-write. Counts records whose creator and
// assignee edges drifted away from the legacy columns. Zero is the only
// acceptable steady-state value.
public sealed class EntityEdgeReconciler
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public EntityEdgeReconciler(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<RecordEdgeDrift> GetRecordEdgeDriftAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var creatorMissing = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM records r
            WHERE NOT EXISTS (
                SELECT 1 FROM entity_edges e
                WHERE e.edge_kind = 'creator'
                  AND e.from_kind = 'user'
                  AND e.from_id   = r.created_by::text
                  AND e.to_kind   = 'record'
                  AND e.to_id     = r.id::text
            )
            """,
            cancellationToken);

        var creatorOrphaned = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM entity_edges e
            JOIN records r ON r.id::text = e.to_id
            WHERE e.edge_kind = 'creator'
              AND e.to_kind   = 'record'
              AND e.from_id  <> r.created_by::text
            """,
            cancellationToken);

        var assigneeMissing = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM records r
            CROSS JOIN LATERAL UNNEST(r.assignee_ids) AS a
            WHERE NOT EXISTS (
                SELECT 1 FROM entity_edges e
                WHERE e.edge_kind = 'assignee'
                  AND e.from_kind = 'user'
                  AND e.from_id   = a::text
                  AND e.to_kind   = 'record'
                  AND e.to_id     = r.id::text
            )
            """,
            cancellationToken);

        var assigneeOrphaned = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM entity_edges e
            JOIN records r ON r.id::text = e.to_id
            WHERE e.edge_kind = 'assignee'
              AND e.to_kind   = 'record'
              AND NOT (e.from_id::uuid = ANY(r.assignee_ids))
            """,
            cancellationToken);

        return new RecordEdgeDrift(
            CreatorEdgesMissing: creatorMissing,
            CreatorEdgesOrphaned: creatorOrphaned,
            AssigneeEdgesMissing: assigneeMissing,
            AssigneeEdgesOrphaned: assigneeOrphaned);
    }

    public async Task<RecordEdgeShadowDrift> GetRecordEdgeShadowDriftAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // record_edges rows that aren't shadowed in entity_edges by primary key.
        var missingShadows = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM record_edges re
            WHERE NOT EXISTS (SELECT 1 FROM entity_edges ee WHERE ee.id = re.id)
            """,
            cancellationToken);

        // Shadowed entity_edges whose payload drifted away from the source row.
        var divergentShadows = await ScalarAsync(db,
            """
            SELECT COUNT(*) AS "Value"
            FROM entity_edges ee
            JOIN record_edges re ON re.id = ee.id
            JOIN record_edge_types et ON et.id = re.edge_type_id
            WHERE ee.edge_kind <> et.short_code
               OR ee.from_kind <> 'record'
               OR ee.to_kind   <> 'record'
               OR ee.from_id   <> re.from_record_id::text
               OR ee.to_id     <> re.to_record_id::text
            """,
            cancellationToken);

        return new RecordEdgeShadowDrift(missingShadows, divergentShadows);
    }

    private static async Task<long> ScalarAsync(AutoNateDbContext db, string sql, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);
}

public readonly record struct RecordEdgeDrift(
    long CreatorEdgesMissing,
    long CreatorEdgesOrphaned,
    long AssigneeEdgesMissing,
    long AssigneeEdgesOrphaned)
{
    public long Total =>
        CreatorEdgesMissing + CreatorEdgesOrphaned +
        AssigneeEdgesMissing + AssigneeEdgesOrphaned;
}

public readonly record struct RecordEdgeShadowDrift(
    long MissingShadows,
    long DivergentShadows)
{
    public long Total => MissingShadows + DivergentShadows;
}
