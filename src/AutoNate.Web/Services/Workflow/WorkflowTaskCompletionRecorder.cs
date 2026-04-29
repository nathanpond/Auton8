using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

// Persists who completed a Flowable task and whether they used the
// override path. Called synchronously from the complete / force-complete
// endpoints. Failures are logged and swallowed so a recorder hiccup
// doesn't block the actual task completion that already succeeded in
// Flowable.
public sealed class WorkflowTaskCompletionRecorder(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<WorkflowTaskCompletionRecorder> logger)
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<WorkflowTaskCompletionRecorder> _logger = logger;

    public async Task RecordAsync(
        string taskId,
        string completedByUserId,
        bool wasOverride,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(completedByUserId))
        {
            return;
        }

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Upsert by task_id — a retry from the SPA shouldn't produce a
            // duplicate row, and the latest completion attempt wins.
            var existing = await dbContext.WorkflowTaskCompletions
                .FirstOrDefaultAsync(c => c.TaskId == taskId, cancellationToken);

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                dbContext.WorkflowTaskCompletions.Add(new WorkflowTaskCompletion
                {
                    TaskId = taskId,
                    CompletedByUserId = completedByUserId,
                    CompletedAtUtc = now,
                    WasOverride = wasOverride
                });
            }
            else
            {
                existing.CompletedByUserId = completedByUserId;
                existing.CompletedAtUtc = now;
                existing.WasOverride = wasOverride;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to record task completion for taskId={TaskId} userId={UserId}.",
                taskId,
                completedByUserId);
        }
    }
}
