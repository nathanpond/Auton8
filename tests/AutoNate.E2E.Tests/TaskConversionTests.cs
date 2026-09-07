using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Manual and generic tasks are converted in the studio; a throw-none passes
/// through (#167).
/// </summary>
[Collection(AutoNateE2ECollection.Name)]
public sealed class TaskConversionTests : E2ETestBase
{
    public TaskConversionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Opening_a_diagram_with_a_manual_task_converts_it_and_says_so()
    {
        // The load path, which is the one a drop-only conversion misses and the one
        // every imported or hand-edited diagram takes.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("manual-task-import");

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = Guid.NewGuid(),
                name,
                processKey = "manual_task_import",
                bpmnXml = LegacyDiagram
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        // The author is told, and told WHY — not merely that something changed.
        await Assertions.Expect(page.GetByText("Converted", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByText("without waiting for anyone", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And the diagram really changed — proven by saving it, which is refused
        // *because* the converted tasks have nobody to do them.
        //
        // That refusal is stronger evidence than reading the XML would be. It fires
        // only on tasks carrying the `autonateConvertedFrom` marker, so seeing it
        // proves three things at once: the elements became user tasks, the marker
        // was written, and the marker survived serialisation. An earlier version of
        // this test read the saved XML instead and caught a real bug — the marker
        // was being dropped, because a diagram authored elsewhere does not declare
        // xmlns:flowable and bpmn-moddle discards an attribute with an undeclared
        // prefix.
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Assertions.Expect(page.GetByText("nobody to do it", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByText("Post the cheque", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The manual task itself is never mentioned in the refusal, because it no
        // longer exists — if the conversion had not happened, the message would name
        // a manual task instead.
        await Assertions.Expect(page.GetByText("Manual task 'Post the cheque'", new() { Exact = false }))
            .ToHaveCountAsync(0);
    }

    private const string LegacyDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="manual_task_import" name="Imported" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:manualTask id="manual" name="Post the cheque" />
            <bpmn:task id="generic" name="Some work" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="manual_task_import">
              <bpmndi:BPMNShape id="sh_s" bpmnElement="s">
                <dc:Bounds x="120" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="sh_manual" bpmnElement="manual">
                <dc:Bounds x="220" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="sh_generic" bpmnElement="generic">
                <dc:Bounds x="360" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="sh_e" bpmnElement="e">
                <dc:Bounds x="500" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}

/// <summary>
/// An intermediate throw (none) event passes straight through (#167).
/// </summary>
/// <remarks>
/// Separate class because this half needs the engine and the conversion half does
/// not — traiting the conversion test would exclude it from CI for no reason.
///
/// Passing straight through is *correct* here, unlike the manual task it ships
/// beside. BPMN defines a none throw as a marker in the flow with no behaviour of its
/// own, so "the token does not stop" is the whole feature rather than a silent no-op.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ThrowNoneExecutionTests : E2ETestBase
{
    public ThrowNoneExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Execution_passes_straight_through_a_throw_none_and_continues()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"throw_none_{Guid.NewGuid():N}"[..24];
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Throw None" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="checkpoint" />
                <bpmn:intermediateThrowEvent id="checkpoint" name="Checkpoint" />
                <bpmn:sequenceFlow id="f1" sourceRef="checkpoint" targetRef="after" />
                <bpmn:userTask id="after" name="After the checkpoint" flowable:assignee="admin" />
                <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              <bpmndi:BPMNDiagram id="Diagram_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{{key}}">
                  <bpmndi:BPMNShape id="sh_s" bpmnElement="s">
                    <dc:Bounds x="120" y="100" width="36" height="36" />
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="sh_cp" bpmnElement="checkpoint">
                    <dc:Bounds x="220" y="100" width="36" height="36" />
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="sh_after" bpmnElement="after">
                    <dc:Bounds x="320" y="80" width="100" height="80" />
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="sh_e" bpmnElement="e">
                    <dc:Bounds x="480" y="100" width="36" height="36" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = key, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = key, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");

        var started = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { }
        });
        Assert.True(started.Ok, $"Starting failed: {started.Status} {await started.TextAsync()}");
        using var startDoc = JsonDocument.Parse(await started.TextAsync());
        var instanceId = startDoc.RootElement.GetProperty("id").GetString()!;

        // Asserted end to end, not by deployment succeeding: the token reached the
        // activity BEYOND the throw event. Asserting only that the process started
        // would pass for one parked on the throw event forever.
        var tasks = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(tasks.Ok, $"Reading tasks failed: {tasks.Status}");
        using var taskDoc = JsonDocument.Parse(await tasks.TextAsync());
        var names = taskDoc.RootElement.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToArray();

        Assert.Contains("After the checkpoint", names);

        // And it did not stop at the throw event.
        var diagram = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        using var diagramDoc = JsonDocument.Parse(await diagram.TextAsync());
        var current = diagramDoc.RootElement.GetProperty("currentActivityIds")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain("checkpoint", current);
    }
}
