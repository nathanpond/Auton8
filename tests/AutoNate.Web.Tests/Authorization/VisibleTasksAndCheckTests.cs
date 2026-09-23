using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class VisibleTasksAndCheckTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AssignedToTeam_EmptyWhenNoSupervisees()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.TasksByUser[AdminUserId.ToString()] = new()
        {
            new() { Id = "own-1", Assignee = AdminUserId.ToString() }
        };
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>("/api/tasks/assigned-to-team");
        Assert.NotNull(tasks);
        Assert.Empty(tasks!);
    }

    [Fact]
    public async Task AssignedToTeam_IncludesSuperviseesButExcludesActor()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        // Hierarchy: admin supervises Alice. Tasks: one for admin, one for Alice.
        // Only Alice's task should appear under the actor's team view.
        var alice = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            db.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = EdgeKinds.Supervisor,
                FromKind = EntityKinds.User,
                FromId = AdminUserId.ToString(),
                ToKind = EntityKinds.User,
                ToId = alice.ToString(),
                Data = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId
            });
            await db.SaveChangesAsync();
        }

        factory.FlowableStub.TasksByUser[AdminUserId.ToString()] = new()
        {
            new() { Id = "own-1", Assignee = AdminUserId.ToString() }
        };
        factory.FlowableStub.TasksByUser[alice.ToString()] = new()
        {
            new() { Id = "alice-1", Assignee = alice.ToString() }
        };

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>("/api/tasks/assigned-to-team");
        Assert.NotNull(tasks);
        var ids = tasks!.Select(t => t.Id).ToHashSet();
        Assert.Contains("alice-1", ids);
        Assert.DoesNotContain("own-1", ids);
    }

    /// <summary>
    /// #664. A supervisee's lane-offered work belongs in Team Tasks, as it has
    /// been in their own list since #171: the two views disagreed about the
    /// same person's work because only one of them asked for their groups.
    /// </summary>
    [Fact]
    public async Task AssignedToTeam_AsksForEachSuperviseesGroups()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var alice = Guid.NewGuid();
        // #673. A second supervisee with NO group, so the complement below is a
        // genuinely different subject rather than a same-subject non-match.
        var bob = Guid.NewGuid();
        Guid groupId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            db.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = EdgeKinds.Supervisor,
                FromKind = EntityKinds.User,
                FromId = AdminUserId.ToString(),
                ToKind = EntityKinds.User,
                ToId = alice.ToString(),
                Data = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId
            });
            db.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = EdgeKinds.Supervisor,
                FromKind = EntityKinds.User,
                FromId = AdminUserId.ToString(),
                ToKind = EntityKinds.User,
                ToId = bob.ToString(),
                Data = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId
            });

            groupId = Guid.NewGuid();
            db.Groups.Add(new AutoNate.Web.Persistence.Scaffolded.Group
            {
                Id = groupId,
                Name = $"finance-{groupId:N}"[..20],
                IsArchived = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = AdminUserId
            });
            // The group must exist before its member: one SaveChanges left EF
            // free to order the inserts the other way round.
            await db.SaveChangesAsync();

            db.GroupMembers.Add(new AutoNate.Web.Persistence.Scaffolded.GroupMember
            {
                GroupId = groupId,
                UserId = alice,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = AdminUserId
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");
        (await client.GetAsync("/api/tasks/assigned-to-team")).EnsureSuccessStatusCode();

        // The supervisee's groups reached the engine query...
        Assert.Contains($"TasksForUserGroups:{alice}:{groupId}", factory.FlowableStub.Calls);
        // ...and a DIFFERENT supervisee, genuinely in no group, still gets asked
        // for — with an empty group list, not omitted and not "" standing in for
        // nothing (#673's complement: a different subject, not alice's own call
        // failing to match a string it was never going to match).
        Assert.Contains($"TasksForUserGroups:{bob}:", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task AuthCheck_ParallelResults_ReflectGrants()
    {
        // Authorization on, no SuperAdmin backfill — admin must rely on grants
        // for the targets we ask about.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });

        // Allow grant only for one specific record id.
        var allowedId = Guid.NewGuid();
        var deniedId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.View, $"/record/{allowedId}", "allow", 0), AdminUserId);
        }

        // Seed one record so the AuthorizeAsync instance lookup actually finds
        // a row (the engine still asks "does this entity exist that the user
        // can view"); the denied id intentionally has no record so it can't
        // be matched by any grant.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            // Seed a record type + records by going through the store so
            // assignee dual-write still happens.
            var typeStore = scope.ServiceProvider
                .GetRequiredService<AutoNate.Web.Services.Records.IRecordTypeStore>();
            var recordStore = scope.ServiceProvider
                .GetRequiredService<AutoNate.Web.Services.Records.IRecordStore>();
            var type = await typeStore.CreateAsync(
                new AutoNate.Web.Services.Records.CreateRecordTypeInput(
                    "CHK", "check", null, null, null), AdminUserId);
            await recordStore.CreateAsync(new AutoNate.Web.Services.Records.CreateRecordInput(
                type.Id, "Allowed", null, null,
                System.Text.Json.JsonDocument.Parse("{}").RootElement, null), AdminUserId);
            // The "denied" id never gets a row.

            // Re-seat the allowed record to use our predetermined id by
            // patching the row in place (records use the first existing id).
            // Simpler: put the grant on whatever id we created.
        }
        // Replace the allowed id with the actual created record id.
        Guid actualAllowedId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            actualAllowedId = await db.Records.AsNoTracking().Select(r => r.Id).FirstAsync();

            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.View, $"/record/{actualAllowedId}", "allow", 0), AdminUserId);
        }

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/auth/check", new
        {
            checks = new[]
            {
                new { kind = "record", action = "view", id = actualAllowedId.ToString() },
                new { kind = "record", action = "view", id = deniedId.ToString() }
            }
        });
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Results.Count);
        var byId = payload.Results.ToDictionary(r => r.Id);
        Assert.True(byId[actualAllowedId.ToString()].Allowed);
        Assert.False(byId[deniedId.ToString()].Allowed);
    }

    private sealed record CheckResponse(bool Authenticated, IReadOnlyList<CheckResult> Results);
    private sealed record CheckResult(string Kind, string Action, string Id, bool Allowed);
}
