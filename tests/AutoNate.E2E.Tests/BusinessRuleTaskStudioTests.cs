using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The decision an author picks survives the studio (#111).
/// </summary>
/// <remarks>
/// <para>
/// <b>This cannot be established by reading the code.</b> bpmn-js silently drops
/// what its moddle does not model — three separate times in M4, each verified in a
/// browser rather than reasoned about — so whether an <c>autonate:decisionKey</c>
/// survives a save is a property of the vendored bundle, not of our code. The
/// author's configuration disappearing on their next save is the failure this
/// exists to catch.
/// </para>
/// <para>
/// Untraited: the round trip needs no engine. The execution half is
/// <c>BusinessRuleTaskExecutionTests</c>.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class BusinessRuleTaskStudioTests : E2ETestBase
{
    public BusinessRuleTaskStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_decision_table_an_author_picks_survives_the_round_trip()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        // A published table for the picker to offer. Unpublished ones are
        // deliberately not offered -- they are not deployed, so a process
        // referencing one would fail.
        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        await PublishTableAsync(api, decisionKey);

        var processKey = "e2e_brs_" + Guid.NewGuid().ToString("n")[..10];
        var name = TestNames.Prefixed("brt-studio");
        var modelId = Guid.NewGuid();

        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = modelId, name, processKey,
                bpmnXml = Diagram(processKey)
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        // What bpmn-js SERIALISED is the evidence, and /prepare carries it.
        var prepared = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/workflows/prepare", StringComparison.Ordinal))
            {
                prepared.Add(request.PostData ?? string.Empty);
            }
        };

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("[data-element-id='decide']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Reached the way an author reaches it: right-click, Configure. Selecting
        // an element opens nothing, so a test that clicked would never get here.
        await page.Locator("[data-element-id='decide']").ClickAsync(new() { Button = MouseButton.Right });
        await page.GetByText("Configure", new() { Exact = false }).First.ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Business Rule Task" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // PICKED, not typed. The control is a select over published tables.
        var picker = page.GetByLabel("Decision table", new() { Exact = true });
        await picker.SelectOptionAsync(new SelectOptionValue { Value = decisionKey });
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // FIRST, what bpmn-js SERIALISED. /prepare carries the modeller's own
        // output, and this is the claim that cannot be established by reading:
        // bpmn-js silently drops what its moddle does not model, so whether an
        // `autonate:` attribute survives a save is a property of the vendored
        // bundle. It does.
        Assert.True(
            await WaitForAsync(() => prepared.Count > 0),
            "The studio never posted the diagram to /prepare, so there is nothing to read "
                + "bpmn-js's serialisation out of.");

        Assert.Contains($"autonate:decisionKey=\\\"{decisionKey}\\\"", prepared[^1],
            StringComparison.Ordinal);

        // THEN, that it reached the database. Polled rather than read once: the
        // first version of this test read the stored model while the save was
        // still in flight and failed against a save that then succeeded -- the
        // status text had appeared but the row had not been written yet.
        var stored = string.Empty;
        var landed = await WaitForAsync(async () =>
        {
            var response = await api.GetAsync($"/api/workflows/{modelId}");
            if (!response.Ok) return false;
            using var document = JsonDocument.Parse(await response.TextAsync());
            stored = document.RootElement.GetProperty("bpmnXml").GetString() ?? string.Empty;
            return stored.Contains("decisionKey", StringComparison.Ordinal);
        });

        Assert.True(landed,
            "The decision table never reached the stored diagram. bpmn-js serialised it "
                + $"(see /prepare), so it was lost after that. Stored XML was:\n{stored}");

        Assert.Contains($"decisionKey=\"{decisionKey}\"", stored, StringComparison.Ordinal);

        // And the element is still a business rule task in the STORED diagram --
        // the expansion is on the deploy path only, so an author reopening this
        // sees what they drew.
        Assert.Contains("businessRuleTask", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("serviceTask", stored, StringComparison.Ordinal);
    }

    /// <summary>Polls a condition for up to 20s.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition) =>
        await WaitForAsync(() => Task.FromResult(condition()));

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }

        return false;
    }

    private static string Diagram(string processKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" isExecutable="true">
            <bpmn:startEvent id="start" />
            <bpmn:sequenceFlow id="f0" sourceRef="start" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Decide" />
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="done" />
            <bpmn:endEvent id="done" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="d">
            <bpmndi:BPMNPlane id="p" bpmnElement="{{processKey}}">
              <bpmndi:BPMNShape id="start_di" bpmnElement="start">
                <dc:Bounds x="150" y="150" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="decide_di" bpmnElement="decide">
                <dc:Bounds x="250" y="130" width="120" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="done_di" bpmnElement="done">
                <dc:Bounds x="450" y="150" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task PublishTableAsync(IAPIRequestContext api, string decisionKey)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey,
                name = "Studio routing",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_amount", label = "Amount", name = "amount", typeRef = "number" }
                },
                outputs = new[]
                {
                    new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
                },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "" }, outputEntries = new[] { "\"auto\"" } }
                }
            }
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;
        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok,
            $"Publishing the table failed: {published.Status} {await published.TextAsync()}");
    }
}
