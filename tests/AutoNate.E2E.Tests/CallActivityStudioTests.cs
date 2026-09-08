using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An author picks the called workflow and maps variables in the studio (#113).
/// </summary>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class CallActivityStudioTests : E2ETestBase
{
    public CallActivityStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_called_workflow_is_chosen_from_published_ones_and_mappings_round_trip()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // A published workflow to pick, and the parent that will call it.
        var childKey = $"cas_c_{Guid.NewGuid():N}"[..22];
        var childName = TestNames.Prefixed(childKey);
        await PublishChildAsync(page.APIRequest, childKey, childName);

        var parentName = TestNames.Prefixed("call-activity-studio");
        var parentId = Guid.NewGuid();
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = parentId,
                name = parentName,
                processKey = "call_activity_studio",
                bpmnXml = ParentDiagram
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = parentName, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Call_1");

        // Chosen from a list of published workflows — not typed.
        var picker = page.GetByLabel("Workflow to run", new() { Exact = true });
        await Assertions.Expect(picker).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await picker.SelectOptionAsync(new SelectOptionValue { Value = childKey });

        await page.GetByRole(AriaRole.Button, new() { Name = "Add send in", Exact = false }).ClickAsync();
        await page.GetByLabel("Send in source 1", new() { Exact = true }).FillAsync("orderId");
        await page.GetByLabel("Send in target 1", new() { Exact = true }).FillAsync("childOrderId");

        await page.GetByRole(AriaRole.Button, new() { Name = "Add bring back", Exact = false }).ClickAsync();
        await page.GetByLabel("Bring back source 1", new() { Exact = true }).FillAsync("childOrderId");
        await page.GetByLabel("Bring back target 1", new() { Exact = true }).FillAsync("returned");

        await Assertions.Expect(picker).ToHaveValueAsync(childKey, new() { Timeout = 5_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();

        // The modal closing is how Apply reports success. Waiting for it here
        // rather than clicking Save straight away caught the real bug this test
        // found: Apply was throwing, the modal stayed open over the Save button,
        // and the failure read as "Save is not clickable".
        await Assertions.Expect(page.GetByLabel("Workflow to run", new() { Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var saved = await ReadSavedXmlAsync(page.APIRequest, parentId,
            xml => xml.Contains($"calledElement=\"{childKey}\"", StringComparison.Ordinal),
            "the chosen workflow to reach the saved diagram");

        // The KEY is stored, not a pinned version — pinning happens at publish, so
        // the studio keeps showing the author what they picked.
        Assert.Contains($"calledElement=\"{childKey}\"", saved);
        Assert.DoesNotContain("calledElementType", saved);

        // Both mappings survived, in the right direction.
        Assert.Contains("source=\"orderId\"", saved);
        Assert.Contains("target=\"childOrderId\"", saved);
        Assert.Contains("target=\"returned\"", saved);

        // And they come back — the read path, which a write-only test leaves
        // entirely unexercised.
        await page.ReloadAsync();
        var reloaded = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(reloaded).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await reloaded.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = parentName, Exact = true }).ClickAsync();

        await OpenConfigureAsync(page, "Call_1");
        await Assertions.Expect(page.GetByLabel("Workflow to run", new() { Exact = true }))
            .ToHaveValueAsync(childKey, new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByLabel("Send in source 1", new() { Exact = true }))
            .ToHaveValueAsync("orderId");
        await Assertions.Expect(page.GetByLabel("Bring back target 1", new() { Exact = true }))
            .ToHaveValueAsync("returned");
    }

    [Fact]
    public async Task A_waiting_parent_shows_its_called_workflow_and_can_open_it()
    {
        // The complaint this story opens with: while a parent waits on a call
        // activity its own task list is empty, so the execution view showed a
        // process that appeared hung with nothing to open.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        var childKey = $"cav_c_{Guid.NewGuid():N}"[..22];
        var parentKey = $"cav_p_{Guid.NewGuid():N}"[..22];
        await PublishChildAsync(api, childKey, TestNames.Prefixed(childKey));
        await PublishParentAsync(api, parentKey, childKey);

        var start = await api.PostAsync($"/api/workflows/{parentKey}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { orderId = "ORD-1" } }
        });
        Assert.True(start.Ok, $"Starting failed: {start.Status} {await start.TextAsync()}");
        using var startDoc = JsonDocument.Parse(await start.TextAsync());
        var parentInstance = startDoc.RootElement.GetProperty("id").GetString()!;

        await page.GotoAsync($"/executions/{parentInstance}");

        // The tab exists, is labelled with the count, and opens the child.
        var tab = page.GetByRole(AriaRole.Tab, new() { Name = "Called Workflows (1)" });
        await Assertions.Expect(tab).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await tab.ClickAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Open" }).First.ClickAsync();

        // Navigated to the child, which is where the work actually is.
        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex($"/executions/(?!{parentInstance})"),
            new() { Timeout = 15_000 });
    }

    private static async Task PublishParentAsync(IAPIRequestContext api, string key, string childKey)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var xml = ParentDiagram
            .Replace("call_activity_studio", key, StringComparison.Ordinal)
            .Replace("<bpmn:callActivity id=\"Call_1\" name=\"Sub\" />",
                     $"<bpmn:callActivity id=\"Call_1\" name=\"Sub\" calledElement=\"{childKey}\" />",
                     StringComparison.Ordinal);

        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the parent failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing the parent failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task PublishChildAsync(IAPIRequestContext api, string key, string displayName)
    {
        var id = Guid.NewGuid();
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Child" isExecutable="true">
                <bpmn:startEvent id="cs" />
                <bpmn:sequenceFlow id="cf0" sourceRef="cs" targetRef="ct" />
                <bpmn:userTask id="ct" name="Child work" />
              </bpmn:process>
              <bpmndi:BPMNDiagram id="D1">
                <bpmndi:BPMNPlane id="P1" bpmnElement="{{key}}">
                  <bpmndi:BPMNShape id="S_cs" bpmnElement="cs">
                    <dc:Bounds x="100" y="100" width="36" height="36" />
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="S_ct" bpmnElement="ct">
                    <dc:Bounds x="200" y="80" width="100" height="80" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the child failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing the child failed: {published.Status} {await published.TextAsync()}");
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

    private const string ParentDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="call_activity_studio" name="Call Studio" isExecutable="true">
            <bpmn:startEvent id="Start_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Call_1" />
            <bpmn:callActivity id="Call_1" name="Sub" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Call_1" targetRef="After_1" />
            <bpmn:userTask id="After_1" name="After" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="call_activity_studio">
              <bpmndi:BPMNShape id="S_Start" bpmnElement="Start_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_Call" bpmnElement="Call_1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_After" bpmnElement="After_1">
                <dc:Bounds x="360" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
