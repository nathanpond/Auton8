using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// #45: PUT /api/records/{id}/assignees carries
// RequirePermission(Record, Assign) but has no caller outside its own test —
// the SPA changes assignees through PATCH /api/records/{id}, which was gated
// on Edit alone. So Record:Assign could be granted or denied with no
// observable effect for any real user.
[Trait("Category", "Integration")]
public sealed class RecordAssignEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Patch_WithAssignees_AndNoAssignGrant_IsForbidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantSeedPrerequisitesAsync(factory);
        await GrantAsync(factory, Actions.Edit);
        var client = await SignedInClientAsync(factory);
        var recordId = await CreateRecordAsync(client);

        var resp = await client.PatchAsJsonAsync(
            $"/api/records/{recordId}",
            new { assigneeIds = new[] { AdminUserId } });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAssignees_AndAssignGrant_Succeeds()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantSeedPrerequisitesAsync(factory);
        await GrantAsync(factory, Actions.Edit);
        await GrantAsync(factory, Actions.Assign);
        var client = await SignedInClientAsync(factory);
        var recordId = await CreateRecordAsync(client);

        var resp = await client.PatchAsJsonAsync(
            $"/api/records/{recordId}",
            new { assigneeIds = new[] { AdminUserId } });

        resp.EnsureSuccessStatusCode();
    }

    // Edit alone must still be enough for everything that is not an assignee
    // change — the point is to charge Assign for assignees, not to make Edit
    // require Assign.
    [Fact]
    public async Task Patch_WithoutAssignees_NeedsOnlyEdit()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantSeedPrerequisitesAsync(factory);
        await GrantAsync(factory, Actions.Edit);
        var client = await SignedInClientAsync(factory);
        var recordId = await CreateRecordAsync(client);

        var resp = await client.PatchAsJsonAsync($"/api/records/{recordId}", new { name = "renamed" });

        resp.EnsureSuccessStatusCode();
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string action, string kind = EntityKinds.Record)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, $"/{kind}/*", "allow", 0), AdminUserId);
    }

    // Seeding a record needs its record type first, which is a different kind.
    private static async Task GrantSeedPrerequisitesAsync(AutoNateWebApplicationFactory factory)
    {
        foreach (var action in new[] { Actions.View, Actions.List, Actions.Create })
        {
            await GrantAsync(factory, action, EntityKinds.RecordType);
        }
        await GrantAsync(factory, Actions.View);
        await GrantAsync(factory, Actions.List);
        await GrantAsync(factory, Actions.Create);
    }

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Guid> CreateRecordAsync(HttpClient client)
    {
        var typeResp = await client.PostAsJsonAsync("/api/record-types/", new
        {
            shortCode = "t" + Guid.NewGuid().ToString("N")[..6],
            name = "Task",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null
        });
        typeResp.EnsureSuccessStatusCode();
        var type = await typeResp.Content.ReadFromJsonAsync<IdDto>();

        var recordResp = await client.PostAsJsonAsync("/api/records/", new
        {
            recordTypeId = type!.Id,
            name = "Item",
            status = (string?)null,
            dueDate = (string?)null,
            values = new { },
            assigneeIds = (Guid[]?)null
        });
        recordResp.EnsureSuccessStatusCode();
        var record = await recordResp.Content.ReadFromJsonAsync<IdDto>();
        return record!.Id;
    }

    private sealed record IdDto(Guid Id);
}
