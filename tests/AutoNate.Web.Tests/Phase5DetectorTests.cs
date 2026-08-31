using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class PluginEnableFailureDetectorTests
{
    [Fact]
    public async Task Plugin_enable_failed_message_opens_a_plugin_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db);

        var pluginId = Guid.NewGuid();
        await detector.HandleAsync(MakeMessage(
            DaprApplicationEventPublisher.TopicName,
            JsonSerializer.Serialize(new
            {
                eventId = Guid.NewGuid(),
                eventType = ApplicationEventTypes.PluginEnableFailed,
                payload = new { id = pluginId.ToString(), name = "MyPlugin", error = "Assembly not found" }
            })));

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal($"plugin:enable_failed:{pluginId}", issue.Fingerprint);
        Assert.Equal(SystemIssueCategories.Plugin, issue.Category);
        Assert.Equal(SystemIssueSeverities.Error, issue.Severity);
        Assert.Contains("MyPlugin", issue.Title);
        Assert.Equal("Assembly not found", issue.Summary);
    }

    [Fact]
    public async Task Other_application_events_are_ignored()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db);

        await detector.HandleAsync(MakeMessage(
            DaprApplicationEventPublisher.TopicName,
            JsonSerializer.Serialize(new
            {
                eventType = ApplicationEventTypes.PluginEnabled,
                payload = new { id = Guid.NewGuid(), name = "MyPlugin" }
            })));

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Repeated_failures_for_same_plugin_dedup_to_one_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db);
        var pluginId = Guid.NewGuid().ToString();

        for (var i = 0; i < 4; i++)
        {
            await detector.HandleAsync(MakeMessage(
                DaprApplicationEventPublisher.TopicName,
                JsonSerializer.Serialize(new
                {
                    eventType = ApplicationEventTypes.PluginEnableFailed,
                    payload = new { id = pluginId, name = "Repeat", error = $"try {i}" }
                })));
        }

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(4, issue.OccurrenceCount);
    }

    private static (PluginEnableFailureDetector detector, EfCoreSystemIssueStore store) CreateDetector(PostgresTestDatabase db)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        var bus = new BusWatcherStreamService(NullLogger<BusWatcherStreamService>.Instance);
        var detector = new PluginEnableFailureDetector(
            bus, store,
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<PluginEnableFailureDetector>.Instance);
        return (detector, store);
    }

    private static BusWatcherStreamService.BusWatcherMessage MakeMessage(string topic, string payload) => new(
        ReceivedAtUtc: DateTimeOffset.UtcNow,
        Topic: topic,
        ContentType: "application/json",
        Headers: new Dictionary<string, string>(),
        Payload: payload);
}

[Trait("Category", "Integration")]
public sealed class WorkflowExecutionErrorOpenDetectorTests
{
    [Fact]
    public async Task Errors_against_running_process_open_one_issue_per_process()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var processId = Guid.NewGuid().ToString();
        await SeedExecutionErrorsAsync(db, processId, 3);

        var flowable = new StubFlowableClient();
        flowable.InstancesById[processId] = new FlowableProcessInstanceSummary
        {
            Id = processId,
            ProcessDefinitionId = "def:1",
            ActivityId = "task1",
            Suspended = false
        };

        var detector = CreateDetector(db, flowable);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(WorkflowExecutionErrorOpenDetector.FingerprintFor(processId), issue.Fingerprint);
        Assert.Equal(SystemIssueCategories.Workflow, issue.Category);
        Assert.Equal(SystemIssueSeverities.Error, issue.Severity);
        using var facts = JsonDocument.Parse(issue.FactsJson);
        Assert.Equal(3, facts.RootElement.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Errors_against_completed_process_resolve_any_open_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var processId = Guid.NewGuid().ToString();
        await SeedExecutionErrorsAsync(db, processId, 1);

        var flowable = new StubFlowableClient();
        // Simulate the process being live during the first tick…
        flowable.InstancesById[processId] = new FlowableProcessInstanceSummary
        {
            Id = processId,
            ProcessDefinitionId = "def:1"
        };
        var detector = CreateDetector(db, flowable);
        await detector.RunOnceAsync(CancellationToken.None);

        // …and gone on the next tick.
        flowable.InstancesById.Remove(processId);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.NoLongerPresent, issue.ResolutionKind);
    }

    [Fact]
    public async Task Errors_against_unknown_process_open_no_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var processId = Guid.NewGuid().ToString();
        await SeedExecutionErrorsAsync(db, processId, 1);
        var flowable = new StubFlowableClient(); // no entry → null

        var detector = CreateDetector(db, flowable);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    private static WorkflowExecutionErrorOpenDetector CreateDetector(PostgresTestDatabase db, StubFlowableClient flowable)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new WorkflowExecutionErrorOpenDetector(
            db.CreateDbContextFactory(), flowable, store,
            Options.Create(new WorkflowExecutionErrorOpenDetectorOptions()),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<WorkflowExecutionErrorOpenDetector>.Instance);
    }

    private static async Task SeedExecutionErrorsAsync(PostgresTestDatabase db, string processInstanceId, int count)
    {
        await using var seed = db.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            seed.WorkflowExecutionErrors.Add(new WorkflowExecutionError
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = processInstanceId,
                ActivityId = $"task{i}",
                ActivityName = $"Task {i}",
                ErrorMessage = $"boom {i}",
                RawFlowableEventType = "JOB_EXECUTION_FAILURE",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await seed.SaveChangesAsync();
    }
}

[Trait("Category", "Integration")]
public sealed class LockedAccountDetectorTests
{
    [Fact]
    public async Task Old_locked_user_opens_an_info_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var seededAdmin = await LockSeededAdminAsync(db, lockedMinutesAgo: 60);

        var detector = CreateDetector(db, new LockedAccountDetectorOptions
        {
            MinLockedDuration = TimeSpan.FromMinutes(15)
        });
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(LockedAccountDetector.FingerprintFor(seededAdmin), issue.Fingerprint);
        Assert.Equal(SystemIssueSeverities.Info, issue.Severity);
        Assert.Equal(SystemIssueCategories.Auth, issue.Category);
    }

    [Fact]
    public async Task Recent_lockout_below_threshold_does_not_open_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await LockSeededAdminAsync(db, lockedMinutesAgo: 2);

        var detector = CreateDetector(db, new LockedAccountDetectorOptions
        {
            MinLockedDuration = TimeSpan.FromMinutes(15)
        });
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Account_unlock_resolves_the_open_issue_on_next_tick()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await LockSeededAdminAsync(db, lockedMinutesAgo: 60);

        var detector = CreateDetector(db, new LockedAccountDetectorOptions
        {
            MinLockedDuration = TimeSpan.FromMinutes(15)
        });
        await detector.RunOnceAsync(CancellationToken.None);

        // Unlock the user out-of-band.
        await using (var seed = db.CreateDbContext())
        {
            await seed.LocalUsers.ExecuteUpdateAsync(set => set
                .SetProperty(u => u.IsLocked, false)
                .SetProperty(u => u.LockedAtUtc, (DateTime?)null));
        }
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
    }

    private static LockedAccountDetector CreateDetector(PostgresTestDatabase db, LockedAccountDetectorOptions opts)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new LockedAccountDetector(
            db.CreateDbContextFactory(), store, store,
            Options.Create(opts),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<LockedAccountDetector>.Instance);
    }

    private static async Task<Guid> LockSeededAdminAsync(PostgresTestDatabase db, int lockedMinutesAgo)
    {
        await using var seed = db.CreateDbContext();
        var admin = await seed.LocalUsers.FirstAsync(u => u.Username == "admin");
        admin.IsLocked = true;
        admin.LockedAtUtc = DateTime.UtcNow.AddMinutes(-lockedMinutesAgo);
        admin.FailedLoginAttempts = 5;
        await seed.SaveChangesAsync();
        return admin.UserId;
    }
}

[Trait("Category", "Integration")]
public sealed class RepeatedAuthFailureDetectorTests
{
    [Fact]
    public async Task Below_threshold_does_not_open_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 5);

        for (var i = 0; i < 4; i++)
        {
            await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));
        }

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Threshold_inside_window_opens_a_warning_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 5);

        for (var i = 0; i < 5; i++)
        {
            await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));
        }

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueSeverities.Warning, issue.Severity);
        Assert.Equal(SystemIssueCategories.Auth, issue.Category);
        Assert.Contains("alice", issue.Title);
    }

    [Fact]
    public async Task Different_usernames_track_independent_windows()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 3);

        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));
        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));
        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "bob"));
        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));
        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginFailed, "alice"));

        await using var read = db.CreateDbContext();
        var issues = await read.SystemIssues.AsNoTracking().ToListAsync();
        // alice crossed threshold (4 then 5 failures), bob hit just one — only alice opens.
        var aliceIssue = Assert.Single(issues);
        Assert.Contains("alice", aliceIssue.Title);
    }

    [Fact]
    public async Task Login_succeeded_events_do_not_count()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 1);

        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.LoginSucceeded, "alice"));
        await detector.HandleAsync(MakeAuthMessage(AuthEventTypes.AccountLocked, "alice"));

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    // #72: _windows was keyed by the attacker-supplied username from the
    // unauthenticated login endpoint and never evicted, so credential stuffing
    // with rotating usernames grew a singleton's heap without bound.
    [Fact]
    public async Task Distinct_usernames_do_not_grow_the_window_map_without_bound()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // Window of zero: every entry is already outside it, so the sweep
        // should reclaim each username as the next one arrives.
        var (detector, _) = CreateDetector(db, threshold: 1_000_000, window: TimeSpan.Zero);

        for (var i = 0; i < 50_000; i++)
        {
            detector.RecordFailure($"user-{i}@example.com");
        }

        Assert.True(
            detector.TrackedUsernameCount < 1_000,
            $"window map held {detector.TrackedUsernameCount} usernames after 50,000 distinct failures");
    }

    // The ceiling has to hold even when nothing ages out — a burst inside one
    // window, where the sweep finds every entry still live.
    [Fact]
    public async Task Window_map_is_capped_even_when_nothing_expires()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 1_000_000, window: TimeSpan.FromHours(1));

        for (var i = 0; i < 30_000; i++)
        {
            detector.RecordFailure($"burst-{i}@example.com");
        }

        Assert.True(
            detector.TrackedUsernameCount <= 10_000,
            $"window map held {detector.TrackedUsernameCount} usernames, above the 10,000 ceiling");
    }

    // Bounding must not break the thing the detector exists to do.
    [Fact]
    public async Task Sweeping_does_not_lose_an_in_window_count()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (detector, _) = CreateDetector(db, threshold: 1_000_000, window: TimeSpan.FromMinutes(5));

        for (var i = 0; i < 4; i++)
        {
            detector.RecordFailure("noise-" + i);
        }
        detector.RecordFailure("victim@example.com");
        detector.RecordFailure("victim@example.com");
        var (count, _) = detector.RecordFailure("victim@example.com");

        Assert.Equal(3, count);
    }

    private static (RepeatedAuthFailureDetector detector, EfCoreSystemIssueStore store) CreateDetector(
        PostgresTestDatabase db, int threshold, TimeSpan? window = null)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        var bus = new BusWatcherStreamService(NullLogger<BusWatcherStreamService>.Instance);
        var detector = new RepeatedAuthFailureDetector(
            bus, store,
            Options.Create(new RepeatedAuthFailureDetectorOptions
            {
                Threshold = threshold,
                Window = window ?? TimeSpan.FromMinutes(5)
            }),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<RepeatedAuthFailureDetector>.Instance);
        return (detector, store);
    }

    private static BusWatcherStreamService.BusWatcherMessage MakeAuthMessage(string eventType, string username)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid(),
            eventType,
            resourceKind = AuthEventTopic.ResourceKind,
            resource = new { username }
        });
        return new BusWatcherStreamService.BusWatcherMessage(
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            Topic: AuthEventTopic.TopicName,
            ContentType: "application/json",
            Headers: new Dictionary<string, string>(),
            Payload: payload);
    }
}

[Trait("Category", "Integration")]
public sealed class StuckWorkflowExecutionDetectorTests
{
    [Fact]
    public async Task Running_process_idle_past_threshold_opens_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Name = "Slow approval",
            WorkflowModelName = "Approval",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            CurrentStep = "WaitForApproval"
        });

        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(StuckWorkflowExecutionDetector.FingerprintFor("p1"), issue.Fingerprint);
        Assert.Equal(SystemIssueSeverities.Warning, issue.Severity);
        Assert.Contains("Slow approval", issue.Title);
    }

    [Fact]
    public async Task Process_idle_but_parked_at_a_user_task_does_not_open_an_issue()
    {
        // User tasks wait on humans by design — the engine enforces no SLA,
        // so an old "idle" timestamp on a process with an active user task
        // is not stuck. The detector must skip these.
        await using var db = await PostgresTestDatabase.CreateAsync();
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Name = "Approval awaiting human",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-3),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            CurrentStep = "Manager Review"
        });
        flowable.TasksByProcess["p1"] = new List<FlowableTaskSummary>
        {
            new() { Id = "task-1", Name = "Manager Review", ProcessInstanceId = "p1" }
        };

        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Recently_active_running_process_does_not_open_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Completed_processes_are_skipped_even_if_idle()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Status = "Complete",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-3),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
        });

        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Open_issue_from_a_previous_app_run_is_resolved_when_process_now_has_a_user_task()
    {
        // The user-reported regression: app boots, detector flags an idle
        // process, operator sees "stuck workflow" issue, then realises the
        // process is actually waiting on a human user task. After my user-
        // task skip went in, NEW ticks correctly skipped these — but the
        // already-open issue from the previous run wasn't auto-resolved
        // because the auto-resolve loop relied on in-memory state that
        // doesn't survive a restart. Fix queries the DB instead.
        await using var db = await PostgresTestDatabase.CreateAsync();

        // Pre-seed an open stuck-process issue (simulates the buggy state
        // the user landed in, persisted across an app restart).
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        await store.RecordAsync(new SystemIssueDraft(
            DetectorId: StuckWorkflowExecutionDetector.DetectorIdValue,
            Category: SystemIssueCategories.Workflow,
            Severity: SystemIssueSeverities.Warning,
            Fingerprint: StuckWorkflowExecutionDetector.FingerprintFor("p1"),
            Title: "stale issue from previous run"));

        // Process is still in Flowable but now parked at a user task —
        // current logic will skip it, so the auto-resolve loop must close
        // the previously-open issue.
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        });
        flowable.TasksByProcess["p1"] = new List<FlowableTaskSummary>
        {
            new() { Id = "t1", Name = "Manager Review", ProcessInstanceId = "p1" }
        };

        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.NoLongerPresent, issue.ResolutionKind);
        Assert.Contains("user task", issue.ResolutionNotes ?? "");
    }

    [Fact]
    public async Task Process_resumes_progress_resolves_the_open_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var flowable = new StubFlowableClient();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        });
        var detector = CreateDetector(db, flowable, staleAfter: TimeSpan.FromMinutes(30));
        await detector.RunOnceAsync(CancellationToken.None);

        // Tick 2: process moved forward (LastActivityAt now recent).
        flowable.Executions.Clear();
        flowable.Executions.Add(new WorkflowExecutionSummary
        {
            Id = "p1",
            Status = "Running",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LastActivityAtUtc = DateTimeOffset.UtcNow
        });
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
    }

    private static StuckWorkflowExecutionDetector CreateDetector(
        PostgresTestDatabase db, StubFlowableClient flowable, TimeSpan staleAfter)
    {
        var store = new EfCoreSystemIssueStore(
            db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new StuckWorkflowExecutionDetector(
            flowable, store, store,
            Options.Create(new StuckWorkflowExecutionDetectorOptions { StaleAfter = staleAfter }),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<StuckWorkflowExecutionDetector>.Instance);
    }
}
