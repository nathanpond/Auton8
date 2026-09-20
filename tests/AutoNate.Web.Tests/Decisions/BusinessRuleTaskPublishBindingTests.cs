using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// What publish does with a business rule task's reference (#111), in slim.
/// </summary>
/// <remarks>
/// <para>
/// <c>BusinessRuleTaskExecutionTests</c> proves the binding against the live
/// engine, and is <c>RequiresService=Flowable</c> — so GitHub never runs it. This
/// runs on every merge, against the same endpoint, with the engine stubbed.
/// </para>
/// <para>
/// What it can say without an engine: which key publish resolved, what it wrote
/// into the deployed copy, and whether anything was deployed at all. What it
/// cannot say is what the engine then decides, which is the live suite's half.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BusinessRuleTaskPublishBindingTests
{
    private static string Diagram(string processKey, string decisionKey) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{processKey}" name="Routing" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice"
                                   autonate:decisionKey="{decisionKey}">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static DecisionTableModel Table(string key) => new()
    {
        DecisionKey = key,
        Name = "Routing",
        HitPolicy = DecisionHitPolicies.First,
        Inputs = [new DecisionColumn("in_amount", "Amount", "amount", DecisionTypeRefs.Number)],
        Outputs = [new DecisionColumn("out_route", "Route", "route", DecisionTypeRefs.String)],
        Rules = [new DecisionRule("r1", ["> 10"], ["\"escalate\""])]
    };

    private static string Key() => "t_" + Guid.NewGuid().ToString("n")[..10];

    [Fact]
    public async Task Publish_pins_the_deployed_copy_to_the_version_published_now()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        var decisionKey = Key();
        var created = await client.PostAsJsonAsync("/api/decision-tables/", Table(decisionKey));
        created.EnsureSuccessStatusCode();
        var table = (await created.Content.ReadFromJsonAsync<DecisionTableModel>())!;
        (await client.PostAsJsonAsync($"/api/decision-tables/{table.Id}/publish", new { }))
            .EnsureSuccessStatusCode();

        var model = new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Routing",
            ProcessKey = "brt_pin_flow",
            BpmnXml = Diagram("brt_pin_flow", decisionKey)
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var published = await client.PostAsJsonAsync($"/api/workflows/{model.Id}/publish", model);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        // THE DEPLOYED COPY, not the stored one.
        var deployed = XDocument.Parse(Assert.Single(factory.FlowableStub.DeployedModels).BpmnXml);
        var field = deployed.Descendants()
            .Single(e => e.Name.LocalName == "field"
                         && (string?)e.Attribute("name") == "decisionTableReferenceKey");

        Assert.Equal($"{decisionKey}-v1", field.Value);

        // AND THE PINNED COPY EXISTS IN THE ENGINE. Pointing the process at a key
        // nothing was deployed under is the same silent no-op as not pinning: the
        // diagram deploys, and the step decides nothing when someone runs it.
        Assert.Contains($"Ensure:{decisionKey}-v1", factory.DecisionStub.Calls);

        // AND THE STORED MODEL STILL SAYS WHAT THE AUTHOR PICKED, so the studio
        // shows them their table and the next publish resolves it afresh.
        var stored = await client.GetFromJsonAsync<WorkflowModel>($"/api/workflows/{model.Id}");
        Assert.Contains($"decisionKey=\"{decisionKey}\"", stored!.BpmnXml, StringComparison.Ordinal);
        Assert.DoesNotContain($"{decisionKey}-v1", stored.BpmnXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_is_refused_when_the_table_is_not_published_and_nothing_is_deployed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        // Created and NEVER published — the same state a deleted table leaves the
        // reference in, reached without deleting anything.
        var decisionKey = Key();
        (await client.PostAsJsonAsync("/api/decision-tables/", Table(decisionKey)))
            .EnsureSuccessStatusCode();

        var model = new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Routing",
            ProcessKey = "brt_unpub_flow",
            BpmnXml = Diagram("brt_unpub_flow", decisionKey)
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var published = await client.PostAsJsonAsync($"/api/workflows/{model.Id}/publish", model);

        Assert.Equal(HttpStatusCode.BadRequest, published.StatusCode);

        var body = await published.Content.ReadAsStringAsync();
        // The key, so the author knows which reference; the element name, so they
        // know where to click.
        Assert.Contains(decisionKey, body, StringComparison.Ordinal);
        Assert.Contains("Route the invoice", body, StringComparison.Ordinal);

        // AND IT NEVER REACHED THE ENGINE. The 400 alone would pass for an
        // implementation that deployed first and complained afterwards — which is
        // the whole difference between "fails at deployment" and "fails at
        // execution", the words this AC is written in.
        Assert.Empty(factory.FlowableStub.DeployedModels);
    }
}
