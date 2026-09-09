using System.Runtime.CompilerServices;
using AutoNate.Web.Services.SystemIssues;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The trap still works, and no longer contaminates other hosts (#215).
/// </summary>
/// <remarks>
/// <para>
/// <c>BackgroundExceptionTrap</c> subscribes the <b>process-global</b>
/// <c>AppDomain.CurrentDomain.UnhandledException</c> and
/// <c>TaskScheduler.UnobservedTaskException</c>. In production that is correct —
/// one host per process. In this suite many hosts run concurrently, so a single
/// stray unobserved exception anywhere fired <em>every</em> live trap and each wrote
/// a <c>system_issues</c> row into its own database. That is exactly the shape of
/// <c>SystemIssueEndpointsTests</c>' <c>Assert.Single() … contained 2 items</c>.
/// </para>
/// <para>
/// The fix is test wiring: <c>AutoNateWebApplicationFactory</c> removes the hosted
/// registration. This class exists so that removal is not the same as switching the
/// feature off — the trap's own behaviour is exercised here directly.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BackgroundExceptionTrapTests
{
    // Only RecordAsync is exercised; the rest of the interface is not this trap's
    // business and throwing makes an accidental call obvious rather than silent.
    private sealed class RecordingSystemIssueRecorder : ISystemIssueRecorder
    {
        public List<string> Recorded { get; } = [];

        public Task<RecordIssueResult> RecordAsync(
            SystemIssueDraft draft, CancellationToken cancellationToken = default)
        {
            lock (Recorded) Recorded.Add(draft.Title);
            return Task.FromResult(new RecordIssueResult(Guid.NewGuid(), true, 1, null));
        }

        public Task<SystemIssue?> MarkResolvedByFingerprintAsync(
            string fingerprint, string resolutionKind, string? notes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SystemIssue?> AcknowledgeAsync(
            Guid id, Guid actorId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SystemIssue?> ResolveAsync(
            Guid id, Guid actorId, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task The_trap_records_an_unobserved_task_exception()
    {
        // AC1: the trap is still exercised, so removing it from the test host cannot
        // be mistaken for the feature being switched off.
        var recorder = new RecordingSystemIssueRecorder();
        var trap = new BackgroundExceptionTrap(recorder, NullLogger<BackgroundExceptionTrap>.Instance);

        await trap.StartAsync(CancellationToken.None);
        try
        {
            RaiseUnobservedTaskException();

            // The event fires on finalization, so collect and drain.
            for (var attempt = 0; attempt < 20 && recorder.Recorded.Count == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(50);
            }

            Assert.NotEmpty(recorder.Recorded);
        }
        finally
        {
            await trap.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_stopped_trap_records_nothing()
    {
        // The complement, and the property the fix depends on: once StopAsync has
        // unsubscribed, a stray exception elsewhere in the process cannot reach this
        // recorder. Without this, "we removed the hosted service" would be an
        // assertion about configuration rather than about behaviour.
        var recorder = new RecordingSystemIssueRecorder();
        var trap = new BackgroundExceptionTrap(recorder, NullLogger<BackgroundExceptionTrap>.Instance);

        await trap.StartAsync(CancellationToken.None);
        await trap.StopAsync(CancellationToken.None);

        RaiseUnobservedTaskException();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }

        Assert.Empty(recorder.Recorded);
    }

    [Fact]
    public async Task A_stray_exception_does_not_reach_a_live_test_host()
    {
        // AC2, asserted as an ABSENCE. Two hosts are live; a stray unobserved
        // exception is raised by neither of them; and no system_issues row appears
        // in either database.
        //
        // Before the fix both hosts recorded one, because both traps were
        // subscribed to the same process-global event — and the row landed in a
        // database belonging to a test that had done nothing wrong, which is why the
        // failure surfaced in an unrelated class.
        await using var first = await AutoNateWebApplicationFactory.CreateAsync();
        await using var second = await AutoNateWebApplicationFactory.CreateAsync();
        _ = first.CreateClient();
        _ = second.CreateClient();

        RaiseUnobservedTaskException();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }

        Assert.Equal(0, await CountSystemIssuesAsync(first));
        Assert.Equal(0, await CountSystemIssuesAsync(second));
    }

    private static async Task<int> CountSystemIssuesAsync(AutoNateWebApplicationFactory factory)
    {
        await using var context = factory.Database.CreateDbContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(context.SystemIssues);
    }

    // Deliberately not inlined: the Task must become unreachable for the finalizer
    // to raise UnobservedTaskException, which cannot happen while a local in the
    // calling frame still refers to it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RaiseUnobservedTaskException()
    {
        _ = Task.Run(() => throw new InvalidOperationException("#215 stray background exception"));
    }
}
