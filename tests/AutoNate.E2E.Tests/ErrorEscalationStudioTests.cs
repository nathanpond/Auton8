using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An author configures error and escalation codes in the studio (#114).
/// </summary>
/// <remarks>
/// The novel risk here is a **dangling ref**. A code lives on a root
/// &lt;bpmn:error&gt; element and the event points at it by id, so an editor that
/// writes the ref without creating the root element produces a diagram that looks
/// configured, matches nothing, and says nothing. Both halves are asserted.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ErrorEscalationStudioTests : E2ETestBase
{
    public ErrorEscalationStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Setting_an_error_code_writes_both_the_reference_and_the_root_element()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("error-code-studio");
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = "error_code_studio", bpmnXml = Diagram }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Boundary_1");

        var code = page.GetByLabel("Error code", new() { Exact = true });
        await Assertions.Expect(code).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // An error boundary always interrupts in BPMN, so the studio must not
        // offer a choice it cannot honour.
        await Assertions.Expect(page.GetByLabel("Stop the attached step while this is handled"))
            .ToHaveCountAsync(0);

        await code.FillAsync("PAYMENT_DECLINED");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var saved = await ReadSavedXmlAsync(page.APIRequest, id,
            xml => xml.Contains("PAYMENT_DECLINED", StringComparison.Ordinal),
            "the error code to reach the saved diagram");

        // The root element exists, carrying the code...
        Assert.Contains("errorCode=\"PAYMENT_DECLINED\"", saved);

        // ...and the event points at it rather than at nothing. A ref with no root
        // element behind it is the silent failure this whole story is about.
        var rootId = System.Text.RegularExpressions.Regex
            .Match(saved, "<bpmn:error[^>]*id=\"([^\"]+)\"[^>]*errorCode=\"PAYMENT_DECLINED\"")
            .Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(rootId), $"No <bpmn:error> root element was written. XML: {saved}");
        Assert.Contains($"errorRef=\"{rootId}\"", saved);
    }

    [Fact]
    public async Task An_escalation_boundary_can_be_made_non_interrupting()
    {
        // The half that distinguishes escalation from error, and the one a
        // write-only editor gets wrong: BPMN treats an absent cancelActivity as
        // true, so "non-interrupting" has to be written explicitly to exist at all.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("escalation-code-studio");
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = "escalation_code_studio", bpmnXml = Diagram }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Escalation_1");

        await page.GetByLabel("Escalation code", new() { Exact = true }).FillAsync("NEEDS_MANAGER");
        var interrupting = page.GetByLabel("Stop the attached step while this is handled");
        await Assertions.Expect(interrupting).ToBeCheckedAsync(new() { Timeout = 10_000 });
        await interrupting.UncheckAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var saved = await ReadSavedXmlAsync(page.APIRequest, id,
            xml => xml.Contains("NEEDS_MANAGER", StringComparison.Ordinal),
            "the escalation code to reach the saved diagram");

        Assert.Contains("escalationCode=\"NEEDS_MANAGER\"", saved);
        Assert.Contains("cancelActivity=\"false\"", saved);
    }

    private static async Task OpenConfigureAsync(IPage page, string elementId)
    {
        var shape = page.Locator($"[data-element-id='{elementId}']");
        await Assertions.Expect(shape).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await shape.ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First
            .ClickAsync(new() { Timeout = 10_000 });
    }

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

    // A subprocess carrying both a (bare) error boundary and an escalation
    // boundary, so one diagram serves both tests.
    private const string Diagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="error_code_studio" name="Error Studio" isExecutable="true">
            <bpmn:startEvent id="Start_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Sub_1" />
            <bpmn:subProcess id="Sub_1" name="Risky">
              <bpmn:startEvent id="Inner_1" />
              <bpmn:sequenceFlow id="Flow_i" sourceRef="Inner_1" targetRef="Inner_2" />
              <bpmn:userTask id="Inner_2" name="Work" />
            </bpmn:subProcess>
            <bpmn:boundaryEvent id="Boundary_1" attachedToRef="Sub_1">
              <bpmn:errorEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:boundaryEvent id="Escalation_1" attachedToRef="Sub_1">
              <bpmn:escalationEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Boundary_1" targetRef="Caught_1" />
            <bpmn:userTask id="Caught_1" name="Caught" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="Escalation_1" targetRef="Esc_1" />
            <bpmn:userTask id="Esc_1" name="Escalated" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="error_code_studio">
              <bpmndi:BPMNShape id="S_Start" bpmnElement="Start_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_Sub" bpmnElement="Sub_1">
                <dc:Bounds x="200" y="60" width="200" height="140" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_B1" bpmnElement="Boundary_1">
                <dc:Bounds x="240" y="182" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_E1" bpmnElement="Escalation_1">
                <dc:Bounds x="330" y="182" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_C1" bpmnElement="Caught_1">
                <dc:Bounds x="220" y="260" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_Esc" bpmnElement="Esc_1">
                <dc:Bounds x="360" y="260" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
