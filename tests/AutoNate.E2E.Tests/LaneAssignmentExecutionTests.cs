using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A lane's group is who its user tasks are offered to (#171) — asserted from
/// BOTH sides, on the engine: a member of the group sees the task in their own
/// list, a non-member does not; a task with its own assignee keeps it and the
/// lane's group does NOT see it; a lane whose group was deleted is refused at
/// publish and nothing reaches the engine.
/// </summary>
[Collection(AutoNateE2ECollection.Name)]
[Trait("RequiresService", "Flowable")]
public sealed class LaneAssignmentExecutionTests : E2ETestBase
{
    public LaneAssignmentExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private const string Password = "Password123!";

    private static string Diagram(string key, string groupId, string ownAssignment = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_1" name="Us" processRef="{key}" />
          </bpmn:collaboration>
          <bpmn:process id="{key}" name="Us" isExecutable="true">
            <bpmn:laneSet id="LS_1">
              <bpmn:lane id="Lane_finance" name="Finance" autonate:groupId="{groupId}">
                <bpmn:flowNodeRef>start</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>approve</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>end</bpmn:flowNodeRef>
              </bpmn:lane>
            </bpmn:laneSet>
            <bpmn:startEvent id="start" />
            <bpmn:sequenceFlow id="f1" sourceRef="start" targetRef="approve" />
            <bpmn:userTask id="approve" name="Approve"{ownAssignment} />
            <bpmn:sequenceFlow id="f2" sourceRef="approve" targetRef="end" />
            <bpmn:endEvent id="end" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private sealed record Cast(string GroupId, UserDto Member, UserDto NonMember);

    private static async Task<Cast> CastAsync(IAPIRequestContext admin, string tag)
    {
        var seeder = new ApiSeeder(admin);
        var member = await seeder.CreateUserAsync(TestNames.Prefixed($"{tag}-member"), Password);
        var nonMember = await seeder.CreateUserAsync(TestNames.Prefixed($"{tag}-other"), Password);

        var group = await admin.PostAsync("/api/admin/groups",
            new APIRequestContextOptions { DataObject = new { name = TestNames.Prefixed($"{tag}-finance"), description = (string?)null } });
        Assert.True(group.Ok, await group.TextAsync());
        var groupId = JsonDocument.Parse(await group.TextAsync()).RootElement.GetProperty("id").GetString()!;

        var added = await admin.PostAsync($"/api/admin/groups/{groupId}/members",
            new APIRequestContextOptions { DataObject = new { userId = member.UserId } });
        Assert.True(added.Ok, await added.TextAsync());

        return new Cast(groupId, member, nonMember);
    }

    private static async Task<string> PublishAndStartAsync(IAPIRequestContext admin, string key, string xml)
    {
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(key);
        var created = await admin.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());
        var published = await admin.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");

        var started = await admin.PostAsync($"/api/workflows/{key}/start",
            new APIRequestContextOptions { DataObject = new { variables = new { } } });
        Assert.True(started.Ok, await started.TextAsync());
        return JsonDocument.Parse(await started.TextAsync()).RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The names of the tasks in this user's own list that belong to the instance.</summary>
    private async Task<List<string>> MyTasksOnAsync(UserDto user, string instanceId, bool expectSome)
    {
        await using var session = await NewSignedInAsAsync(user.Username, Password);
        var api = session.Page.APIRequest;
        // A member's task exists the moment the instance starts; the loop is for
        // the positive case only, so the negative one is not a race that passed.
        var deadline = DateTime.UtcNow.AddSeconds(expectSome ? 20 : 3);
        List<string> names = [];
        do
        {
            var response = await api.GetAsync("/api/tasks/assigned-to-me");
            Assert.True(response.Ok, $"{user.Username}'s task list failed: {response.Status} {await response.TextAsync()}");
            using var document = JsonDocument.Parse(await response.TextAsync());
            names = document.RootElement.EnumerateArray()
                .Where(t => t.GetProperty("processInstanceId").GetString() == instanceId)
                .Select(t => t.GetProperty("name").GetString() ?? "")
                .ToList();
            if (names.Count > 0 || !expectSome) break;
            await Task.Delay(500);
        } while (DateTime.UtcNow < deadline);
        return names;
    }

    [Fact]
    public async Task A_task_in_a_lane_is_offered_to_the_lanes_group_and_not_to_others()
    {
        await using var admin = await NewSignedInAsAdminAsync();
        var cast = await CastAsync(admin.Page.APIRequest, "lane");
        var key = $"lane{Guid.NewGuid():N}"[..20];

        var instanceId = await PublishAndStartAsync(admin.Page.APIRequest, key, Diagram(key, cast.GroupId));

        // THE MEMBER SEES IT: the lane's group reached the engine as the task's
        // candidate group, and the member's own list asks for their groups.
        Assert.Contains("Approve", await MyTasksOnAsync(cast.Member, instanceId, expectSome: true));

        // AND THE NON-MEMBER DOES NOT. Asserting only the first half would pass
        // against a task offered to everyone.
        Assert.Empty(await MyTasksOnAsync(cast.NonMember, instanceId, expectSome: false));

        // The engine's own record: the candidate group on the task IS the lane's group.
        using var engine = EngineClient();
        var links = await engine.GetStringAsync(
            $"service/runtime/tasks?processInstanceId={Uri.EscapeDataString(instanceId)}&candidateGroup={Uri.EscapeDataString(cast.GroupId)}");
        Assert.Equal(1, JsonDocument.Parse(links).RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_task_with_its_own_assignee_keeps_it_and_the_lanes_group_does_not_see_it()
    {
        await using var admin = await NewSignedInAsAdminAsync();
        var cast = await CastAsync(admin.Page.APIRequest, "override");
        var key = $"lano{Guid.NewGuid():N}"[..20];

        // The task's own assignee is the NON-member. If the lane won, the member
        // would see it; if the override were refused, publish would fail.
        var xml = Diagram(key, cast.GroupId, ownAssignment: $" flowable:assignee=\"{cast.NonMember.UserId}\"");
        var instanceId = await PublishAndStartAsync(admin.Page.APIRequest, key, xml);

        Assert.Contains("Approve", await MyTasksOnAsync(cast.NonMember, instanceId, expectSome: true));
        Assert.Empty(await MyTasksOnAsync(cast.Member, instanceId, expectSome: false));
    }

    [Fact]
    public async Task A_lane_whose_group_was_deleted_is_refused_at_publish_naming_the_lane_and_nothing_reaches_the_engine()
    {
        await using var admin = await NewSignedInAsAdminAsync();
        var api = admin.Page.APIRequest;
        var cast = await CastAsync(api, "gone");
        var deleted = await api.DeleteAsync($"/api/admin/groups/{cast.GroupId}");
        Assert.True(deleted.Ok, await deleted.TextAsync());

        var key = $"laned{Guid.NewGuid():N}"[..20];
        var xml = Diagram(key, cast.GroupId);
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.False(published.Ok, "A lane naming a deleted group must not publish.");
        var body = await published.TextAsync();
        Assert.Contains("Finance", body, StringComparison.Ordinal);
        Assert.Contains("no longer exists", body, StringComparison.Ordinal);

        using var engine = EngineClient();
        var definitions = await engine.GetStringAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}");
        Assert.Equal(0, JsonDocument.Parse(definitions).RootElement.GetProperty("total").GetInt32());
    }

    private static HttpClient EngineClient() => FlowableDeploymentSweep.CreateClient(
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");
}
