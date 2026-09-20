using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Decisions;

/// <summary>
/// <see cref="IDecisionTableStore"/> over <c>decision_tables</c> (#110).
/// </summary>
public sealed class EfCoreDecisionTableStore(IDbContextFactory<AutoNateDbContext> dbContextFactory)
    : IDecisionTableStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<DecisionTableModel>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.DecisionTables
            .AsNoTracking()
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(ToModel).ToList();
    }

    public async Task<DecisionTableModel?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.DecisionTables.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<DecisionTableModel?> GetByKeyAsync(
        string decisionKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.DecisionTables.AsNoTracking()
            .SingleOrDefaultAsync(t => t.DecisionKey == decisionKey, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<DecisionTableModel> SaveAsync(
        DecisionTableModel table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        // Validated HERE, not only at the endpoint. Storing rules as structured
        // data buys nothing if a bad cell can reach the database through a caller
        // that forgot -- and the next caller is always the one that forgets.
        var errors = DecisionTableValidator.Validate(table);
        if (errors.Count > 0)
        {
            throw new DecisionTableInvalidException(errors);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var row = await db.DecisionTables.SingleOrDefaultAsync(t => t.Id == table.Id, cancellationToken);
        if (row is null)
        {
            row = new DecisionTable
            {
                Id = table.Id == Guid.Empty ? Guid.NewGuid() : table.Id,
                CreatedAtUtc = now
            };
            db.DecisionTables.Add(row);
        }

        row.DecisionKey = table.DecisionKey.Trim();
        row.Name = table.Name.Trim();
        row.Description = string.IsNullOrWhiteSpace(table.Description) ? null : table.Description.Trim();
        row.HitPolicy = table.HitPolicy;
        row.Inputs = JsonSerializer.Serialize(table.Inputs, Json);
        row.Outputs = JsonSerializer.Serialize(table.Outputs, Json);
        row.Rules = JsonSerializer.Serialize(table.Rules, Json);
        row.UpdatedAtUtc = now;

        // A save always produces a DRAFT, whatever the caller said. The published
        // version is immutable and lives in its own table; letting a save clear
        // is_draft would make "a process bound to a version keeps that version's
        // behaviour" depend on a field the editor happens to send.
        row.IsDraft = true;

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Versions cascade (ON DELETE CASCADE). That is deliberate: a version whose
        // table is gone cannot be re-published, re-read in the editor, or explained.
        await db.DecisionTables.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<DecisionTableModel> RecordPublishAsync(
        Guid id,
        string dmnXml,
        DecisionDeploymentInfo deployment,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.DecisionTables.SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"No decision table with id {id}.");

        // The next version number comes from the versions table, not from the
        // draft counter. Two publishes racing on the draft counter would both read
        // the same number; the unique index on (decision_table_id, version_number)
        // then refuses the second, which is the right outcome and a confusing one
        // to debug if the number came from somewhere else.
        var lastVersion = await db.DecisionTableVersions
            .Where(v => v.DecisionTableId == id)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;
        var versionNumber = lastVersion + 1;

        var now = DateTime.UtcNow;
        db.DecisionTableVersions.Add(new DecisionTableVersion
        {
            Id = Guid.NewGuid(),
            DecisionTableId = id,
            VersionNumber = versionNumber,
            Name = row.Name,
            DecisionKey = row.DecisionKey,
            HitPolicy = row.HitPolicy,
            Inputs = row.Inputs,
            Outputs = row.Outputs,
            Rules = row.Rules,
            DmnXml = dmnXml,
            DeploymentId = deployment.DeploymentId,
            DecisionId = deployment.DecisionId,
            DecisionDefinitionKey = deployment.DecisionKey,
            DecisionVersion = deployment.DecisionVersion,
            PublishedAtUtc = now
        });

        row.IsDraft = false;
        row.PublishedVersionNumber = versionNumber;
        row.DraftVersionNumber = versionNumber + 1;
        row.LastDeploymentId = deployment.DeploymentId;
        row.LastDecisionId = deployment.DecisionId;
        row.LastDecisionKey = deployment.DecisionKey;
        row.LastDecisionVersion = deployment.DecisionVersion;
        row.LastDeployedAtUtc = now;
        row.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task<IReadOnlyList<DecisionTableVersionSummary>> ListVersionsAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.DecisionTableVersions.AsNoTracking()
            .Where(v => v.DecisionTableId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        return rows.Select(ToVersionSummary).ToList();
    }

    public async Task<PublishedDecisionSnapshot?> GetPublishedSnapshotAsync(
        string decisionKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var table = await db.DecisionTables.AsNoTracking()
            .SingleOrDefaultAsync(t => t.DecisionKey == decisionKey, cancellationToken);

        if (table?.PublishedVersionNumber is not { } published) return null;

        // The version row, not the table row. The table carries the DRAFT's rules
        // once it has been edited since publishing, and binding a process to those
        // would hand it rules nobody published.
        var version = await db.DecisionTableVersions.AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.DecisionTableId == table.Id && v.VersionNumber == published,
                cancellationToken);

        if (version is null || string.IsNullOrWhiteSpace(version.DmnXml)) return null;

        return new PublishedDecisionSnapshot(table.DecisionKey, version.VersionNumber, version.DmnXml);
    }

    private static DecisionTableModel ToModel(DecisionTable row) => new()
    {
        Id = row.Id,
        DecisionKey = row.DecisionKey,
        Name = row.Name,
        Description = row.Description,
        HitPolicy = row.HitPolicy,
        Inputs = Deserialize<DecisionColumn>(row.Inputs),
        Outputs = Deserialize<DecisionColumn>(row.Outputs),
        Rules = Deserialize<DecisionRule>(row.Rules),
        IsDraft = row.IsDraft,
        DraftVersionNumber = row.DraftVersionNumber,
        PublishedVersionNumber = row.PublishedVersionNumber,
        CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc)),
        UpdatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc)),
        LastDeployment = row.LastDeploymentId is null || row.LastDecisionId is null
            ? null
            : new DecisionDeploymentSummary(
                row.LastDeploymentId,
                row.LastDecisionId,
                row.LastDecisionKey ?? row.DecisionKey,
                row.LastDecisionVersion ?? 0,
                new DateTimeOffset(DateTime.SpecifyKind(
                    row.LastDeployedAtUtc ?? row.UpdatedAtUtc, DateTimeKind.Utc)))
    };

    private static DecisionTableVersionSummary ToVersionSummary(DecisionTableVersion row) => new(
        row.Id,
        row.DecisionTableId,
        row.VersionNumber,
        row.Name,
        row.DecisionKey,
        row.DecisionId,
        row.DecisionVersion,
        new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAtUtc, DateTimeKind.Utc)));

    /// <summary>
    /// Reads a JSONB column, treating unreadable content as empty rather than throwing.
    /// </summary>
    /// <remarks>
    /// A table whose rules will not deserialize is a table an author can still open
    /// and repair. Throwing here would make the editor the one surface that cannot
    /// fix the thing that is broken.
    /// </remarks>
    private static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
