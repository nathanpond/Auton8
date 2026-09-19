using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The DMN engine, against the engine (#106).
/// </summary>
/// <remarks>
/// <para>
/// #106's first question was whether decision execution costs a container. It
/// does not: the DMN engine ships enabled on the image this repo already builds
/// and digest-pins, mounted under <c>/flowable-rest/dmn-api/</c>. That finding is
/// recorded on the issue with its evidence; this is the half that keeps it true,
/// because a capability established once and never exercised again is a
/// capability that goes away silently on the next image bump.
/// </para>
/// <para>
/// **Why here and not in the backend suite.** These claims need a live engine,
/// and <c>TestTierDefinitionTests.The_backend_project_carries_no_service_trait</c>
/// fails the build if a <c>RequiresService</c> trait appears in
/// <c>AutoNate.Web.Tests</c> — slim runs that project whole, and GitHub stands up
/// no Flowable. So the engine half lives here, and
/// <c>FlowableDecisionClientTests</c> holds the client half in slim. That is the
/// same split as <c>PlacementDifferentialTests</c> / its live sibling: the
/// recorded shapes are checked on every merge, and the engine changing under the
/// recording is this file's job.
/// </para>
/// <para>
/// It talks to Flowable directly rather than through <c>FlowableDecisionClient</c>
/// because the E2E project does not reference <c>AutoNate.Web</c>. What it
/// therefore has to do is assert the exact request and response shapes the client
/// is written against — which it does, field by field, so a divergence fails here
/// rather than at the first real use.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
public sealed class DecisionEngineTests
{
    private static HttpClient Engine() => FlowableDeploymentSweep.CreateClient(
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

    /// <summary>
    /// Two inputs, one output, three rules — #106's demo, as a table.
    /// </summary>
    /// <remarks>
    /// The hit policy is FIRST, so at most one rule applies and the no-match case
    /// is an empty result rather than an error. <c>amount &gt; 10000</c> and
    /// <c>region = "EU"</c> are chosen so each rule is reachable independently and
    /// a fourth combination reaches none of them.
    /// </remarks>
    private static string DecisionTable(string key) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
                     id="defs_{key}"
                     name="{key}"
                     namespace="http://autonate.dev/decisions">
          <decision id="{key}" name="Routing">
            <decisionTable id="dt_{key}" hitPolicy="FIRST">
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
              <rule id="r_high">
                <inputEntry id="ie1"><text>&gt; 10000</text></inputEntry>
                <inputEntry id="ie2"><text></text></inputEntry>
                <outputEntry id="oe1"><text>"escalate"</text></outputEntry>
              </rule>
              <rule id="r_eu">
                <inputEntry id="ie3"><text></text></inputEntry>
                <inputEntry id="ie4"><text>"EU"</text></inputEntry>
                <outputEntry id="oe2"><text>"eu-desk"</text></outputEntry>
              </rule>
              <rule id="r_us">
                <inputEntry id="ie5"><text>&lt; 100</text></inputEntry>
                <inputEntry id="ie6"><text>"US"</text></inputEntry>
                <outputEntry id="oe3"><text>"auto-approve"</text></outputEntry>
              </rule>
            </decisionTable>
          </decision>
        </definitions>
        """;

    private static async Task<string> DeployAsync(HttpClient engine, string key)
    {
        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(DecisionTable(key), Encoding.UTF8, "application/xml"),
            "file",
            $"{key}.dmn");

        using var response = await engine.PostAsync("dmn-api/dmn-repository/deployments", content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Deploying the decision table failed: {(int)response.StatusCode} {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ExecuteAsync(
        HttpClient engine, string key, object amount, string region)
    {
        var request = new
        {
            decisionKey = key,
            inputVariables = new object[]
            {
                new { name = "amount", type = "double", value = amount },
                new { name = "region", type = "string", value = region }
            }
        };

        using var response = await engine.PostAsJsonAsync("dmn-api/dmn-rule/execute", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Evaluating '{key}' failed: {(int)response.StatusCode} {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string? RouteOf(JsonElement result)
    {
        var rows = result.GetProperty("resultVariables");
        if (rows.GetArrayLength() == 0) return null;

        foreach (var variable in rows[0].EnumerateArray())
        {
            if (variable.GetProperty("name").GetString() == "route")
            {
                return variable.GetProperty("value").GetString();
            }
        }

        return null;
    }

    [Fact]
    public async Task A_deployed_decision_table_evaluates_every_rule_and_reports_no_match_as_empty()
    {
        using var engine = Engine();
        var key = $"e2e_dmn_{Guid.NewGuid():n}"[..24];

        var deploymentId = await DeployAsync(engine, key);
        Assert.False(string.IsNullOrWhiteSpace(deploymentId));

        try
        {
            // The deployment produced a decision the repository service can find
            // by key. This is the step the client depends on -- the engine does
            // not error when a resource is deployed under a name whose extension
            // it does not parse, so "deployed" and "exists as a decision" are
            // genuinely different facts.
            using var listed = await engine.GetAsync(
                $"dmn-api/dmn-repository/decisions?key={Uri.EscapeDataString(key)}&latest=true");
            listed.EnsureSuccessStatusCode();
            using var listBody = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
            var decisions = listBody.RootElement.GetProperty("data");
            Assert.Equal(1, decisions.GetArrayLength());
            Assert.Equal(key, decisions[0].GetProperty("key").GetString());
            Assert.Equal(1, decisions[0].GetProperty("version").GetInt32());

            // Each rule, reached independently.
            Assert.Equal("escalate", RouteOf(await ExecuteAsync(engine, key, 50_000, "US")));
            Assert.Equal("eu-desk", RouteOf(await ExecuteAsync(engine, key, 500, "EU")));
            Assert.Equal("auto-approve", RouteOf(await ExecuteAsync(engine, key, 50, "US")));

            // And the complement: inputs no rule covers. This is not an error --
            // it is an empty result, and a caller has to be able to tell that
            // from a failed call. If the engine ever started throwing here, the
            // client's DecisionEvaluationResult.Matched would become a lie.
            var noMatch = await ExecuteAsync(engine, key, 500, "US");
            Assert.Equal(0, noMatch.GetProperty("resultVariables").GetArrayLength());
        }
        finally
        {
            using var deleted = await engine.DeleteAsync(
                $"dmn-api/dmn-repository/deployments/{deploymentId}?cascade=true");
            // Best effort: a failed cleanup must not fail the assertion above.
            _ = deleted.StatusCode;
        }
    }

    [Fact]
    public async Task An_unknown_decision_key_is_reported_as_a_caller_error()
    {
        using var engine = Engine();

        // Measured, not recalled. This one went the other way from the guess:
        // the engine classifies a caller's typo correctly, as a 400, rather than
        // as the 500 that would page someone. FlowableDecisionClient still
        // resolves the key first -- it needs the decision anyway, for the type
        // check -- but it does so for a better MESSAGE, not to repair a wrong
        // status, and this is what says so.
        var request = new
        {
            decisionKey = $"no_such_decision_{Guid.NewGuid():n}",
            inputVariables = Array.Empty<object>()
        };

        using var response = await engine.PostAsJsonAsync("dmn-api/dmn-rule/execute", request);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_type_mismatched_input_is_accepted_and_returns_an_empty_result()
    {
        using var engine = Engine();
        var key = $"e2e_dmn_{Guid.NewGuid():n}"[..24];
        var deploymentId = await DeployAsync(engine, key);

        try
        {
            // THE TRAP, pinned. `amount` is declared typeRef="number"; sending it
            // as a string does not fail. The engine answers 201 with an empty
            // result -- byte-identical to the legitimate no-match asserted above.
            //
            // So a caller who gets a type wrong is told "no rule applied", and a
            // routing decision silently becomes "do nothing". This is why
            // FlowableDecisionClient checks inputs against the decision's own
            // declared types before sending, and this test is the reason that
            // check is not superstition: if a later Flowable starts reporting the
            // mismatch itself, this fails and the client-side check can go.
            var request = new
            {
                decisionKey = key,
                inputVariables = new object[]
                {
                    new { name = "amount", type = "string", value = "not-a-number" },
                    new { name = "region", type = "string", value = "US" }
                }
            };

            using var response = await engine.PostAsJsonAsync("dmn-api/dmn-rule/execute", request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected the engine to ACCEPT a type-mismatched input. Got {(int)response.StatusCode}: {body}");

            using var document = JsonDocument.Parse(body);
            Assert.Equal(0, document.RootElement.GetProperty("resultVariables").GetArrayLength());
        }
        finally
        {
            using var deleted = await engine.DeleteAsync(
                $"dmn-api/dmn-repository/deployments/{deploymentId}?cascade=true");
            _ = deleted.StatusCode;
        }
    }
}
