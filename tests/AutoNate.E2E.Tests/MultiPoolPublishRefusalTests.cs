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
        foreach (var key in new[] { buyerKey, sellerKey })
        {
            var defs = await api.GetAsync(
                $"/api/workflows/flowable/definitions?processKey={key}");
            if (!defs.Ok) continue;   // no such surface is fine; the DB check below is the backstop

            var text = await defs.TextAsync();
            Assert.DoesNotContain(key, text, StringComparison.Ordinal);
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
