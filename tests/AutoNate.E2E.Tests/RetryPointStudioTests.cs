using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An author can mark a step as an independent retry point from the studio, and
/// the marking survives a save and a reload (#168).
/// </summary>
/// <remarks>
/// The execution half lives in <see cref="RetryPointExecutionTests"/>. This half
/// exists because the setting has to reach the deployed definition to mean
/// anything: a retry point that does not serialise is a switch that does nothing.
///
/// The saved XML is read back deliberately. #167 found that bpmn-moddle silently
/// discards an attribute whose namespace prefix the diagram does not declare, and
/// flowable:async is written exactly that way — so "the switch flipped" is not
/// evidence that anything was written.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class RetryPointStudioTests : E2ETestBase
{
    public RetryPointStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private const string RetryPointLabel = "Retry this step on its own if it fails";

    [Fact]
    public async Task Marking_a_service_task_as_a_retry_point_survives_a_save_and_reload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("retry-point-studio");
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id,
                name,
                processKey = "retry_point_studio",
                bpmnXml = Diagram
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

        await OpenConfigureAsync(page, "ServiceTask_1");

        var toggle = page.GetByLabel(RetryPointLabel);
        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Off by default for a service task that has never been marked — the AC,
        // and the thing that makes the check below mean something.
        await Assertions.Expect(toggle).Not.ToBeCheckedAsync();

        await toggle.CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // It reached the stored definition, with its prefix intact.
        await ReadSavedXmlAsync(page.APIRequest, id,
            xml => xml.Contains("flowable:async=\"true\"", StringComparison.Ordinal),
            "the retry point to reach the saved diagram");

        // And it comes back. This is the read path (describeServiceTask), which a
        // write-only test would leave entirely unexercised.
        await page.ReloadAsync();
        var reloadedSelector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(reloadedSelector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await reloadedSelector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "ServiceTask_1");

        await Assertions.Expect(page.GetByLabel(RetryPointLabel))
            .ToBeCheckedAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Unmarking_a_retry_point_removes_it_from_the_saved_definition()
    {
        // The direction a write-only implementation gets wrong: setting works,
        // clearing silently does not, and the author cannot undo the choice.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("retry-point-unmark");
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id,
                name,
                processKey = "retry_point_unmark",
                bpmnXml = Diagram.Replace(
                    "flowable:behaviorKey=\"autonate.noop\"",
                    "flowable:behaviorKey=\"autonate.noop\" flowable:async=\"true\"",
                    StringComparison.Ordinal)
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "ServiceTask_1");

        var toggle = page.GetByLabel(RetryPointLabel);
        await Assertions.Expect(toggle).ToBeCheckedAsync(new() { Timeout = 10_000 });

        await toggle.UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await ReadSavedXmlAsync(page.APIRequest, id,
            xml => !xml.Contains("flowable:async", StringComparison.Ordinal),
            "the retry point to be cleared from the saved diagram");
    }

    // The property editors are reached by right-clicking a node and choosing
    // "Configure…" — a plain click only selects it. Centralised so all three call
    // sites open the modal the same way a person does.
    private static async Task OpenConfigureAsync(IPage page, string elementId)
    {
        var shape = page.Locator($"[data-element-id='{elementId}']");
        await Assertions.Expect(shape).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await shape.ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First
            .ClickAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Polls the stored diagram until <paramref name="until"/> holds. Save is a
    /// round trip, so reading the instant the button is clicked races it.
    /// </summary>
    /// <remarks>
    /// The caller supplies the condition deliberately. An earlier version polled
    /// until the XML merely contained the element id — which the SEEDED diagram
    /// already satisfied, so it returned the pre-save document every time and both
    /// tests failed against a correct implementation. A poll predicate that is
    /// already true before the event is not a wait at all.
    /// </remarks>
    private static async Task<string> ReadSavedXmlAsync(
        IAPIRequestContext api, Guid id, Func<string, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var last = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/workflows/{id}");
            if (response.Ok)
            {
                using var document = JsonDocument.Parse(await response.TextAsync());
                if (document.RootElement.TryGetProperty("bpmnXml", out var xml))
                {
                    last = xml.GetString() ?? string.Empty;
                    if (until(last)) return last;
                }
            }

            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. The saved diagram was: {last}");
        return last;
    }

    private const string Diagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="retry_point_studio" name="Retry Point Studio" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Do the thing"
                              flowable:delegateExpression="${autonateBehaviorDelegate}"
                              flowable:autonateServiceKind="behavior"
                              flowable:behaviorKey="autonate.noop" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="retry_point_studio">
              <bpmndi:BPMNShape id="Shape_Start" bpmnElement="StartEvent_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Service" bpmnElement="ServiceTask_1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_End" bpmnElement="EndEvent_1">
                <dc:Bounds x="360" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
