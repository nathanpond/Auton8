using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A collaboration publishes as ONE deployment holding a definition per pool —
/// and one that could not deploy as a set leaves <b>nothing</b> in the engine
/// (#169, inverting #578).
/// </summary>
/// <remarks>
/// <para>
/// #578's spec proved a two-pool diagram was refused and deployed neither pool.
/// That refusal existed because only one definition survived a publish. #169
/// reads the set back by deployment id, so the same diagram now deploys both —
/// and the spec that proved the refusal becomes the spec that proves the
/// capability, keeping its engine-side half: the claim is made against
/// Flowable's own counts, not against the HTTP response.
/// </para>
/// <para>
/// The refusal shape is kept for what IS refused now: a pool whose process is
/// missing. A response check cannot tell "refused" from "refused after
/// deploying", which is the orphan this story exists to prevent, so the
/// engine is asked directly there too.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
[Trait("RequiresService", "Flowable")]
public sealed class MultiPoolPublishRefusalTests : E2ETestBase
{
    public MultiPoolPublishRefusalTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private static string TwoPools(string buyerKey, string sellerKey) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_buyer" name="Buyer" processRef="{buyerKey}" />
            <bpmn:participant id="P_seller" name="Seller" processRef="{sellerKey}" />
          </bpmn:collaboration>
          <bpmn:process id="{buyerKey}" name="Buyer" isExecutable="true">
            <bpmn:startEvent id="bs" name="Start" />
            <bpmn:sequenceFlow id="bf" sourceRef="bs" targetRef="bt" />
            <bpmn:userTask id="bt" name="Buy" />
            <bpmn:sequenceFlow id="bf2" sourceRef="bt" targetRef="be" />
            <bpmn:endEvent id="be" name="End" />
          </bpmn:process>
          <bpmn:process id="{sellerKey}" name="Seller" isExecutable="true">
            <bpmn:startEvent id="ss" name="Start" />
            <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="st" />
            <bpmn:userTask id="st" name="Sell" />
            <bpmn:sequenceFlow id="sf2" sourceRef="st" targetRef="se" />
            <bpmn:endEvent id="se" name="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// The inversion of #578's <c>A_refused_multi_pool_publish_deploys_neither_pool</c>.
    /// </summary>
    [Fact]
    public async Task A_two_pool_publish_deploys_both_pools_under_one_deployment()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var buyerKey = $"buy{Guid.NewGuid():N}"[..20];
        var sellerKey = $"sel{Guid.NewGuid():N}"[..20];
        var xml = TwoPools(buyerKey, sellerKey);

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(buyerKey), processKey = buyerKey, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(buyerKey), processKey = buyerKey, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"A two-pool diagram must publish now: {published.Status} {await published.TextAsync()}");

        // BOTH POOLS REACHED THE ENGINE, UNDER ONE DEPLOYMENT. Asked of Flowable
        // directly, on its own counts (#633's correction, kept).
        using var engine = EngineClient();
        var deploymentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { buyerKey, sellerKey })
        {
            var definitions = await engine.GetStringAsync(
                $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}");
            using var page = JsonDocument.Parse(definitions);
            Assert.Equal(1, page.RootElement.GetProperty("total").GetInt32());
            deploymentIds.Add(page.RootElement.GetProperty("data")[0].GetProperty("deploymentId").GetString()!);
        }

        // One deployment id across both keys is the shared version identity the
        // AC asks for; two would be two publishes, not a set.
        Assert.Single(deploymentIds);

        // The workflow list shows ONE workflow for the diagram — the author
        // authored one thing.
        var list = await api.GetAsync("/api/workflows/");
        Assert.True(list.Ok, await list.TextAsync());
        using var workflows = JsonDocument.Parse(await list.TextAsync());
        var matching = workflows.RootElement.EnumerateArray()
            .Count(w => w.TryGetProperty("id", out var wid) && wid.GetString() == id.ToString());
        Assert.Equal(1, matching);

        // The primary starts; the counterparty's definition carries the pool's
        // name, which is how an execution says which participant it belongs to.
        var started = await api.PostAsync($"/api/workflows/{buyerKey}/start",
            new APIRequestContextOptions { DataObject = new { name = TestNames.Prefixed("run") } });
        Assert.True(started.Ok, await started.TextAsync());

        var sellerDefinition = await engine.GetStringAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(sellerKey)}");
        using var sellerPage = JsonDocument.Parse(sellerDefinition);
        Assert.Equal("Seller", sellerPage.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// Republishing advances EVERY definition in the set, including a pool whose
    /// contents did not change (#169).
    /// </summary>
    /// <remarks>
    /// Asserted on the UNCHANGED pool specifically. That is the property that
    /// makes the atomic model worth its cost: a message flow between the pools is
    /// guaranteed a compatible counterpart because they shipped together. A
    /// per-pool versioning would leave Seller at v1 here and pass a Buyer-only
    /// assertion.
    /// </remarks>
    [Fact]
    public async Task Republishing_advances_the_unchanged_pool_too()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var buyerKey = $"buy{Guid.NewGuid():N}"[..20];
        var sellerKey = $"sel{Guid.NewGuid():N}"[..20];
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(buyerKey);

        var v1 = TwoPools(buyerKey, sellerKey);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = buyerKey, bpmnXml = v1 }
        });
        Assert.True(created.Ok, await created.TextAsync());
        var first = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = buyerKey, bpmnXml = v1 }
        });
        Assert.True(first.Ok, await first.TextAsync());

        // Change only the Buyer pool. Seller's XML is byte-identical.
        var v2 = v1.Replace("name=\"Buy\"", "name=\"Buy now\"", StringComparison.Ordinal);
        Assert.NotEqual(v1, v2);
        var second = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = buyerKey, bpmnXml = v2 }
        });
        Assert.True(second.Ok, await second.TextAsync());

        using var engine = EngineClient();
        var seller = await engine.GetStringAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(sellerKey)}&latest=true");
        using var sellerPage = JsonDocument.Parse(seller);
        Assert.Equal(2, sellerPage.RootElement.GetProperty("data")[0].GetProperty("version").GetInt32());

        var buyer = await engine.GetStringAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(buyerKey)}&latest=true");
        using var buyerPage = JsonDocument.Parse(buyer);
        Assert.Equal(2, buyerPage.RootElement.GetProperty("data")[0].GetProperty("version").GetInt32());

        // Same deployment id for both latest definitions: the set moved as one.
        Assert.Equal(
            sellerPage.RootElement.GetProperty("data")[0].GetProperty("deploymentId").GetString(),
            buyerPage.RootElement.GetProperty("data")[0].GetProperty("deploymentId").GetString());
    }

    /// <summary>
    /// A collaboration that could not deploy as a set leaves nothing in the
    /// engine (#169) — the refusal shape #578 established, kept for what is
    /// refused now.
    /// </summary>
    [Fact]
    public async Task A_pool_whose_process_is_missing_is_refused_and_neither_pool_reaches_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var buyerKey = $"buy{Guid.NewGuid():N}"[..20];
        var sellerKey = $"sel{Guid.NewGuid():N}"[..20];
        // Seller's participant points at a process that is not in the diagram.
        var xml = TwoPools(buyerKey, sellerKey)
            .Replace($"processRef=\"{sellerKey}\"", "processRef=\"nowhere\"", StringComparison.Ordinal);

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(buyerKey), processKey = buyerKey, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(buyerKey), processKey = buyerKey, bpmnXml = xml }
        });
        Assert.False(published.Ok, "A collaboration with a dangling processRef must not publish.");
        var body = await published.TextAsync();
        Assert.Contains("Seller", body, StringComparison.Ordinal);
        Assert.Contains("nowhere", body, StringComparison.Ordinal);

        // AND NEITHER POOL REACHED THE ENGINE.
        using var engine = EngineClient();
        foreach (var key in new[] { buyerKey, sellerKey })
        {
            var definitions = await engine.GetStringAsync(
                $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}");
            using var page = JsonDocument.Parse(definitions);
            Assert.Equal(0, page.RootElement.GetProperty("total").GetInt32());
        }

        var model = await api.GetAsync($"/api/workflows/{id}");
        Assert.True(model.Ok, $"Reading the model back failed: {model.Status}");
        using var doc = JsonDocument.Parse(await model.TextAsync());
        if (doc.RootElement.TryGetProperty("publishedVersionNumber", out var publishedVersion))
        {
            Assert.True(publishedVersion.ValueKind == JsonValueKind.Null,
                "A refused publish must leave the model unpublished.");
        }
    }

    /// <summary>
    /// #645. A pool drawn only to show a counterparty -- nothing in it -- deploys
    /// as NOTHING through /publish, the path every caller uses. Before #645 that
    /// held only on /prepare, and the story's own oracle cell had left its empty
    /// counterparty in the engine as a live definition.
    /// </summary>
    [Fact]
    public async Task A_pool_with_nothing_in_it_deploys_as_nothing()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var buyerKey = $"buy{Guid.NewGuid():N}"[..20];
        var emptyKey = $"emp{Guid.NewGuid():N}"[..20];
        // Everything between the Seller process's open and close tags goes:
        // a pool with nothing in it.
        var xml = System.Text.RegularExpressions.Regex.Replace(
            TwoPools(buyerKey, emptyKey),
            $"(<bpmn:process id=\"{emptyKey}\"[^>]*>).*?(</bpmn:process>)",
            "$1$2",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.DoesNotContain("id=\"st\"", xml, StringComparison.Ordinal);

        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(buyerKey);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = buyerKey, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());
        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = buyerKey, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"A diagram with an empty counterparty must publish: {published.Status} {await published.TextAsync()}");

        using var engine = EngineClient();
        var buyer = await engine.GetStringAsync($"service/repository/process-definitions?key={Uri.EscapeDataString(buyerKey)}");
        Assert.Equal(1, JsonDocument.Parse(buyer).RootElement.GetProperty("total").GetInt32());
        // AND THE EMPTY POOL IS NOT A DEFINITION. Asked of the engine, by key.
        var empty = await engine.GetStringAsync($"service/repository/process-definitions?key={Uri.EscapeDataString(emptyKey)}");
        Assert.Equal(0, JsonDocument.Parse(empty).RootElement.GetProperty("total").GetInt32());
    }

    private static HttpClient EngineClient() => FlowableDeploymentSweep.CreateClient(
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");
}
