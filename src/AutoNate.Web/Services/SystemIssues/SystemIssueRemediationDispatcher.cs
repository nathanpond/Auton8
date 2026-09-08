using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemIssueEntity = AutoNate.Web.Persistence.Scaffolded.SystemIssue;

namespace AutoNate.Web.Services.SystemIssues;

// Near-clone of AuditOutboxDispatcher: claim a batch with FOR UPDATE SKIP
// LOCKED, dispatch each row to the matching IIssueRemediator, write back the
// result. Built up front (Phase 1) so future detectors can opt into auto-
// remediation without re-plumbing — when no IIssueRemediator is registered for
// a detector, the dispatcher Skip()s and clears next_remediation_after_utc so
// the row stops being polled.
public sealed class SystemIssueRemediationDispatcher(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IEnumerable<IIssueRemediator> remediators,
    IAuditEventPublisher auditPublisher,
    IServiceScopeFactory scopeFactory,
    IOptions<SystemIssueOptions> options,
    ILogger<SystemIssueRemediationDispatcher> logger,
    // #215. One clock decides both when a retry is scheduled and when it is
    // due. Defaults to the system clock, so production behaviour is unchanged;
    // the parameter exists so a test can move that clock and prove the
    // eligibility window follows it rather than the database server's.
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly SystemIssueOptions _options = options.Value;
    private readonly IReadOnlyList<IIssueRemediator> _remediators = remediators.ToList();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RemediationEnabled)
        {
            logger.LogInformation(
                "System issue remediation dispatcher disabled via {Section}:RemediationEnabled.",
                SystemIssueOptions.SectionName);
            return;
        }

        if (_remediators.Count == 0)
        {
            logger.LogInformation(
                "System issue remediation dispatcher started with zero registered remediators. " +
                "Loop will run but no work will match until a detector registers an IIssueRemediator.");
        }

        var poll = _options.RemediationPollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The dispatcher must never die. Log and back off.
                logger.LogError(ex, "System issue remediation tick failed; retrying after PollInterval.");
                try { await Task.Delay(poll, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // On-demand remediation triggered by POST /api/system-issues/{id}/remediate.
    // Bypasses the dispatcher's poll cadence and backoff schedule so an
    // operator can retry immediately. Returns:
    //   * RemediationOutcome.NoRemediator — no IIssueRemediator matches the
    //     issue (endpoint surfaces 404 + reason).
    //   * RemediationOutcome.NotFound — issue id doesn't exist.
    //   * RemediationOutcome.NotEligible — issue isn't open (already
    //     resolved/auto_resolved).
    //   * RemediationOutcome.Result — the IIssueRemediator's result. Caller
    //     can read .Result to differentiate Success / Failure / Skip.
    public async Task<RemediationOutcome> RemediateNowAsync(Guid issueId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE so we serialise with the loop dispatcher and concurrent
        // /remediate calls. The row count + bound id are dynamic so we use
        // FromSqlInterpolated.
        var row = await dbContext.SystemIssues
            .FromSqlInterpolated($"SELECT * FROM system_issues WHERE id = {issueId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return RemediationOutcome.NotFound;
        }

        if (row.State != SystemIssueStates.Open)
        {
            await tx.RollbackAsync(cancellationToken);
            return RemediationOutcome.NotEligible;
        }

        var domain = ToDomain(row);
        var remediator = _remediators.FirstOrDefault(r => r.DetectorId == row.DetectorId && r.CanRemediate(domain));
        if (remediator is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return RemediationOutcome.NoRemediator;
        }

        // Reuse the same per-row apply-result logic as the loop. Means
        // attempt counts, audit events, and state transitions stay
        // consistent regardless of which path triggered the remediation.
        await using var scope = scopeFactory.CreateAsyncScope();
        await DispatchOneAsync(row, scope.ServiceProvider, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Re-derive the outcome from the row's post-state. State == AutoResolved
        // means Success ran; AutoRemediationLastError set without a state
        // change means Failure (or Skip). Skip clears NextRemediationAfterUtc.
        if (row.State == SystemIssueStates.AutoResolved)
        {
            return RemediationOutcome.From(new RemediationResult.Success(row.ResolutionNotes));
        }
        if (row.AutoRemediationLastError is not null && row.NextRemediationAfterUtc is not null)
        {
            return RemediationOutcome.From(new RemediationResult.Failure(row.AutoRemediationLastError));
        }
        return RemediationOutcome.From(new RemediationResult.Skip(row.AutoRemediationLastError ?? "Skipped."));
    }

    // Public so tests can drive a single tick without spinning up the
    // BackgroundService loop. Returns the number of rows considered (success,
    // failure, or skip) in this pass.
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // #215. The cutoff is the caller's clock, not the database server's.
        //
        // next_remediation_after_utc is written from the same clock (see
        // DispatchOneAsync), so comparing it against the server's NOW() asks
        // two different clocks to agree. They mostly do, and then the suite
        // runs on a machine where Postgres is in a VM whose clock lags under
        // host CPU pressure: a row scheduled with zero backoff is written at
        // the client's "now" and is not yet <= the server's "now", so the next
        // tick silently skips it. That is what made
        // SystemIssueRemediationTests.Failing_remediator_caps_attempts_and_
        // leaves_issue_open flake under load and pass in isolation — it counts
        // three ticks and got two.
        //
        // Reading and writing the schedule with one clock removes the
        // disagreement. Every other timestamp this dispatcher writes is
        // already client-side, so client-side is the coherent choice.
        var cutoffUtc = _clock.GetUtcNow().UtcDateTime;

        var batch = await dbContext.SystemIssues
            .FromSqlRaw(
                """
                SELECT * FROM system_issues
                WHERE state = 'open'
                  AND next_remediation_after_utc IS NOT NULL
                  AND next_remediation_after_utc <= {2}
                  AND auto_remediation_attempt_count < {0}
                ORDER BY next_remediation_after_utc
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                _options.MaxRemediationAttempts,
                _options.RemediationBatchSize,
                cutoffUtc)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();

        foreach (var row in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchOneAsync(row, scope.ServiceProvider, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task DispatchOneAsync(
        SystemIssueEntity row,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var domain = ToDomain(row);
        var remediator = _remediators.FirstOrDefault(r => r.DetectorId == row.DetectorId && r.CanRemediate(domain));
        if (remediator is null)
        {
            // No remediator. Stop polling this row but leave the issue open
            // for humans. Don't consume an attempt.
            row.NextRemediationAfterUtc = null;
            row.AutoRemediationLastError = "No registered remediator for detector " + row.DetectorId;
            return;
        }

        RemediationResult result;
        try
        {
            result = await remediator.TryRemediateAsync(domain, cancellationToken);
        }
        catch (Exception ex)
        {
            result = new RemediationResult.Failure(ex.Message);
            logger.LogError(ex,
                "Remediator {DetectorId} threw while remediating issue {IssueId} (fingerprint={Fingerprint}).",
                row.DetectorId, row.Id, row.Fingerprint);
        }

        switch (result)
        {
            case RemediationResult.Success success:
                row.State = SystemIssueStates.AutoResolved;
                row.ResolutionKind = SystemIssueResolutionKinds.AutoRemediated;
                row.ResolutionNotes = success.Notes;
                row.ResolvedAtUtc = _clock.GetUtcNow().UtcDateTime;
                row.AutoRemediationAttemptCount += 1;
                row.AutoRemediationLastError = null;
                row.NextRemediationAfterUtc = null;
                await auditPublisher.PublishAsync(
                    SystemIssueEventTopic.TopicName,
                    SystemIssueEventTypes.AutoResolved,
                    SystemIssueEventTopic.ResourceKind,
                    resource: new { id = row.Id, fingerprint = row.Fingerprint, severity = row.Severity },
                    details: new
                    {
                        resolutionKind = SystemIssueResolutionKinds.AutoRemediated,
                        notes = success.Notes
                    },
                    cancellationToken);
                break;

            case RemediationResult.Failure failure:
                row.AutoRemediationAttemptCount += 1;
                row.AutoRemediationLastError = failure.Error;
                if (row.AutoRemediationAttemptCount >= _options.MaxRemediationAttempts)
                {
                    // Give up. Leaves issue open for triage.
                    row.NextRemediationAfterUtc = null;
                }
                else
                {
                    row.NextRemediationAfterUtc =
                        _clock.GetUtcNow().UtcDateTime + ComputeBackoff(row.AutoRemediationAttemptCount);
                }
                await auditPublisher.PublishAsync(
                    SystemIssueEventTopic.TopicName,
                    SystemIssueEventTypes.RemediationFailed,
                    SystemIssueEventTopic.ResourceKind,
                    resource: new { id = row.Id, fingerprint = row.Fingerprint, detectorId = row.DetectorId },
                    details: new
                    {
                        attemptCount = row.AutoRemediationAttemptCount,
                        maxAttempts = _options.MaxRemediationAttempts,
                        error = failure.Error
                    },
                    cancellationToken);
                break;

            case RemediationResult.Skip skip:
                row.NextRemediationAfterUtc = null;
                row.AutoRemediationLastError = skip.Reason;
                break;
        }
    }

    private TimeSpan ComputeBackoff(int attemptCount)
    {
        // Exponential, capped. Mirrors AuditOutboxDispatcher.ComputeBackoff.
        var multiplier = 1L << Math.Min(attemptCount - 1, 30);
        var ticks = _options.RemediationBaseBackoff.Ticks * multiplier;
        if (ticks > _options.RemediationMaxBackoff.Ticks || ticks < 0)
        {
            return _options.RemediationMaxBackoff;
        }
        return TimeSpan.FromTicks(ticks);
    }

    // Discriminator for the on-demand remediate path. Using a record over an
    // enum so we can carry the underlying RemediationResult (Success notes,
    // Failure error message) back to the API layer.
    public abstract record RemediationOutcome
    {
        public static readonly RemediationOutcome NotFound = new NotFoundOutcome();
        public static readonly RemediationOutcome NotEligible = new NotEligibleOutcome();
        public static readonly RemediationOutcome NoRemediator = new NoRemediatorOutcome();

        public static RemediationOutcome From(RemediationResult result) => new ResultOutcome(result);

        public sealed record NotFoundOutcome : RemediationOutcome;
        public sealed record NotEligibleOutcome : RemediationOutcome;
        public sealed record NoRemediatorOutcome : RemediationOutcome;
        public sealed record ResultOutcome(RemediationResult Result) : RemediationOutcome;
    }

    private static SystemIssue ToDomain(SystemIssueEntity row) => new(
        row.Id, row.DetectorId, row.Category, row.Severity, row.Fingerprint,
        row.Title, row.Summary, row.RelatedEntityKind, row.RelatedEntityId,
        row.FactsJson, row.State,
        new DateTimeOffset(DateTime.SpecifyKind(row.FirstSeenAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(row.LastSeenAtUtc, DateTimeKind.Utc)),
        row.OccurrenceCount,
        row.AcknowledgedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.AcknowledgedAtUtc.Value, DateTimeKind.Utc))
            : null,
        row.AcknowledgedBy,
        row.ResolvedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.ResolvedAtUtc.Value, DateTimeKind.Utc))
            : null,
        row.ResolutionKind, row.ResolutionNotes,
        row.AutoRemediationAttemptCount, row.AutoRemediationLastError,
        row.NextRemediationAfterUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.NextRemediationAfterUtc.Value, DateTimeKind.Utc))
            : null);
}
