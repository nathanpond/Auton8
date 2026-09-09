using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A complex gateway's routing script survives the studio (#218).
/// </summary>
/// <remarks>
/// This exists because the answer could not be reasoned to. bpmn-js is vendored
/// as a browser bundle with NO Flowable moddle extension, so what it does with a
/// <c>&lt;bpmn:script&gt;</c> child on an element whose moddle type has no such
/// property is a property of that bundle, not of the BPMN spec. Guessing wrong
/// loses an author's code silently on their next save, which is the exact
/// failure this milestone exists to end — so it is asserted rather than assumed.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ComplexGatewayStudioRoundTripTests : E2ETestBase
{
    public ComplexGatewayStudioRoundTripTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_routing_script_survives_opening_and_saving_in_the_studio()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var id = Guid.NewGuid();
        var name = TestNames.Prefixed("cg-round-trip");
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = "cg_round_trip", bpmnXml = Diagram }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        // The modeller parsed and drew it. Until this passes, a later assertion
        // about the script would be about a diagram that never loaded.
        await Assertions.Expect(page.Locator("[data-element-id='cg']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Save round-trips the diagram through bpmn-js's serialiser, which is
        // where anything its moddle does not model gets dropped.
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(3_000);

        var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, await stored.TextAsync());
        using var document = JsonDocument.Parse(await stored.TextAsync());
        var xml = document.RootElement.GetProperty("bpmnXml").GetString()!;

        Assert.Contains("complexGateway", xml, StringComparison.Ordinal);

        // The load-bearing assertion. A <bpmn:script> CHILD does not survive
        // this — proven, not assumed: the first version of this test seeded one
        // and it came back gone. bpmn-js's moddle has no such property on
        // ComplexGateway and drops what it cannot model.
        //
        // An attribute in the autonate namespace is the mechanism runAs already
        // uses through $attrs, which does survive.
        Assert.Contains("return 'fa';", xml, StringComparison.Ordinal);

        // And the newlines with it. XML normalises literal newlines in attribute
        // values to spaces on parse, so this only works if the serialiser
        // escapes them — and a routing script folded onto one line would have
        // its first // comment swallow the return statement.
        //
        // Asserted by parsing rather than by matching the escape's spelling:
        // bpmn-js writes &#xA; and .NET writes &#10;, and both are the same
        // newline. The first version of this assertion pinned the decimal form
        // and failed on a value that was completely correct.
        Assert.Contains("// pick a route", xml, StringComparison.Ordinal);

        var parsed = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace autonate = "http://autonate.dev/workflows";
        var routeScript = parsed.Descendants()
            .Single(e => e.Attribute("id")?.Value == "cg")
            .Attribute(autonate + "routeScript")?.Value;

        Assert.NotNull(routeScript);
        Assert.Contains("\n", routeScript);
        Assert.Contains("return 'fa';", routeScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_author_opens_the_gateway_from_the_context_menu_and_writes_a_routing_script()
    {
        // The story's first user-observable truth: no palette work, no custom
        // modeller module, reached by the same right-click path as every other
        // element.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var id = Guid.NewGuid();
        var name = TestNames.Prefixed("cg-editor");
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = "cg_editor", bpmnXml = Diagram }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        var shape = page.Locator("[data-element-id='cg']");
        await Assertions.Expect(shape).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await shape.ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First
            .ClickAsync(new() { Timeout = 10_000 });

        // Titled for what it is. The same panel serves script tasks, and telling
        // an author they are editing a script task would be a lie about the
        // element they right-clicked.
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText("Complex Gateway", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        // A gateway's result variable is generated and bound to the flow
        // conditions, so it must not be offered — an author editing it would
        // break the routing with nothing to say so.
        await Assertions.Expect(dialog.GetByText("Result Variable", new() { Exact = false }))
            .ToHaveCountAsync(0);

        var consoleErrors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error") consoleErrors.Add(message.Text);
        };
        page.PageError += (_, error) => consoleErrors.Add(error);

        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(1_500);
        Assert.True(consoleErrors.Count == 0,
            "Apply raised: " + string.Join(" | ", consoleErrors));

        // The modal closing is how Apply reports success, and waiting for it is
        // not politeness — the overlay sits over the Save button, so clicking
        // Save while it is open times out rather than saving. The same trap is
        // recorded in CallActivityStudioTests.
        try
        {
            await Assertions.Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }
        catch
        {
            var alerts = await page.Locator("[role='alert'], [role='status'], .mantine-Notification-root")
                .AllTextContentsAsync();
            Assert.Fail("Apply did not close the panel. Notifications: "
                        + (alerts.Count == 0 ? "(none)" : string.Join(" | ", alerts))
                        + " || console: "
                        + (consoleErrors.Count == 0 ? "(none)" : string.Join(" | ", consoleErrors)));
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })
            .ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(3_000);

        // Saving through the editor keeps the script rather than clearing it —
        // the failure a panel that reads from the wrong place produces.
        var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, await stored.TextAsync());
        using var document = JsonDocument.Parse(await stored.TextAsync());
        Assert.Contains("return 'fa';",
            document.RootElement.GetProperty("bpmnXml").GetString()!, StringComparison.Ordinal);
    }

    // #115. Compensation is authored by drawing an ASSOCIATION from a
    // compensation boundary event to its handler. If bpmn-js drops that on save
    // — as it drops a <bpmn:script> child on a complex gateway — then the handler
    // is unreachable and compensation silently does nothing, which is the exact
    // failure #115 exists to fix. Asserted rather than assumed.
    [Fact]
    public async Task A_compensation_association_survives_the_studio()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var id = Guid.NewGuid();
        var name = TestNames.Prefixed("compensation-round-trip");
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = "compensation_rt", bpmnXml = CompensationDiagram }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("[data-element-id='b1']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(3_000);

        var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, await stored.TextAsync());
        using var document = JsonDocument.Parse(await stored.TextAsync());
        var xml = document.RootElement.GetProperty("bpmnXml").GetString()!;

        var parsed = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        // The association is the link. Without it the handler is a stray node.
        var association = Assert.Single(parsed.Descendants(bpmn + "association"));
        Assert.Equal("b1", association.Attribute("sourceRef")?.Value);
        Assert.Equal("h1", association.Attribute("targetRef")?.Value);

        // And the marker that makes the handler a handler rather than an
        // unreachable step.
        Assert.Equal("true", parsed.Descendants()
            .Single(e => e.Attribute("id")?.Value == "h1")
            .Attribute("isForCompensation")?.Value);
    }

    private const string CompensationDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="compensation_rt" name="Undo" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
            <bpmn:userTask id="t1" name="Take payment" />
            <bpmn:sequenceFlow id="f1" sourceRef="t1" targetRef="done" />
            <bpmn:endEvent id="done" name="Undo everything">
              <bpmn:compensateEventDefinition />
            </bpmn:endEvent>
            <bpmn:boundaryEvent id="b1" attachedToRef="t1">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="h1" name="Refund" isForCompensation="true" />
            <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="compensation_rt">
              <bpmndi:BPMNShape id="Shape_s" bpmnElement="s">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_t1" bpmnElement="t1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_b1" bpmnElement="b1">
                <dc:Bounds x="280" y="142" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_h1" bpmnElement="h1">
                <dc:Bounds x="260" y="220" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_done" bpmnElement="done">
                <dc:Bounds x="380" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private const string Diagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="cg_round_trip" name="Round trip" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="cg" />
            <bpmn:complexGateway id="cg" name="Choose"
                                 autonate:scriptFormat="javascript"
                                 autonate:routeScript="// pick a route&#10;return 'fa';" />
            <bpmn:sequenceFlow id="fa" sourceRef="cg" targetRef="ta" />
            <bpmn:sequenceFlow id="fb" sourceRef="cg" targetRef="tb" />
            <bpmn:userTask id="ta" name="Route A" />
            <bpmn:userTask id="tb" name="Route B" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="cg_round_trip">
              <bpmndi:BPMNShape id="Shape_s" bpmnElement="s">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_cg" bpmnElement="cg">
                <dc:Bounds x="200" y="90" width="50" height="50" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_ta" bpmnElement="ta">
                <dc:Bounds x="320" y="60" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_tb" bpmnElement="tb">
                <dc:Bounds x="320" y="180" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
