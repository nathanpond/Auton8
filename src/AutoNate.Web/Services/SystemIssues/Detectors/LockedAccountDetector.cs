using System.Text.Json;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Periodic. Surfaces accounts that have been locked longer than a tolerance
// window. Helps an operator notice "this user has been locked out for two
// days" without having to dig through audit logs.
//
// One issue per locked user, fingerprint includes the user id so each
// account dedups on its own. When the user is unlocked (manually via the
// admin UI, or eventually by a future auto-unlock policy), the next tick
// resolves the issue with no_longer_present.
public sealed class LockedAccountDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ISystemIssueRecorder recorder,
    ISystemIssueStore issueStore,
    IOptions<LockedAccountDetectorOptions> lockedOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<LockedAccountDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly LockedAccountDetectorOptions _lockedOptions = lockedOptions.Value;

    public const string DetectorIdValue = "locked_account";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _lockedOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var olderThan = DateTime.UtcNow - _lockedOptions.MinLockedDuration;

        var lockedUsers = await dbContext.LocalUsers.AsNoTracking()
            .Where(u => u.IsLocked && u.LockedAtUtc != null && u.LockedAtUtc < olderThan)
            .Select(u => new
            {
                u.UserId,
                u.Username,
                u.LockedAtUtc,
                u.FailedLoginAttempts
            })
            .ToListAsync(cancellationToken);

        var thisTick = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in lockedUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = FingerprintFor(user.UserId);
            thisTick.Add(fingerprint);

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Auth,
                Severity: SystemIssueSeverities.Info,
                Fingerprint: fingerprint,
                Title: $"Account '{user.Username}' has been locked for over {(int)_lockedOptions.MinLockedDuration.TotalMinutes} min",
                Summary: $"User {user.Username} ({user.UserId}) was locked at {user.LockedAtUtc:O} after {user.FailedLoginAttempts} failed attempts.",
                RelatedEntityKind: "user",
                RelatedEntityId: user.UserId.ToString(),
                FactsJson: JsonSerializer.Serialize(new
                {
                    userId = user.UserId,
                    username = user.Username,
                    lockedAtUtc = user.LockedAtUtc,
                    failedLoginAttempts = user.FailedLoginAttempts
                })));
        }

        // Auto-resolve via DB query so an issue stranded by an app restart
        // still gets cleared when the user is unlocked. (In-memory tracking
        // would be empty after restart and miss the cleanup.)
        var openInDb = await issueStore.ListOpenFingerprintsForDetectorAsync(DetectorIdValue, cancellationToken);
        foreach (var fingerprint in openInDb)
        {
            if (thisTick.Contains(fingerprint)) continue;
            await recorder.MarkResolvedByFingerprintAsync(
                fingerprint,
                SystemIssueResolutionKinds.NoLongerPresent,
                notes: "Account is no longer locked.",
                cancellationToken);
        }
    }

    public static string FingerprintFor(Guid userId) => $"auth:account_locked:{userId}";
}

public sealed class LockedAccountDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:LockedAccount";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    // Don't open issues for transient lockouts that an operator hasn't had
    // a chance to clear yet. 15 minutes leaves a generous "user just hit
    // the lockout threshold and is calling support" window.
    public TimeSpan MinLockedDuration { get; set; } = TimeSpan.FromMinutes(15);
}
