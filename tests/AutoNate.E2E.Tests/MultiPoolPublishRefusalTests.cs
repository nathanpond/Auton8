using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A refused multi-pool publish leaves <b>nothing</b> in the engine (#578).
/// </summary>
/// <remarks>
/// <para>
/// The unit test proves the refusal; this proves there is nothing to roll back.
/// Asserting only the HTTP response cannot tell "refused" from "refused after
/// deploying" — and the defect being fixed is precisely a deployment Auton8 could
/// not see, so a response-only assertion would be blind to the thing that mattered.
/// </para>
/// </remarks>
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
            <bpmn:sequenceFlow id="bf" sourceRef="bs" targetRef="be" />
            <bpmn:endEvent id="be" name="End" />
          </bpmn:process>
          <bpmn:process id="{sellerKey}" name="Seller" isExecutable="true">
            <bpmn:startEvent id="ss" name="Start" />
            <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
            <bpmn:endEvent id="se" name="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_refused_multi_pool_publish_deploys_neither_pool()
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

        // Refused, and the reason names both pools.
        Assert.False(published.Ok, "A two-pool diagram must not publish.");
        var body = await published.TextAsync();
        Assert.Contains("Buyer", body, StringComparison.Ordinal);
        Assert.Contains("Seller", body, StringComparison.Ordinal);

        // AND NEITHER POOL REACHED THE ENGINE. This is the half a response check
        // cannot make: the defect was a definition deployed where Auton8 could
        // never see it, so "refused" and "refused after deploying" have to be
        // told apart at the engine.
        // #633. ASKED OF FLOWABLE DIRECTLY, and that is the correction.
        //
        // This block used to GET `/api/workflows/flowable/definitions`, a route
        // that exists nowhere in src/ -- so every iteration 404'd, hit
        // `if (!defs.Ok) continue;`, and asserted nothing. The comment above
        // called it "the half a response check cannot make" while making no
        // check at all. A skip-on-missing-route is silence dressed as tolerance.
        //
        // The Auton8-side check below cannot replace it either: a model row
        // staying unpublished cannot distinguish "nothing was deployed" from
        // "something was deployed and Auton8 forgot about it", which is exactly
        // the orphan this story exists to prevent.
        using var engine = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL")
                ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        foreach (var key in new[] { buyerKey, sellerKey })
        {
            var definitions = await engine.GetStringAsync(
                $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}");

            using var page = JsonDocument.Parse(definitions);

            // `total: 0` is the claim. Asserted on the engine's own count rather
            // than on the absence of a substring, so a differently-shaped payload
            // fails loudly instead of passing by not containing the key.
            Assert.Equal(0, page.RootElement.GetProperty("total").GetInt32());
        }

        // The model itself stayed unpublished, which is the Auton8-side shadow of
        // the same fact.
        var model = await api.GetAsync($"/api/workflows/{id}");
        Assert.True(model.Ok, $"Reading the model back failed: {model.Status}");
        using var doc = JsonDocument.Parse(await model.TextAsync());
        if (doc.RootElement.TryGetProperty("publishedVersionNumber", out var published_))
        {
            Assert.True(
                published_.ValueKind == JsonValueKind.Null,
                "A refused publish must leave the model unpublished.");
        }
    }
}
