using System.Net;
using System.Text.Json;
using AutoNate.Web.Services.Flowable;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The DMN client, in slim (#106).
/// </summary>
/// <remarks>
/// <para>
/// The engine half of #106 lives in <c>DecisionEngineTests</c>, which is
/// <c>RequiresService=Flowable</c> and full-local only. This half needs no
/// engine: it pins what the client SENDS and how it reads what comes back, so a
/// regression in either fails the merge gate rather than waiting for a
/// full-local run. Same split as <c>PlacementDifferentialTests</c> and its live
/// sibling, for the same reason.
/// </para>
/// <para>
/// The stubbed shapes are not invented. Every response body here was copied from
/// a real Flowable 8.0.0 answer recorded while building this — the deployment's
/// <c>id</c>, the decision list's envelope, <c>resultVariables</c> as a list of
/// lists, and the <c>resourcedata</c> XML with its DMN 1.3 namespace.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FlowableDecisionClientTests
{
    private const string BaseAddress = "http://flowable.test/flowable-rest/";

    private const string DecisionXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
                     id="defs_routing" name="routing" namespace="http://autonate.dev/decisions">
          <decision id="routing" name="Routing">
            <decisionTable id="dt" hitPolicy="FIRST">
              <input id="in_amount" label="amount">
                <inputExpression id="ie_amount" typeRef="number">
                  <text>amount</text>
                </inputExpression>
              </input>
              <input id="in_region" label="region">
                <inputExpression id="ie_region" typeRef="string">
                  <text>region</text>
                </inputExpression>
              </input>
              <output id="out_route" label="route" name="route" typeRef="string" />
            </decisionTable>
          </decision>
        </definitions>
        """;

    private static (FlowableDecisionClient client, StubHttpMessageHandler stub) CreateClient()
    {
        var stub = new StubHttpMessageHandler();
        var http = new HttpClient(stub) { BaseAddress = new Uri(BaseAddress) };
        return (new FlowableDecisionClient(http, new MemoryCache(new MemoryCacheOptions())), stub);
    }

    /// <summary>Registers the lookup + definition reads every evaluation makes.</summary>
    private static void StubDecision(StubHttpMessageHandler stub, string key = "routing")
    {
        stub.WhenJson(HttpMethod.Get, "dmn-api/dmn-repository/decisions?key=", new
        {
            data = new[] { new { id = "dec-1", key, name = "Routing", version = 1, deploymentId = "dep-1" } },
            total = 1
        });
        stub.When(HttpMethod.Get, "decisions/dec-1/resourcedata",
            _ => StubHttpMessageHandler.TextResponse(DecisionXml, mediaType: "application/xml"));
    }

    private static StubHttpMessageHandler.RecordedRequest RequestFor(
        StubHttpMessageHandler stub, HttpMethod method, string path) =>
        Assert.Single(stub.Requests, request =>
            request.Method == method && request.Url.Contains(path, StringComparison.Ordinal));

    // ── deploy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeployDecisionAsync_PostsToTheDmnRepository_WithADmnFileName()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-repository/deployments",
            new { id = "dep-1", name = "routing" });
        StubDecision(stub);

        var info = await client.DeployDecisionAsync("routing", DecisionXml);

        var request = RequestFor(stub, HttpMethod.Post, "dmn-api/dmn-repository/deployments");

        // The path prefix, pinned. `dmn-repository/...` without the `dmn-api`
        // segment is a 404 on the real engine, and it is the single detail most
        // likely to be "corrected" by someone who knows the BPMN routes.
        Assert.Contains("dmn-api/dmn-repository/deployments", request.Url, StringComparison.Ordinal);

        // And the file name, which is load-bearing rather than cosmetic: the
        // engine dispatches on the EXTENSION. A resource that does not end .dmn
        // is accepted into the deployment and quietly not parsed as a decision,
        // so this deploy would return 200 with no decision afterwards.
        Assert.Contains("routing.dmn", request.Body ?? string.Empty, StringComparison.Ordinal);

        Assert.Equal("dep-1", info.DeploymentId);
        Assert.Equal("routing", info.DecisionKey);
        Assert.Equal(1, info.DecisionVersion);
    }

    [Fact]
    public async Task DeployDecisionAsync_FailsWhenTheDeploymentProducedNoDecision()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-repository/deployments", new { id = "dep-1" });
        // The complement of the fact above: the engine accepted the resource and
        // no decision exists. A client that returned the deployment id here would
        // report success for a deployment that decided nothing.
        stub.WhenJson(HttpMethod.Get, "dmn-api/dmn-repository/decisions?key=",
            new { data = Array.Empty<object>(), total = 0 });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DeployDecisionAsync("routing", DecisionXml));

        Assert.Contains("routing", error.Message, StringComparison.Ordinal);
    }

    // ── evaluate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_SendsTheDecisionKeyAndTypedInputs_AndReadsTheOutputs()
    {
        var (client, stub) = CreateClient();
        StubDecision(stub);
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-rule/execute", new
        {
            resultVariables = new[]
            {
                new[] { new { name = "route", type = "string", value = "escalate" } }
            }
        }, HttpStatusCode.Created);

        var result = await client.EvaluateAsync(
            "routing",
            new Dictionary<string, object?> { ["amount"] = 50_000d, ["region"] = "US" });

        Assert.True(result.Matched);
        Assert.Equal("escalate", result.First!["route"]);

        var request = RequestFor(stub, HttpMethod.Post, "dmn-api/dmn-rule/execute");
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("routing", body.RootElement.GetProperty("decisionKey").GetString());

        var variables = body.RootElement.GetProperty("inputVariables");
        Assert.Equal(2, variables.GetArrayLength());

        // The engine binds by type name, so a value sent under the wrong one is
        // the trap this client exists to close. Both are asserted, not just the
        // interesting one: a mapping that answered "string" for everything would
        // pass a single-input check.
        var amount = variables.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "amount");
        Assert.Equal("double", amount.GetProperty("type").GetString());
        var region = variables.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "region");
        Assert.Equal("string", region.GetProperty("type").GetString());
    }

    [Fact]
    public async Task EvaluateAsync_TreatsNoMatchedRuleAsAnEmptyResult_NotAFailure()
    {
        var (client, stub) = CreateClient();
        StubDecision(stub);
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-rule/execute",
            new { resultVariables = Array.Empty<object[]>() }, HttpStatusCode.Created);

        var result = await client.EvaluateAsync(
            "routing",
            new Dictionary<string, object?> { ["amount"] = 500d, ["region"] = "US" });

        // A hit policy may legitimately match nothing, and a caller has to tell
        // that from a failed call. Throwing here would make the two the same.
        Assert.False(result.Matched);
        Assert.Null(result.First);
        Assert.Empty(result.Outputs);
    }

    // ── the three failure modes, kept apart ─────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ReportsAnUnknownDecisionKeyAsNotFound()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "dmn-api/dmn-repository/decisions?key=",
            new { data = Array.Empty<object>(), total = 0 });

        var error = await Assert.ThrowsAsync<FlowableRequestException>(
            () => client.EvaluateAsync("nope", new Dictionary<string, object?>()));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.True(error.IsCallerError);
        Assert.Contains("nope", error.Message, StringComparison.Ordinal);

        // And it never reached the rule service: a client that sent the call
        // anyway would get the engine's own 400, which is right in class and
        // says nothing about which key was missing.
        Assert.DoesNotContain(stub.Requests, r => r.Url.Contains("dmn-rule/execute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_RefusesAnInputWhoseTypeTheTableCannotUse()
    {
        var (client, stub) = CreateClient();
        StubDecision(stub);
        // Deliberately registered, so the assertion below is about the client
        // choosing not to call it rather than about the route being absent.
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-rule/execute",
            new { resultVariables = Array.Empty<object[]>() }, HttpStatusCode.Created);

        var error = await Assert.ThrowsAsync<FlowableRequestException>(
            () => client.EvaluateAsync(
                "routing",
                // `amount` is declared typeRef="number" in the table above.
                new Dictionary<string, object?> { ["amount"] = "not-a-number", ["region"] = "US" }));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("amount", error.Message, StringComparison.Ordinal);
        Assert.Contains("number", error.Message, StringComparison.Ordinal);

        // The whole point. DecisionEngineTests pins that the engine ACCEPTS this
        // and answers 201 with an empty result -- indistinguishable from a
        // legitimate no-match. Letting the call through would turn a caller's
        // type error into "no rule applied", which for a routing decision reads
        // as "do nothing".
        Assert.DoesNotContain(stub.Requests, r => r.Url.Contains("dmn-rule/execute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_PassesTheEnginesOwnFailureThroughWithItsStatus()
    {
        var (client, stub) = CreateClient();
        StubDecision(stub);
        stub.WhenStatus(HttpMethod.Post, "dmn-api/dmn-rule/execute",
            HttpStatusCode.ServiceUnavailable, "engine is down");

        var error = await Assert.ThrowsAsync<FlowableRequestException>(
            () => client.EvaluateAsync(
                "routing",
                new Dictionary<string, object?> { ["amount"] = 1d, ["region"] = "US" }));

        // The third mode: the engine itself failed. 5xx, so NOT a caller error --
        // which is what keeps it apart from the two above when something decides
        // whether to retry or to tell the user they got it wrong.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.False(error.IsCallerError);
    }

    [Fact]
    public async Task EvaluateAsync_AllowsAnInputTheTableDoesNotDeclare()
    {
        var (client, stub) = CreateClient();
        StubDecision(stub);
        stub.WhenJson(HttpMethod.Post, "dmn-api/dmn-rule/execute",
            new { resultVariables = Array.Empty<object[]>() }, HttpStatusCode.Created);

        // The complement of the refusal: the check must not become a whitelist.
        // A caller may legitimately pass variables the table ignores, and
        // refusing those would break every real call that reuses a process
        // variable bag.
        var result = await client.EvaluateAsync(
            "routing",
            new Dictionary<string, object?>
            {
                ["amount"] = 1d,
                ["region"] = "US",
                ["unrelated"] = "whatever"
            });

        Assert.False(result.Matched);
        Assert.Contains(stub.Requests, r => r.Url.Contains("dmn-rule/execute", StringComparison.Ordinal));
    }
}
