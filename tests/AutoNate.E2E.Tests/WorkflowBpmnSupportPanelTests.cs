using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The BPMN support panel says what is true, and a diagram we will not publish
/// still opens (#107).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>RequiresService=Flowable</c>, unlike
/// <see cref="WorkflowStudioTests"/>: nothing here publishes. Both assertions are
/// about the studio's own behaviour, so they run everywhere CI runs.
/// </para>
/// <para>
/// The panel's content is asserted against the manifest rather than snapshotted,
/// so a change to one fails without the other — a snapshot would have to be
/// re-blessed on every element that lands, which is how a snapshot stops being a
/// check and becomes a chore.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowBpmnSupportPanelTests : E2ETestBase
{
    public WorkflowBpmnSupportPanelTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private sealed record ManifestElement(
        string Name, string Category, string Studio, string Engine, string? Reason);

    private static IReadOnlyList<ManifestElement> Manifest()
    {
        // Walk up to the repo root rather than hard-coding a depth, which changes
        // whenever the target framework or configuration folder does.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "shared")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "shared", "bpmn-support.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("elements").EnumerateArray()
            .Select(element => new ManifestElement(
                element.GetProperty("name").GetString()!,
                element.GetProperty("category").GetString()!,
                element.GetProperty("studio").GetString()!,
                element.GetProperty("engine").GetString()!,
                element.GetProperty("reason").GetString()))
            .ToArray();
    }

    [Fact]
    public async Task The_panel_shows_what_the_manifest_says_and_hides_what_was_withdrawn()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "BPMN element support" }).ClickAsync();
        var panel = page.GetByRole(AriaRole.Dialog, new() { Name = "BPMN element support" });
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var manifest = Manifest();
        Assert.NotEmpty(manifest);

        // Everything the manifest offers is listed. Sampling would let a whole
        // category go missing, which is the failure mode a hand-kept list had.
        foreach (var element in manifest.Where(e => e.Studio != "withdrawn"))
        {
            await Assertions.Expect(panel.GetByText(element.Name, new() { Exact = true }).First)
                .ToBeVisibleAsync(new() { Timeout = 5_000 });
        }

        // And nothing withdrawn is: advertising an element the engine has never
        // heard of is the exact dishonesty this story removes. The owner descoped
        // the compensation start event on 2026-09-07 because Flowable 8.0.0 ships
        // no behaviour for it.
        foreach (var element in manifest.Where(e => e.Studio == "withdrawn"))
        {
            await Assertions.Expect(panel.GetByText(element.Name, new() { Exact = true }))
                .ToHaveCountAsync(0);
        }

        // An element that cannot run is shown with its reason, not merely omitted
        // from the supported column — an author who reaches for it gets an answer.
        var unavailable = manifest
            .Where(e => e.Studio == "coming-soon" && e.Engine == "cannot-execute")
            .ToArray();
        Assert.NotEmpty(unavailable);
        foreach (var element in unavailable)
        {
            await Assertions.Expect(panel.GetByText(element.Reason!, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
    }

    [Fact]
    public async Task A_diagram_using_an_element_we_will_not_publish_still_opens_and_renders()
    {
        // The regression a naive validation change causes, and the one nobody
        // tests for, because you do not reach for a diagram you can no longer
        // publish. Loading and publishing are different paths: validation runs on
        // prepare, never on load, and this proves the load path was left alone.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("legacy-complex-gateway");

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = Guid.NewGuid(),
                name,
                processKey = "legacy_complex_gateway",
                bpmnXml = LegacyDiagram
            }
        });
        Assert.True(created.Ok, $"Seeding the legacy diagram failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        // The modeller drew it: bpmn-js renders each element into the SVG canvas
        // with its id as a data attribute. If the studio had refused the diagram,
        // this shape would never appear.
        await Assertions.Expect(page.Locator("[data-element-id='Gateway_1']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.Locator("[data-element-id='Task_1']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private const string LegacyDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="legacy_complex_gateway" name="Legacy" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1" />
            <bpmn:complexGateway id="Gateway_1" name="Two of three" />
            <bpmn:userTask id="Task_1" name="Approve" />
            <bpmn:endEvent id="EndEvent_1" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="legacy_complex_gateway">
              <bpmndi:BPMNShape id="Shape_Start" bpmnElement="StartEvent_1">
                <dc:Bounds x="150" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Gateway" bpmnElement="Gateway_1">
                <dc:Bounds x="240" y="93" width="50" height="50" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Task" bpmnElement="Task_1">
                <dc:Bounds x="350" y="78" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_End" bpmnElement="EndEvent_1">
                <dc:Bounds x="510" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
