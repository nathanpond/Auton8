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
        using var response = await client.GetAsync("service/repository/deployments?size=1000");
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
}
