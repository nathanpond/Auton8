using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Starting a workflow is a write the caller's next read reflects (#609).
/// </summary>
/// <remarks>
/// <para>
/// The executions list is served from <c>workflow_execution_cache</c>, and the
/// start endpoint never wrote to it — so a person who started a workflow did not
/// see it for up to <c>ExecutionPollInterval</c>, a minute, and everything that
/// acts on a row had no row to act on.
/// </para>
/// <para>
/// This is #604's defect on the sibling cache, found the same way: it was the
/// four full-local specs still failing after #604 took the tier from 24 to 4, all
/// of them "start it, then find it in the list". Those four need a live engine
/// and GitHub never runs them; this runs on every merge.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WorkflowStartProjectsInstanceTests
{
    [Fact]
    public async Task A_started_instance_is_in_the_cache_before_the_call_returns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string ProcessKey = "start_projects_flow";
        const string InstanceId = "inst-609";

        factory.FlowableStub.StartedInstance = new FlowableProcessInstanceSummary
        {
            Id = InstanceId,
            Name = "Invoice for Acme",
            ProcessDefinitionId = "invoice:1:1"
        };

        // What the read-through will find when it goes looking.
        factory.FlowableStub.InstancesById[InstanceId] = factory.FlowableStub.StartedInstance;

        var started = await client.PostAsJsonAsync($"/api/workflows/{ProcessKey}/start", new { });
        started.EnsureSuccessStatusCode();

        // NO POLL, NO DELAY. Before the fix this row did not exist until the
        // execution poll ran, a minute later.
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var row = await db.WorkflowExecutionCache.AsNoTracking()
            .SingleOrDefaultAsync(e => e.FlowableInstanceId == InstanceId);

        Assert.NotNull(row);
        Assert.Equal("Invoice for Acme", row!.Name);
    }

    /// <summary>
    /// Cancelling keeps the row and marks it cancelled (#609).
    /// </summary>
    /// <remarks>
    /// <b>Not the read-through the start path uses.</b> A cancelled instance is
    /// gone from the engine's RUNTIME, so reading back would find nothing and
    /// delete the row — and the list is supposed to keep showing it, as cancelled.
    /// It is marked from the positive fact of the cancellation instead, and the
    /// poll reconciles the rest from history.
    /// </remarks>
    [Fact]
    public async Task Cancelling_marks_the_row_cancelled_rather_than_removing_it()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string InstanceId = "inst-609c";
        await SeedExecutionAsync(factory, InstanceId, "Invoice for Gamma");

        var cancelled = await client.PostAsJsonAsync(
            $"/api/executions/{InstanceId}/cancel", new { });
        cancelled.EnsureSuccessStatusCode();

        var row = await ReadExecutionAsync(factory, InstanceId);

        Assert.NotNull(row);
        Assert.Equal("cancelled", row!.Status);
        Assert.NotNull(row.EndTime);
    }

    [Fact]
    public async Task Deleting_removes_the_row_in_the_same_request()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string InstanceId = "inst-609d";
        await SeedExecutionAsync(factory, InstanceId, "Invoice for Delta");

        var deleted = await client.DeleteAsync($"/api/executions/{InstanceId}");
        deleted.EnsureSuccessStatusCode();

        Assert.Null(await ReadExecutionAsync(factory, InstanceId));
    }

    /// <summary>
    /// A projection failure does not fail the start (#609).
    /// </summary>
    /// <remarks>
    /// The complement, and the one that matters operationally: the instance is
    /// already running by the time this runs. Reporting failure for work that
    /// succeeded would be worse than a cache that is briefly behind — the poll is
    /// still the safety net.
    /// </remarks>
    [Fact]
    public async Task A_projection_failure_does_not_fail_a_start_that_succeeded()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        factory.FlowableStub.StartedInstance = new FlowableProcessInstanceSummary
        {
            Id = "inst-609b",
            Name = "Invoice for Beta",
            ProcessDefinitionId = "invoice:1:1"
        };

        // The engine accepted the start and then became unreachable for the
        // read-through that follows it.
        factory.FlowableStub.GetProcessInstanceThrows = new HttpRequestException("connection refused");

        var started = await client.PostAsJsonAsync("/api/workflows/start_projects_flow/start", new { });

        Assert.True(
            started.IsSuccessStatusCode,
            $"The start succeeded in the engine, so it must not be reported as failed; got {started.StatusCode}.");
    }

    // ---- helpers ----

    private static async Task SeedExecutionAsync(
        AutoNateWebApplicationFactory factory, string instanceId, string name)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider
            .GetRequiredService<AutoNate.Web.Services.Flowable.Cache.FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [new AutoNate.Web.Services.Projections.ChangeEvent<WorkflowExecutionSummary>(
                AutoNate.Web.Services.Projections.ChangeOp.Upsert, instanceId,
                new WorkflowExecutionSummary
                {
                    Id = instanceId,
                    Name = name,
                    ProcessDefinitionId = "invoice:1:1",
                    Status = "Running",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }

    private static async Task<AutoNate.Web.Persistence.Scaffolded.WorkflowExecutionCache?> ReadExecutionAsync(
        AutoNateWebApplicationFactory factory, string instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkflowExecutionCache.AsNoTracking()
            .SingleOrDefaultAsync(e => e.FlowableInstanceId == instanceId);
    }
}
