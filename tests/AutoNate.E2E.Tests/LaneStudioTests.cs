using System.Text.Json;
using System.Xml.Linq;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Lanes in the studio (#171): dragging a task into another lane changes its
/// <c>flowNodeRef</c> membership, not only where it is drawn; a lane's group is
/// picked from the groups that exist; and a task in a lane says where its
/// assignment comes from.
/// </summary>
/// <remarks>
/// The flowNodeRef assertion is the primary one. The classic lane bug is a
/// diagram whose lanes look right and whose membership is stale, and it looks
/// completely correct on screen -- so the assertion is on the saved XML, and on
/// the lane the task LEFT as well as the one it entered.
/// </remarks>
public sealed class LaneStudioTests : E2ETestBase
{
    public LaneStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Autonate = "http://autonate.dev/workflows";

    private static string Diagram(string key) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_1" name="Us" processRef="{key}" />
          </bpmn:collaboration>
          <bpmn:process id="{key}" name="Us" isExecutable="true">
            <bpmn:laneSet id="LS_1">
              <bpmn:lane id="Lane_finance" name="Finance">
                <bpmn:flowNodeRef>start</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>approve</bpmn:flowNodeRef>
              </bpmn:lane>
              <bpmn:lane id="Lane_legal" name="Legal">
                <bpmn:flowNodeRef>end</bpmn:flowNodeRef>
              </bpmn:lane>
            </bpmn:laneSet>
            <bpmn:startEvent id="start" />
            <bpmn:sequenceFlow id="f1" sourceRef="start" targetRef="approve" />
            <bpmn:userTask id="approve" name="Approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="approve" targetRef="end" />
            <bpmn:endEvent id="end" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Collab_1">
              <bpmndi:BPMNShape id="Shape_P_1" bpmnElement="P_1" isHorizontal="true">
                <dc:Bounds x="100" y="60" width="700" height="360" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Lane_finance" bpmnElement="Lane_finance" isHorizontal="true">
                <dc:Bounds x="130" y="60" width="670" height="180" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Lane_legal" bpmnElement="Lane_legal" isHorizontal="true">
                <dc:Bounds x="130" y="240" width="670" height="180" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_start" bpmnElement="start">
                <dc:Bounds x="182" y="132" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_approve" bpmnElement="approve">
                <dc:Bounds x="300" y="110" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_end" bpmnElement="end">
                <dc:Bounds x="482" y="312" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNEdge id="Edge_f1" bpmnElement="f1">
                <di:waypoint x="218" y="150" /><di:waypoint x="300" y="150" />
              </bpmndi:BPMNEdge>
              <bpmndi:BPMNEdge id="Edge_f2" bpmnElement="f2">
                <di:waypoint x="400" y="150" /><di:waypoint x="500" y="150" /><di:waypoint x="500" y="312" />
              </bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task<Guid> SeedAsync(IPage page, string name, string key)
    {
        var id = Guid.NewGuid();
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = Diagram(key) }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");
        return id;
    }

    private static async Task OpenInStudioAsync(IPage page, string name)
    {
        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-element-id='approve']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.Locator("[data-element-id='Lane_legal']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private static async Task<XDocument> SaveAndReadBackAsync(IPage page, Guid id)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(3_000);
        var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, await stored.TextAsync());
        using var document = JsonDocument.Parse(await stored.TextAsync());
        return XDocument.Parse(document.RootElement.GetProperty("bpmnXml").GetString()!);
    }

    private static string[] Members(XDocument xml, string laneId) =>
        xml.Descendants(Bpmn + "lane").Single(l => l.Attribute("id")?.Value == laneId)
            .Elements(Bpmn + "flowNodeRef").Select(r => r.Value.Trim()).ToArray();

    [Fact]
    public async Task Dragging_a_task_into_another_lane_moves_its_membership_not_only_its_position()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var key = $"lanes{Guid.NewGuid():N}"[..20];
        var name = TestNames.Prefixed("lane-drag");
        var id = await SeedAsync(page, name, key);
        await OpenInStudioAsync(page, name);

        var task = page.Locator("[data-element-id='approve']");
        var legal = page.Locator("[data-element-id='Lane_legal']");
        var from = (await task.BoundingBoxAsync())!;
        var to = (await legal.BoundingBoxAsync())!;

        // A real drag, through the mouse, so bpmn-js's own move + lane-update
        // behaviour runs -- the thing under test is that it keeps flowNodeRef in
        // step with the shape, not that the XML can be edited to say so.
        var startX = from.X + from.Width / 2;
        var startY = from.Y + from.Height / 2;
        var endX = startX;
        var endY = to.Y + to.Height / 2;
        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(startX + 5, startY + 5);
        await page.Mouse.MoveAsync(endX, endY, new() { Steps = 20 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        var xml = await SaveAndReadBackAsync(page, id);

        // Entered the lane it was dropped in...
        Assert.Contains("approve", Members(xml, "Lane_legal"));
        // ...AND LEFT THE ONE IT CAME FROM. A stale membership is exactly the
        // bug: the diagram would draw the task in Legal and deploy it to Finance.
        Assert.DoesNotContain("approve", Members(xml, "Lane_finance"));

        // The bounds moved too, which is the half that always worked.
        var shape = xml.Descendants().Single(e => e.Name.LocalName == "BPMNShape" && e.Attribute("bpmnElement")?.Value == "approve");
        var y = double.Parse(shape.Elements().Single(e => e.Name.LocalName == "Bounds").Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(y >= 240, $"the task's shape should now sit in the Legal lane (y >= 240), but y = {y}");
    }

    [Fact]
    public async Task A_lane_picks_its_group_from_the_groups_that_exist_and_the_choice_is_saved()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var groupName = TestNames.Prefixed("lane-group");
        var group = await page.APIRequest.PostAsync("/api/admin/groups",
            new APIRequestContextOptions { DataObject = new { name = groupName, description = (string?)null } });
        Assert.True(group.Ok, await group.TextAsync());
        var groupId = JsonDocument.Parse(await group.TextAsync()).RootElement.GetProperty("id").GetString()!;

        var key = $"laneg{Guid.NewGuid():N}"[..20];
        var name = TestNames.Prefixed("lane-group-pick");
        var id = await SeedAsync(page, name, key);
        await OpenInStudioAsync(page, name);

        // Right-click the lane by its label strip, clear of the task inside it.
        var lane = page.Locator("[data-element-id='Lane_finance']");
        await lane.ClickAsync(new() { Button = MouseButton.Right, Position = new() { X = 12, Y = 30 } });
        await page.GetByText("Configure", new() { Exact = false }).First.ClickAsync(new() { Timeout = 10_000 });

        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(dialog.GetByText("Lane", new() { Exact = true }).First).ToBeVisibleAsync();

        // A picker over the groups that exist, not a text field.
        var picker = dialog.GetByRole(AriaRole.Textbox, new() { Name = "Group" });
        await picker.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = groupName, Exact = true }).ClickAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        var xml = await SaveAndReadBackAsync(page, id);
        var saved = xml.Descendants(Bpmn + "lane").Single(l => l.Attribute("id")?.Value == "Lane_finance");
        Assert.Equal(groupId, saved.Attribute(Autonate + "groupId")?.Value);
        // And the lane's contents survived the round trip with it.
        Assert.Contains("approve", Members(xml, "Lane_finance"));
    }

    [Fact]
    public async Task A_task_in_a_lane_says_its_assignment_comes_from_the_lane()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var groupName = TestNames.Prefixed("lane-src");
        var group = await page.APIRequest.PostAsync("/api/admin/groups",
            new APIRequestContextOptions { DataObject = new { name = groupName, description = (string?)null } });
        Assert.True(group.Ok, await group.TextAsync());
        var groupId = JsonDocument.Parse(await group.TextAsync()).RootElement.GetProperty("id").GetString()!;

        var key = $"lanes{Guid.NewGuid():N}"[..20];
        var name = TestNames.Prefixed("lane-source");
        var id = Guid.NewGuid();
        var xml = Diagram(key).Replace("<bpmn:lane id=\"Lane_finance\" name=\"Finance\">",
            $"<bpmn:lane id=\"Lane_finance\" name=\"Finance\" autonate:groupId=\"{groupId}\">", StringComparison.Ordinal);
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());
        await OpenInStudioAsync(page, name);

        var task = page.Locator("[data-element-id='approve']");
        await task.ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First.ClickAsync(new() { Timeout = 10_000 });
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Names the lane AND the group, so an author knows which one won and why.
        await Assertions.Expect(dialog.GetByText("Assignment source: the lane", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(dialog.GetByText(groupName, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}
