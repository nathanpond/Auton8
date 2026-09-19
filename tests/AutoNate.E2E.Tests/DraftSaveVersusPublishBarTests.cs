using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A draft save and a publish do not want the same bar (#234).
/// </summary>
/// <remarks>
/// <para>
/// They shared one. <c>prepareAndStore</c> returned null on any validation
/// error, and both call sites bailed on it, so every publish-time refusal also
/// refused a draft save -- an author who dropped an unconfigured element on the
/// canvas could not store their work in progress. Nobody chose that: #225 moved
/// the full validation set onto publish, save went through the same call, and
/// each rule promoted to a publish refusal joined the save refusals silently.
/// </para>
/// <para>
/// Both halves are here on purpose. "Save stores it" alone would pass against a
/// studio that had simply stopped validating; "publish refuses it" alone would
/// pass against the old shared bar. It is the pair that pins the split, and the
/// diagram is the SAME one in both -- one rule violation, two verdicts.
/// </para>
/// <para>
/// Deliberately not <c>RequiresService=Flowable</c>: the publish half is refused
/// in the browser before any request leaves it, and the save half never reaches
/// the engine. This runs in slim, which is where a regression on the merge gate
/// wants catching.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class DraftSaveVersusPublishBarTests : E2ETestBase
{
    public DraftSaveVersusPublishBarTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private static string ProcessKey() => "dsp_" + Guid.NewGuid().ToString("n")[..12];

    /// <summary>
    /// Well-formed, and unpublishable: a timer start event with no schedule can
    /// never fire, so <c>ValidateProcess</c> refuses it. The XML itself parses
    /// cleanly, which is the whole point -- this is the "half-built" class, not
    /// the "unreadable" one.
    /// </summary>
    private static string UnscheduledTimerDiagram(string processKey) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{processKey}" isExecutable="true">
            <bpmn:startEvent id="start">
              <bpmn:timerEventDefinition id="timer" />
            </bpmn:startEvent>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="d">
            <bpmndi:BPMNPlane id="p" bpmnElement="{processKey}">
              <bpmndi:BPMNShape id="start_di" bpmnElement="start">
                <dc:Bounds x="150" y="150" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>Polls a condition for up to 15s, for state a Playwright event sets.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }

        return false;
    }

    [Fact]
    public async Task A_diagram_that_publish_refuses_still_saves_as_a_draft()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var name = TestNames.Prefixed("draft-save-bar");
        var processKey = ProcessKey();
        var id = Guid.NewGuid();

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey, bpmnXml = UnscheduledTimerDiagram(processKey) }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        // Count what actually left the browser. The old failure mode was an
        // ABSENT request -- the studio reported the error and simply did not
        // POST -- so a status-text assertion alone would not have located it.
        var saves = 0;
        var publishes = 0;
        var posts = new System.Collections.Concurrent.ConcurrentBag<string>();
        page.Request += (_, request) =>
        {
            if (!string.Equals(request.Method, "POST", StringComparison.Ordinal)) return;
            posts.Add(request.Url);
            if (request.Url.Contains($"/api/workflows/{id}/publish", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref publishes);
            }
            else if (request.Url.EndsWith("/api/workflows", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref saves);
            }
        };

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-element-id='start']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // --- Save: stores the work, and still says what is wrong with it. ---
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Saved workflow model", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Polled, not read once: Playwright dispatches Request events over its
        // own connection, so the status text can land in the DOM before the
        // event reaches this process. A single read here failed against a save
        // that had demonstrably happened.
        Assert.True(
            await WaitForAsync(() => Volatile.Read(ref saves) > 0),
            "The studio never POSTed the draft -- the save did not happen. POSTs seen: "
                + string.Join(" | ", posts));

        // The errors are reported, not swallowed. A save that stored the draft
        // by discarding the diagnostics would pass the line above while making
        // the studio quieter about a real problem.
        await Assertions.Expect(page.GetByText("recurrence schedule", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And it reached the database: a status message is the studio's claim,
        // the stored model is the fact.
        var stored = await page.APIRequest.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, $"Reading the saved model back failed: {stored.Status}");
        Assert.Contains("timerEventDefinition", await stored.TextAsync(), StringComparison.Ordinal);

        // --- Publish: the complement. Same diagram, refused. ---
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("recurrence schedule", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Give a publish that was going to happen time to happen, so the zero
        // below means "refused", not "not yet" -- the same event lag that makes
        // the save assertion poll makes an immediate zero meaningless here.
        await page.WaitForTimeoutAsync(5_000);
        Assert.Equal(0, Volatile.Read(ref publishes));
    }
}
