using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The studio's BPMN property editors, and the hook every spec that drives them
/// depends on (#78, E2E-036/E2E-037).
/// </summary>
/// <remarks>
/// <para>
/// <b>UNTRAITED, deliberately, and that is the story's own instruction.</b> These
/// journeys open a diagram, edit it and reload it; none of them publishes, so none
/// of them needs Flowable. <c>WorkflowStudioTests</c> carries
/// <c>RequiresService=Flowable</c> because publishing there fails without it, and
/// the trait is per CLASS -- so putting canvas coverage in that file would push it
/// into a tier no pull request runs. The half of E2E-036 that genuinely needs a
/// running engine (publish, start, open the resulting task surface) stays there.
/// </para>
/// <para>
/// <c>WorkflowPaletteTests</c> and <c>WorkflowBpmnSupportPanelTests</c> already
/// drive this canvas untraited, so this is the established boundary rather than a
/// new claim about what the studio needs.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowStudioEditorTests : E2ETestBase
{
    public WorkflowStudioEditorTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>
    /// Every rendered element carries its authored id (#78).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing pinned this, and eight spec files are built on it.</b>
    /// <c>data-element-id</c> is stock diagram-js: it is not a test-only
    /// affordance, it ships to real users, and no custom renderer strips it today.
    /// That is exactly why it is worth a guard -- it is inert, so nobody would
    /// think to check it, and a future custom renderer would take out
    /// <c>BusinessRuleTaskStudioTests</c>, <c>CallActivityStudioTests</c>,
    /// <c>ComplexGatewayStudioRoundTripTests</c> and five others in one commit,
    /// with failures that look like the editors breaking rather than the hook.
    /// </para>
    /// <para>
    /// Asserted on a SHAPE and a CONNECTION, because diagram-js stamps both and a
    /// renderer replacing only one would leave half the specs working. And on the
    /// AUTHORED id specifically -- an attribute carrying some internal handle
    /// would satisfy "the attribute exists" while every selector in those eight
    /// files still missed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_rendered_element_carries_the_id_the_author_gave_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var (_, seededName) = await SeedAsync(page, "elementid", EditorDiagram);
        await OpenAsync(page, seededName);

        // A shape.
        await Assertions.Expect(page.Locator("[data-element-id='approve']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A CONNECTION. diagram-js stamps these too, and a renderer that handled
        // only shapes would leave every sequence-flow selector silently empty.
        await Assertions.Expect(page.Locator("[data-element-id='f1']"))
            .ToHaveCountAsync(1);

        // Every authored id is present, not merely the two asserted above. A
        // renderer that stamped the first element and stopped would pass a
        // spot-check.
        foreach (var id in new[] { "timer", "approve", "notify", "gate", "calc", "f1", "f2" })
        {
            await Assertions.Expect(page.Locator($"[data-element-id='{id}']"))
                .ToHaveCountAsync(1);
        }

        // THE COMPLEMENT: the attribute carries the AUTHORED id, not an internal
        // handle that merely happens to be unique. Without this, a renderer
        // stamping its own ids would satisfy every assertion above while breaking
        // all eight spec files that select by the id they seeded.
        var stamped = await page.Locator("[data-element-id]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-element-id'))");

        Assert.Contains("approve", stamped);
        Assert.Contains("f1", stamped);
    }

    /// <summary>
    /// E2E-037: each advanced editor's value survives a reload (#78).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reload is the whole assertion.</b> Every one of these panels writes
    /// into bpmn-js's in-memory moddle first, and a panel that writes there and
    /// nowhere else looks exactly like one that saved: the field shows the new
    /// value, the canvas updates, the modal closes. The difference appears only
    /// when the page is thrown away and the diagram re-read from the database.
    /// </para>
    /// <para>
    /// Asserted through the REOPENED EDITOR rather than by reading the stored XML,
    /// because the round trip an author cares about is panel to panel. A test that
    /// read the XML would pass against a serialiser that writes an attribute the
    /// panel then cannot parse back.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("timer", "Timer Start Event", "Event Name (optional)", "Nightly sweep")]
    [InlineData("gate", "Exclusive Gateway", "Name", "Route by amount")]
    [InlineData("calc", "Script Task", "Task Name", "Compute the total")]
    public async Task An_editors_value_survives_a_reload(
        string elementId, string panelHeading, string fieldLabel, string value)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var (id, name) = await SeedAsync(page, $"ed{elementId}", EditorDiagram);
        await OpenAsync(page, name);

        await ConfigureAsync(page, elementId, panelHeading);
        var field = page.GetByLabel(fieldLabel, new() { Exact = true });
        await field.FillAsync(value);
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // Polled rather than slept: the status text appears before the row is
        // written, and reloading into that window reads the diagram as it was.
        Assert.True(
            await WaitForAsync(async () =>
            {
                var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
                if (!stored.Ok) return false;
                using var document = JsonDocument.Parse(await stored.TextAsync());
                return (document.RootElement.GetProperty("bpmnXml").GetString() ?? "")
                    .Contains(value, StringComparison.Ordinal);
            }),
            $"'{value}' never reached the stored diagram, so the reload below would "
                + "prove nothing about persistence.");

        // THE RELOAD. Everything above is equally true of an editor that only ever
        // touched the in-memory moddle.
        await page.ReloadAsync();
        await OpenAsync(page, name);
        await ConfigureAsync(page, elementId, panelHeading);

        await Assertions.Expect(page.GetByLabel(fieldLabel, new() { Exact = true }))
            .ToHaveValueAsync(value, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// E2E-037, the signal-start half, which needs its own diagram (#78).
    /// </summary>
    /// <remarks>
    /// Separate because a signal start event replaces the plain one: the editors
    /// above all hang off a diagram whose start event is a timer, and a process
    /// carrying both would be asserting something about multiple start events
    /// rather than about this panel.
    /// </remarks>
    [Fact]
    public async Task The_signal_start_editors_value_survives_a_reload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var (id, name) = await SeedAsync(page, "edsignal", SignalStartDiagram);
        await OpenAsync(page, name);

        await ConfigureAsync(page, "sigstart", "Signal Start Event");
        var field = page.GetByLabel("Topic", new() { Exact = true });
        await field.FillAsync("orders.received");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        Assert.True(
            await WaitForAsync(async () =>
            {
                var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
                if (!stored.Ok) return false;
                using var document = JsonDocument.Parse(await stored.TextAsync());
                return (document.RootElement.GetProperty("bpmnXml").GetString() ?? "")
                    .Contains("orders.received", StringComparison.Ordinal);
            }),
            "The signal name never reached the stored diagram.");

        await page.ReloadAsync();
        await OpenAsync(page, name);
        await ConfigureAsync(page, "sigstart", "Signal Start Event");

        await Assertions.Expect(page.GetByLabel("Topic", new() { Exact = true }))
            .ToHaveValueAsync("orders.received", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// E2E-037, the service task: its Apply is gated on a behaviour (#78).
    /// </summary>
    /// <remarks>
    /// Its own spec rather than a row in the theory above, and the reason is the
    /// panel's own rule: Apply is <c>disabled={disabled || !editor.behaviorKey.trim()}</c>,
    /// so filling the name and pressing Apply can never work. A theory row doing
    /// that failed against a disabled button -- correctly. Choosing the behaviour
    /// IS the meaningful edit for a service task, so that is what this asserts
    /// survives the reload.
    /// </remarks>
    [Fact]
    public async Task The_service_tasks_chosen_behaviour_survives_a_reload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var (id, name) = await SeedAsync(page, "ednotify", EditorDiagram);
        await OpenAsync(page, name);

        await ConfigureAsync(page, "notify", "Service Task");

        // Whatever the deployment actually registers, rather than a hard-coded
        // key: the list is built from the behaviour registry, and pinning a name
        // here would make this a test of the seed data.
        var behaviour = page.GetByLabel("Behavior", new() { Exact = true });
        await Assertions.Expect(behaviour).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var chosen = await behaviour.EvaluateAsync<string>(
            "el => { const o = [...el.options].find(x => x.value); return o ? o.value : ''; }");
        Assert.False(string.IsNullOrEmpty(chosen),
            "No workflow behaviour is registered, so the Service Task panel's Apply can never "
                + "enable and this spec would be asserting against a disabled button.");

        await behaviour.SelectOptionAsync(new SelectOptionValue { Value = chosen });
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        Assert.True(
            await WaitForAsync(async () =>
            {
                var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
                if (!stored.Ok) return false;
                using var document = JsonDocument.Parse(await stored.TextAsync());
                return (document.RootElement.GetProperty("bpmnXml").GetString() ?? "")
                    .Contains(chosen, StringComparison.Ordinal);
            }),
            $"The behaviour '{chosen}' never reached the stored diagram.");

        await page.ReloadAsync();
        await OpenAsync(page, name);
        await ConfigureAsync(page, "notify", "Service Task");

        await Assertions.Expect(page.GetByLabel("Behavior", new() { Exact = true }))
            .ToHaveValueAsync(chosen, new() { Timeout = 15_000 });
    }

    /// <summary>Right-click, Configure -- the way an author reaches a panel.</summary>
    /// <remarks>
    /// Selecting an element opens nothing, so a spec that clicked would sit on a
    /// canvas waiting for a modal that never arrives.
    /// </remarks>
    private static async Task ConfigureAsync(IPage page, string elementId, string heading)
    {
        var shape = page.Locator($"[data-element-id='{elementId}']");
        await Assertions.Expect(shape).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await shape.ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First.ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private static async Task<(Guid Id, string Name)> SeedAsync(IPage page, string slug, string xml)
    {
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed($"studio-{slug}");
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = $"studio_{slug}", bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");
        return (id, name);
    }

    private static async Task OpenAsync(IPage page, string name)
    {
        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();
    }

    private const string EditorDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="studio_editors" name="Editors" isExecutable="true">
            <bpmn:startEvent id="timer">
              <bpmn:timerEventDefinition><bpmn:timeCycle>0 0 2 * * ?</bpmn:timeCycle></bpmn:timerEventDefinition>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="timer" targetRef="approve" />
            <bpmn:userTask id="approve" name="Approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="approve" targetRef="gate" />
            <bpmn:exclusiveGateway id="gate" name="Decide" />
            <bpmn:serviceTask id="notify" name="Notify" />
            <bpmn:scriptTask id="calc" name="Calculate" scriptFormat="javascript">
              <bpmn:script>return 1;</bpmn:script>
            </bpmn:scriptTask>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="studio_editors">
              <bpmndi:BPMNShape id="Shape_timer" bpmnElement="timer">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_approve" bpmnElement="approve">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_gate" bpmnElement="gate">
                <dc:Bounds x="360" y="95" width="50" height="50" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_notify" bpmnElement="notify">
                <dc:Bounds x="200" y="220" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_calc" bpmnElement="calc">
                <dc:Bounds x="360" y="220" width="100" height="80" />
              </bpmndi:BPMNShape>
              <!-- The EDGES need DI too. Measured: without a BPMNEdge bpmn-js
                   renders no connection at all, so `[data-element-id='f1']`
                   resolved to zero elements while every shape was present. A
                   guard for the connection half would have been vacuous on a
                   diagram that draws no connections. -->
              <bpmndi:BPMNEdge id="Edge_f1" bpmnElement="f1">
                <di:waypoint x="136" y="118" />
                <di:waypoint x="200" y="120" />
              </bpmndi:BPMNEdge>
              <bpmndi:BPMNEdge id="Edge_f2" bpmnElement="f2">
                <di:waypoint x="300" y="120" />
                <di:waypoint x="360" y="120" />
              </bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>Its own diagram: a signal start REPLACES the timer one.</summary>
    private const string SignalStartDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_2" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Signal_1" name="placeholder" />
          <bpmn:process id="studio_edsignal" name="Signal" isExecutable="true">
            <bpmn:startEvent id="sigstart">
              <bpmn:signalEventDefinition signalRef="Signal_1" />
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="sf1" sourceRef="sigstart" targetRef="work" />
            <bpmn:userTask id="work" name="Work" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_2">
            <bpmndi:BPMNPlane id="Plane_2" bpmnElement="studio_edsignal">
              <bpmndi:BPMNShape id="Shape_sigstart" bpmnElement="sigstart">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_work" bpmnElement="work">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNEdge id="Edge_sf1" bpmnElement="sf1">
                <di:waypoint x="136" y="118" />
                <di:waypoint x="200" y="120" />
              </bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }

        return false;
    }
}
