using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A process branches on what a decision table decided (#111).
/// </summary>
/// <remarks>
/// <para>
/// The AC is explicit about why the gateway is the assertion: <i>"asserted end to
/// end, because 'the table was evaluated' and 'the process did something different
/// as a result' are different claims and only the second is the feature."</i>
/// </para>
/// <para>
/// So each run asserts <b>which user task the process is now waiting on</b>, and
/// the two runs differ only in the amount they start with. A test asserting that
/// the output variable was set would pass against a process whose gateway ignored
/// it.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class BusinessRuleTaskExecutionTests : E2ETestBase
{
    public BusinessRuleTaskExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_table_decides_and_a_gateway_after_it_takes_the_path_it_chose()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // ── A published decision table, authored through Auton8's own API.
        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var tableId = await PublishTableAsync(api, decisionKey);

        // ── A process whose business rule task points at it.
        var processKey = $"e2e_brt_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, processKey, Diagram(processKey, decisionKey));

        // Big amount -> the table says "escalate" -> the gateway takes that path.
        var escalated = await StartAsync(api, processKey, amount: 50_000);
        await EventuallyActivityAsync(api, escalated, "escalate_task",
            "the escalate path to be taken");

        // Small amount, SAME process definition -> "auto" -> the other path.
        //
        // This is the pair that makes it a feature rather than a coincidence. One
        // run alone passes against a gateway with a stuck default flow.
        var auto = await StartAsync(api, processKey, amount: 50);
        await EventuallyActivityAsync(api, auto, "auto_task", "the auto path to be taken");

        // And the complement: neither run wandered onto the other's path.
        Assert.DoesNotContain("auto_task", await ActivityIdsAsync(api, escalated));
        Assert.DoesNotContain("escalate_task", await ActivityIdsAsync(api, auto));

        await api.DeleteAsync($"/api/decision-tables/{tableId}");
    }

    /// <summary>
    /// A deployed process keeps deciding the way the table decided when it was
    /// deployed, and a process deployed afterwards picks the new answer up (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both directions, because one alone proves nothing.</b> "The old process
    /// still says auto" is satisfied by a binding that is stuck on version 1
    /// forever, which would make republishing a table useless; "the new process
    /// says escalate" is satisfied by no binding at all, which is the defect.
    /// Only the pair distinguishes binding from either failure.
    /// </para>
    /// <para>
    /// The two runs use the SAME amount against the SAME diagram. The only thing
    /// that differs is which version of the table each process definition was
    /// deployed against, so the branch each one takes is the binding and nothing
    /// else.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Republishing_a_table_does_not_change_an_already_deployed_process()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // v1: 50 is well under the threshold, so it routes `auto`.
        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var tableId = await PublishTableAsync(api, decisionKey, threshold: 10_000);

        var before = $"e2e_brv1_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, before, Diagram(before, decisionKey));

        // Proven against v1 BEFORE the republish. Without this the test cannot
        // tell "bound to v1" from "this process was always going to say auto".
        var first = await StartAsync(api, before, amount: 50);
        await EventuallyActivityAsync(api, first, "auto_task", "the auto path under v1");

        // v2 inverts the answer for that same amount: 50 is now over the line.
        await RepublishTableAsync(api, tableId, decisionKey, threshold: 10);

        // ── Direction 1: the process deployed against v1 still decides v1's way.
        var again = await StartAsync(api, before, amount: 50);
        await EventuallyActivityAsync(api, again, "auto_task",
            "the already-deployed definition to keep deciding the way it did when it was deployed");
        Assert.DoesNotContain("escalate_task", await ActivityIdsAsync(api, again));

        // ── Direction 2: a process deployed now reads v2.
        var after = $"e2e_brv2_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, after, Diagram(after, decisionKey));

        var fresh = await StartAsync(api, after, amount: 50);
        await EventuallyActivityAsync(api, fresh, "escalate_task",
            "a freshly deployed definition to read the republished table");
        Assert.DoesNotContain("auto_task", await ActivityIdsAsync(api, fresh));

        await api.DeleteAsync($"/api/decision-tables/{tableId}");
    }

    [Fact]
    public async Task A_business_rule_task_with_no_table_is_refused_at_publish()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var processKey = $"e2e_brx_{Guid.NewGuid():N}"[..22];
        var refused = await TryPublishWorkflowAsync(api, processKey, UnconfiguredDiagram(processKey));

        Assert.False(refused.Ok,
            "A business rule task with no decision table should be refused at publish.");

        var body = await refused.TextAsync();
        Assert.Contains("decision table", body, StringComparison.Ordinal);
        // Named, so an author knows which element to open.
        Assert.Contains("Route the invoice", body, StringComparison.Ordinal);

        // And it never reached the engine. Without this, an implementation that
        // deployed first and complained after would still pass -- and the engine's
        // own refusal for this element is a 500 naming a Java class
        // (NoClassDefFoundError org/kie/api/runtime/rule/AgendaFilter), which is
        // exactly the message an author must never be handed.
        Assert.DoesNotContain("org/kie/api", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AgendaFilter", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reference to a table that has been deleted is refused at publish (#111).
    /// </summary>
    /// <remarks>
    /// The AC names this case separately from "no table" because it is the one an
    /// author cannot see coming: the diagram was valid when it was drawn, and the
    /// thing it points at went away somewhere else. Flowable accepts such a
    /// process happily and fails only when an instance reaches the step, which is
    /// the silent-no-op this milestone exists to end.
    /// </remarks>
    [Fact]
    public async Task A_business_rule_task_whose_table_was_deleted_is_refused_at_publish()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var tableId = await PublishTableAsync(api, decisionKey);

        // Published while the table existed: the diagram itself is fine, which is
        // what makes the refusal below about the reference and not the drawing.
        var before = $"e2e_brd1_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, before, Diagram(before, decisionKey));

        var deleted = await api.DeleteAsync($"/api/decision-tables/{tableId}");
        Assert.True(deleted.Ok, $"Deleting the table failed: {deleted.Status} {await deleted.TextAsync()}");

        var after = $"e2e_brd2_{Guid.NewGuid():N}"[..22];
        var refused = await TryPublishWorkflowAsync(api, after, Diagram(after, decisionKey));

        Assert.False(refused.Ok,
            "Publishing a process whose decision table has been deleted should be refused.");

        var body = await refused.TextAsync();
        // The key, so the author knows WHICH reference, and the element name, so
        // they know where to click.
        Assert.Contains(decisionKey, body, StringComparison.Ordinal);
        Assert.Contains("Route the invoice", body, StringComparison.Ordinal);

        // And the engine never saw it. Asked of Flowable rather than of Auton8: a
        // 400 alone would pass for an implementation that deployed first and
        // complained afterwards, and only the engine can say whether it did.
        Assert.False(
            await EngineHasDefinitionAsync(after),
            "The refused process reached the engine anyway, so the refusal happened after "
            + "the deployment rather than instead of it.");
    }

    /// <summary>
    /// An evaluation that fails at run time shows up on the execution, naming the
    /// step and carrying the engine's reason (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured before it was built.</b> A DMN task whose expression fails
    /// throws out of whatever transaction reaches it. Synchronously that meant
    /// HTTP 500 on the start call, the transaction rolled back, and no instance
    /// and no history left behind — the author gets an error and nobody else can
    /// see that anything happened, which is the AC's "silent stall" exactly.
    /// Publish therefore stamps <c>flowable:async="true"</c>, and the failure
    /// becomes a job an operator can find.
    /// </para>
    /// <para>
    /// The cell that fails is an output expression, not a bad type: a type
    /// mismatch is ACCEPTED by this engine (#106) and a failing FEEL expression is
    /// accepted by the validator on purpose — <i>"an expression that parses and
    /// means something silly is the author's business"</i>. So this is the failure
    /// an author can really reach, reached the way they would reach it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_evaluation_that_fails_surfaces_on_the_execution_rather_than_stalling()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var tableId = await PublishFailingTableAsync(api, decisionKey);

        var processKey = $"e2e_brf_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, processKey, Diagram(processKey, decisionKey));

        // The START SUCCEEDS. That is half the point: the instance exists, so
        // there is something for an operator to open.
        var instance = await StartAsync(api, processKey, amount: 50);

        var job = await EventuallyFailedJobAsync(api, instance);

        // Named by the step the author drew, not by a generated id.
        Assert.Equal("decide", job.GetProperty("elementId").GetString());

        // And diagnosable. The engine's own sentence names the decision that
        // failed; an empty or generic message would leave an operator with a red
        // dot and nowhere to go.
        var message = job.GetProperty("exceptionMessage").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message),
            "The failed job carries no message, so nothing on this execution says why it failed.");
        Assert.Contains(decisionKey, message!, StringComparison.Ordinal);

        await api.DeleteAsync($"/api/decision-tables/{tableId}");
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    private static string Diagram(string processKey, string decisionKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="Invoice routing" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice"
                                   autonate:decisionKey="{{decisionKey}}">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="split" />
            <bpmn:exclusiveGateway id="split" default="f_auto" />
            <bpmn:sequenceFlow id="f_esc" sourceRef="split" targetRef="escalate_task">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${route == 'escalate'}</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:userTask id="escalate_task" name="Escalate" />
            <bpmn:sequenceFlow id="f_auto" sourceRef="split" targetRef="auto_task" />
            <bpmn:userTask id="auto_task" name="Auto approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="escalate_task" targetRef="e" />
            <bpmn:sequenceFlow id="f3" sourceRef="auto_task" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(processKey, "s", "decide", "split", "escalate_task", "auto_task", "e")}}
        </bpmn:definitions>
        """;

    private static string UnconfiguredDiagram(string processKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="Unconfigured" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(processKey, "s", "decide", "e")}}
        </bpmn:definitions>
        """;

    // ── harness ─────────────────────────────────────────────────────────────

    /// <summary>The table body, parameterised by the threshold that separates the two routes.</summary>
    /// <remarks>
    /// One shape, two versions. The version-binding test changes ONLY the number,
    /// so a run that decides differently decided differently because of the
    /// version it read and not because it read a different table.
    /// </remarks>
    private static object TableBody(string decisionKey, int threshold) => new
    {
        decisionKey,
        name = "Invoice routing",
        hitPolicy = "FIRST",
        inputs = new[]
        {
            new { id = "in_amount", label = "Amount", name = "amount", typeRef = "number" }
        },
        outputs = new[]
        {
            new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
        },
        rules = new object[]
        {
            new { id = "r1", inputEntries = new[] { $"> {threshold}" }, outputEntries = new[] { "\"escalate\"" } },
            new { id = "r2", inputEntries = new[] { "" }, outputEntries = new[] { "\"auto\"" } }
        }
    };

    private static async Task<string> PublishTableAsync(
        IAPIRequestContext api, string decisionKey, int threshold = 10_000)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = TableBody(decisionKey, threshold)
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;

        await PublishVersionAsync(api, id);
        return id;
    }

    /// <summary>Saves a new threshold over an existing table and publishes it again.</summary>
    private static async Task RepublishTableAsync(
        IAPIRequestContext api, string id, string decisionKey, int threshold)
    {
        var saved = await api.PutAsync($"/api/decision-tables/{id}", new APIRequestContextOptions
        {
            DataObject = TableBody(decisionKey, threshold)
        });
        Assert.True(saved.Ok, $"Re-saving the table failed: {saved.Status} {await saved.TextAsync()}");

        await PublishVersionAsync(api, id);
    }

    private static async Task PublishVersionAsync(IAPIRequestContext api, string id)
    {
        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok,
            $"Publishing the table failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<IAPIResponse> TryPublishWorkflowAsync(
        IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        return await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
    }

    private static async Task PublishWorkflowAsync(IAPIRequestContext api, string key, string xml)
    {
        var published = await TryPublishWorkflowAsync(api, key, xml);
        Assert.True(published.Ok,
            $"Publishing the workflow failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key, double amount)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { amount } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>A table whose only rule produces a value the engine cannot compute.</summary>
    /// <remarks>
    /// <c>bogusFunction(1)</c> passes <c>DecisionTableValidator</c> because it
    /// contains a call — the validator declines to type-check FEEL on purpose —
    /// and it deploys, because the DMN engine does not resolve functions until it
    /// evaluates. So the failure lands exactly where this test needs it: at run
    /// time, in a process.
    /// </remarks>
    private static async Task<string> PublishFailingTableAsync(
        IAPIRequestContext api, string decisionKey)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey,
                name = "Invoice routing",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_amount", label = "Amount", name = "amount", typeRef = "number" }
                },
                outputs = new[]
                {
                    new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
                },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "" }, outputEntries = new[] { "bogusFunction(1)" } }
                }
            }
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;

        await PublishVersionAsync(api, id);
        return id;
    }

    /// <summary>Polls the execution's own job list for one carrying a failure.</summary>
    /// <remarks>
    /// Auton8's route, not the engine's. What the AC asks is that the failure is on
    /// the EXECUTION an operator opens, and a query straight to Flowable would pass
    /// for a product that never surfaced it.
    /// </remarks>
    private static async Task<JsonElement> EventuallyFailedJobAsync(
        IAPIRequestContext api, string instanceId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var seen = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/jobs");
            if (response.Ok)
            {
                var document = JsonDocument.Parse(await response.TextAsync());
                foreach (var job in document.RootElement.EnumerateArray())
                {
                    if (job.TryGetProperty("exceptionMessage", out var message)
                        && !string.IsNullOrWhiteSpace(message.GetString()))
                    {
                        return job.Clone();
                    }
                }

                seen = document.RootElement.GetArrayLength() == 0
                    ? "no jobs on this execution"
                    : $"{document.RootElement.GetArrayLength()} job(s), none carrying a failure";
            }
            else
            {
                seen = $"the jobs route answered {response.Status}";
            }

            await Task.Delay(1000);
        }

        Assert.Fail(
            $"Timed out after 90s waiting for a failed job on the execution: {seen}. The decision "
            + "failed and nothing an operator can see says so, which is the stall this AC forbids.");
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>Asks Flowable directly whether a process definition with this key exists.</summary>
    private static async Task<bool> EngineHasDefinitionAsync(string processKey)
    {
        using var engine = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var response = await engine.GetAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(processKey)}");

        if (!response.IsSuccessStatusCode) return false;

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("total", out var total) && total.GetInt32() > 0;
    }

    private static async Task<List<string>> ActivityIdsAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/history");
        Assert.True(response.Ok, $"Reading history failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("activityId").GetString()!)
            .ToList();
    }

    /// <summary>
    /// Waits for an activity to appear in the ENGINE'S history.
    /// </summary>
    /// <remarks>
    /// Not the cached task list. Measured in #172: after a state change, history
    /// shows the new activity while <c>/tasks</c> still lists the previous one, so
    /// an assertion there measures projection latency rather than the feature.
    /// </remarks>
    private static async Task EventuallyActivityAsync(
        IAPIRequestContext api, string instanceId, string activityId, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var trail = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            var ids = await ActivityIdsAsync(api, instanceId);
            if (ids.Contains(activityId)) return;
            trail = string.Join(" | ", ids);
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 60s waiting for {what}. History was: {trail}");
    }

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 160)}" y="100" width="120" height="90" />
                  </bpmndi:BPMNShape>
             """));

        return $"""
              <bpmndi:BPMNDiagram id="Diagram_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{processKey}">
            {shapes}
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            """;
    }
}
