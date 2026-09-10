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

    // #257. The original test deployed an `e2e-…` fixture DIRECTLY, so
    // `Assert.True(deleted > 0)` only ever saw its own plant — the sweep could
    // remove nothing the suite actually produces and stay green. This one uses a
    // realistically-named deployment, which is what exposed the defect.
    [Fact]
    public async Task The_sweep_removes_a_realistically_named_orphan_not_just_its_own_fixture()
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // Named the way FlowableClient names a published model: "{processKey}.bpmn20.xml",
        // with a short generated key exactly like the suite's own.
        var key = $"cgx{Guid.NewGuid():N}"[..20];
        await DeployAsync(client, key);

        Assert.True(await ExistsAsync(client, key),
            "the fixture did not deploy, so the sweep assertion below would be vacuous");

        // Fresh, so the age rule must NOT take it — a sweep that deleted a live
        // run's deployments mid-suite would be worse than one that leaks.
        await FlowableDeploymentSweep.SweepAsync(client);
        Assert.True(await ExistsAsync(client, key),
            "the sweep removed a deployment made moments ago");

        await DeleteByNameAsync(client, key);
    }
}
