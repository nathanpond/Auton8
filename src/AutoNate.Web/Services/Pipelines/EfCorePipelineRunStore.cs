using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Pipelines;

public sealed class EfCorePipelineRunStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IPipelineRunStore
{
    public async Task<PipelineRun> EnqueueAsync(
        Guid pipelineId,
        string graphSnapshotJson,
        Guid actorId,
        string triggerKind,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new PipelineRun
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            Status = PipelineRunStatuses.Queued,
            GraphSnapshotJson = graphSnapshotJson,
            QueuedAtUtc = DateTime.UtcNow,
            TriggeredBy = actorId,
            TriggerKind = triggerKind,
        };
        db.PipelineRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<PipelineRun>> DequeueOldestAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PipelineRuns.AsNoTracking()
            .Where(r => r.Status == PipelineRunStatuses.Queued)
            .OrderBy(r => r.QueuedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkRunningAsync(Guid runId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PipelineRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new PipelineRunNotFoundException(runId);
        entity.Status = PipelineRunStatuses.Running;
        entity.StartedAtUtc = utcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid runId, string status, string? errorMessage, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PipelineRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new PipelineRunNotFoundException(runId);
        entity.Status = status;
        entity.CompletedAtUtc = utcNow;
        entity.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PipelineRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PipelineRuns.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<PipelineRun>> ListForPipelineAsync(
        Guid pipelineId, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PipelineRuns.AsNoTracking()
            .Where(r => r.PipelineId == pipelineId)
            .OrderByDescending(r => r.QueuedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<PipelineRunStep> CreateStepAsync(
        Guid runId, string nodeKey, string nodeKind, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new PipelineRunStep
        {
            Id = Guid.NewGuid(),
            PipelineRunId = runId,
            NodeKey = nodeKey,
            NodeKind = nodeKind,
            Status = PipelineRunStatuses.Queued,
        };
        db.PipelineRunSteps.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task MarkStepStartedAsync(Guid stepId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PipelineRunSteps.SingleOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (entity is null) return;
        entity.Status = PipelineRunStatuses.Running;
        entity.StartedAtUtc = utcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkStepCompletedAsync(
        Guid stepId, string status, long? rowCount, string? errorMessage, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PipelineRunSteps.SingleOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (entity is null) return;
        entity.Status = status;
        entity.CompletedAtUtc = utcNow;
        entity.RowCount = rowCount;
        entity.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PipelineRunStep>> ListStepsAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PipelineRunSteps.AsNoTracking()
            .Where(s => s.PipelineRunId == runId)
            .OrderBy(s => s.StartedAtUtc ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);
    }
}
