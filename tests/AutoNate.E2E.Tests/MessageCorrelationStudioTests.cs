using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An author sets a message element's correlation key in the studio (#112).
/// </summary>
/// <remarks>
/// The AC is "set in the studio, not by hand-editing BPMN", so this drives the
/// real modal. The saved XML is read back because #167 established that
/// bpmn-moddle silently discards an attribute whose namespace prefix the diagram
/// does not declare — so "the field accepted the text" is not evidence that
/// anything was written.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class MessageCorrelationStudioTests : E2ETestBase
{
    public MessageCorrelationStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Setting_a_correlation_key_on_a_catch_event_reaches_the_saved_diagram()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("message-correlation-studio");
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id,
                name,
                processKey = "message_correlation_studio",
                bpmnXml = Diagram
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Catch_1");

        // The message name is shown but not editable here: it comes from the
        // <bpmn:message> the diagram declares, so letting it be typed would let it
        // diverge from what the engine subscribes to.
        var messageField = page.GetByLabel("Message", new() { Exact = true });
        await Assertions.Expect(messageField).ToBeDisabledAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(messageField).ToHaveValueAsync("paymentCleared");

        var correlation = page.GetByLabel("Correlation key", new() { Exact = true });
        await Assertions.Expect(correlation).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(correlation).ToHaveValueAsync("");

        await correlation.FillAsync("orderId");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await ReadSavedXmlAsync(page.APIRequest, id,
            xml => xml.Contains("autonateCorrelationKey=\"orderId\"", StringComparison.Ordinal),
            "the correlation key to reach the saved diagram");

        // And it comes back — the read path, which a write-only test leaves
        // entirely unexercised.
        await page.ReloadAsync();
        var reloaded = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(reloaded).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await reloaded.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Catch_1");
        await Assertions.Expect(page.GetByLabel("Correlation key", new() { Exact = true }))
            .ToHaveValueAsync("orderId", new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task A_message_start_event_is_not_offered_a_correlation_key()
    {
        // Nothing is waiting when a message start event fires, so a key written on
        // one would be a filter that silently matched everything. Offering the
        // field and ignoring it is the failure this prevents.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("message-start-studio");

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = Guid.NewGuid(),
                name,
                processKey = "message_start_studio",
                bpmnXml = Diagram
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Start_1");

        // The modal opened for it (it IS a message element)...
        await Assertions.Expect(page.GetByText("Nothing is waiting yet", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ...but with no correlation key to set.
        await Assertions.Expect(page.GetByLabel("Correlation key", new() { Exact = true }))
            .ToHaveCountAsync(0);
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

    private const string Diagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="paymentCleared" />
          <bpmn:message id="Msg_2" name="orderPlaced" />
          <bpmn:process id="message_correlation_studio" name="Message Studio" isExecutable="true">
            <bpmn:startEvent id="Start_1">
              <bpmn:messageEventDefinition messageRef="Msg_2" />
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Catch_1" />
            <bpmn:intermediateCatchEvent id="Catch_1" name="Await payment">
              <bpmn:messageEventDefinition messageRef="Msg_1" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Catch_1" targetRef="End_1" />
            <bpmn:endEvent id="End_1" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="message_correlation_studio">
              <bpmndi:BPMNShape id="Shape_Start" bpmnElement="Start_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Catch" bpmnElement="Catch_1">
                <dc:Bounds x="220" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_End" bpmnElement="End_1">
                <dc:Bounds x="340" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
