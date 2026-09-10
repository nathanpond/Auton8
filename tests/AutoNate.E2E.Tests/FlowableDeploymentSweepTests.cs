using AutoNate.E2E.Tests.Support;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The Flowable sweep removes the suite's deployments and nobody else's (#214).
/// </summary>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class FlowableDeploymentSweepTests : E2ETestBase
{
    public FlowableDeploymentSweepTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task It_removes_suite_deployments_and_leaves_everything_else()
    {
        // The criterion that matters: a prefix sweep against a SHARED engine can
        // delete a developer's real work. The engine held deployments named
        // `autonate`, `default`, `car` and `account` alongside 303 from the suite.
        using var client = FlowableDeploymentSweep.CreateClient(
            "http://localhost:8080/flowable-rest", "rest-admin", "test");

        var mine = $"e2e-sweep-probe-{Guid.NewGuid():N}"[..28];
        var theirs = $"someones-real-work-{Guid.NewGuid():N}"[..28];

        await DeployAsync(client, mine);
        await DeployAsync(client, theirs);

        try
        {
            Assert.True(await ExistsAsync(client, mine), "Precondition: the suite deployment was not created.");
            Assert.True(await ExistsAsync(client, theirs), "Precondition: the other deployment was not created.");

            var deleted = await FlowableDeploymentSweep.SweepAsync(client);

            Assert.True(deleted > 0, "The sweep reported deleting nothing.");
            Assert.False(await ExistsAsync(client, mine), "A suite deployment survived the sweep.");
            // The half that makes this safe to run on a shared engine.
            Assert.True(await ExistsAsync(client, theirs),
                "The sweep deleted a deployment outside the suite's naming convention.");
        }
        finally
        {
            await DeleteByNameAsync(client, theirs);
        }
    }

    private static async Task DeployAsync(HttpClient client, string name)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         targetNamespace="http://autonate.dev/workflows">
              <process id="p{Guid.NewGuid():N}" isExecutable="true"><startEvent id="s" /></process>
            </definitions>
            """;

        using var content = new MultipartFormDataContent();
        var file = new StringContent(xml);
        content.Add(file, "file", $"{name}.bpmn20.xml");
        using var response = await client.PostAsync("service/repository/deployments", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<bool> ExistsAsync(HttpClient client, string name) =>
        await FindIdAsync(client, name) is not null;

    private static async Task<string?> FindIdAsync(HttpClient client, string name)
    {
        // Newest first: the shared engine holds more deployments than one page,
        // so an unsorted query silently misses a deployment made seconds ago.
        using var response = await client.GetAsync(
            "service/repository/deployments?size=1000&sort=deployTime&order=desc");
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var element in document.RootElement.GetProperty("data").EnumerateArray())
        {
            if (element.TryGetProperty("name", out var n) && n.GetString() == name)
            {
                return element.GetProperty("id").GetString();
            }
        }
        return null;
    }

    private static async Task DeleteByNameAsync(HttpClient client, string name)
    {
        if (await FindIdAsync(client, name) is { } id)
        {
            using var _ = await client.DeleteAsync($"service/repository/deployments/{id}?cascade=true");
        }
    }

    // #257, second pass. The claim this file most needs to make, and the one it
    // was not making: that the name the APP produces is a name the sweep matches.
    //
    // Both tests above deploy their fixture straight to the engine, choosing the
    // name themselves. That is how the original defect survived its own test for
    // the whole of M4 — the suite published through FlowableClient, which named
    // deployments after the process key (`adh…`, `cgx…`), while the sweep looked
    // for `e2e-`. Two conventions, no test comparing them.
    //
    // This one publishes through the real endpoint and then asks the sweep to
    // find it. It fails if the prefix option is dropped, if the fixture stops
    // setting it, or if the sweep's constant drifts from the fixture's.
    [Fact]
    public async Task The_sweep_matches_what_the_app_actually_deploys()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"swp{Guid.NewGuid():N}"[..20];
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(key);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{key}" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
                <bpmn:userTask id="t" name="Wait" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var created = await api.PostAsync("/api/workflows/", new Microsoft.Playwright.APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new Microsoft.Playwright.APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");

        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // The name the app chose, not one this test chose.
        var deployedAs = FlowableDeploymentSweep.SuiteDeploymentPrefix + key;
        Assert.True(
            await ExistsAsync(client, deployedAs),
            $"The app published as something other than '{deployedAs}'. The sweep " +
            "matches by name, so a mismatch here is the sweep silently removing " +
            "nothing -- which is exactly what #257 was.");

        var deleted = await FlowableDeploymentSweep.SweepAsync(client);
        Assert.True(deleted > 0, "The sweep reported deleting nothing.");
        Assert.False(await ExistsAsync(client, deployedAs),
            "The sweep did not remove a deployment the app itself published.");
    }

    [Fact]
    public async Task The_sweep_leaves_a_realistically_named_deployment_alone_at_any_age()
    {
        // The complement of the above, and the property the first #257 fix broke:
        // it swept by age, so a developer's own three-hour-old work went with the
        // orphans while this file's own remarks promised it would not.
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // Named the way FlowableClient names a PRODUCTION model: no prefix.
        var key = $"cgx{Guid.NewGuid():N}"[..20];
        await DeployAsync(client, key);

        try
        {
            Assert.True(await ExistsAsync(client, key),
                "the fixture did not deploy, so the assertion below would be vacuous");

            await FlowableDeploymentSweep.SweepAsync(client);

            Assert.True(await ExistsAsync(client, key),
                "The sweep removed a deployment outside the suite's naming " +
                "convention. On a shared engine that is a developer's real work.");
        }
        finally
        {
            await DeleteByNameAsync(client, key);
        }
    }
}
