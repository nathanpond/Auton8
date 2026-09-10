using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The palette an author actually sees is the manifest's (#241).
/// </summary>
/// <remarks>
/// <para>
/// <c>BpmnPaletteManifestTests</c> checks that the catalog and the manifest agree.
/// This checks the half that failed silently for the whole of #107: that the
/// derived provider is what the studio renders. The array #107 shipped as "the
/// palette" was never imported by anything, so every claim about palette contents
/// was true of a module with no consumers, while authors looked at bpmn-js's stock
/// palette instead.
/// </para>
/// <para>
/// Deliberately not <c>RequiresService=Flowable</c>: nothing here publishes, so it
/// runs everywhere CI runs — which is the point, since the drift it guards is a
/// build-time property and CI is exactly where you want to catch it.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowPaletteTests : E2ETestBase
{
    public WorkflowPaletteTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private sealed record CatalogEntry(string Id, string LocalName, string? EventDefinition);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "shared")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!;
    }

    private static string Key(string localName, string? eventDefinition) =>
        $"{localName} {eventDefinition ?? string.Empty}";

    /// <summary>Catalog entries split by what the manifest says about each.</summary>
    private static (IReadOnlyList<string> Offered, IReadOnlyList<string> Withheld) PartitionCatalog()
    {
        var root = RepoRoot();

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, "src", "shared", "bpmn-support.json")));
        var supported = manifest.RootElement.GetProperty("elements").EnumerateArray()
            .Where(element => element.GetProperty("studio").GetString() == "supported")
            .Select(element => Key(
                element.GetProperty("localName").GetString()!,
                element.GetProperty("eventDefinition").GetString()))
            .ToHashSet(StringComparer.Ordinal);

        using var catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, "src", "shared", "bpmn-palette.json")));
        var entries = catalog.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => new CatalogEntry(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("localName").GetString()!,
                entry.GetProperty("eventDefinition").GetString()))
            .ToList();

        return (
            entries.Where(e => supported.Contains(Key(e.LocalName, e.EventDefinition)))
                .Select(e => e.Id).ToList(),
            entries.Where(e => !supported.Contains(Key(e.LocalName, e.EventDefinition)))
                .Select(e => e.Id).ToList());
    }

    /// <summary>A process key unique to this run; the column is unique-constrained.</summary>
    private static string ProcessKey() => "pal_" + Guid.NewGuid().ToString("n")[..12];

    private static string EmptyDiagram(string processKey) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{processKey}" isExecutable="true">
            <bpmn:startEvent id="start" />
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

    /// <summary>
    /// Opens the studio on a diagram of its own.
    /// </summary>
    /// <remarks>
    /// The modeler — and so the palette — mounts only once a model is open, and
    /// which model that is persists across visits. Seeding one per test keeps the
    /// palette assertions from depending on what an earlier test left selected.
    /// </remarks>
    private static async Task<IPage> OpenStudioAsync(SignedInSession session, string label)
    {
        var page = session.Page;
        var name = TestNames.Prefixed(label);
        var processKey = ProcessKey();
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = Guid.NewGuid(),
                name,
                processKey,
                bpmnXml = EmptyDiagram(processKey)
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

        await Assertions.Expect(page.Locator("[data-element-id='start']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.Locator(".djs-palette"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        return page;
    }

    [Fact]
    public async Task The_rendered_palette_offers_every_supported_element_and_nothing_withheld()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = await OpenStudioAsync(session, "palette-contents");

        var (offered, withheld) = PartitionCatalog();
        Assert.NotEmpty(offered);
        Assert.NotEmpty(withheld);

        // A provider that throws during getPaletteEntries leaves bpmn-js with an
        // empty palette, and every "is not offered" assertion below then passes
        // for the wrong reason. Anchor on a real count first.
        var rendered = await page.Locator(".djs-palette .entry[data-action]")
            .EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.getAttribute('data-action'))");
        Assert.True(
            rendered.Length >= offered.Count,
            $"The palette rendered {rendered.Length} entries; the manifest offers " +
            $"{offered.Count}. Rendered: {string.Join(", ", rendered)}");

        var renderedSet = rendered.ToHashSet(StringComparer.Ordinal);

        var missing = offered.Where(id => !renderedSet.Contains(id)).ToList();
        Assert.True(
            missing.Count == 0,
            $"Supported elements with no palette entry rendered: {string.Join(", ", missing)}");

        // The direction that was inverted when #241 was filed: elements the studio
        // offers and publish then refuses. An author must not be able to draw one.
        var offeredAnyway = withheld.Where(renderedSet.Contains).ToList();
        Assert.True(
            offeredAnyway.Count == 0,
            "The palette offers elements the manifest does not call supported, so " +
            $"an author can draw what publish will refuse: {string.Join(", ", offeredAnyway)}");
    }

    [Fact]
    public async Task The_navigation_tools_survive_overriding_the_stock_provider()
    {
        // Replacing bpmn-js's paletteProvider replaces ALL of it, tools included.
        // Losing the hand tool is not a BPMN-coverage regression, so nothing else
        // in this milestone would notice it.
        await using var session = await NewSignedInAsAdminAsync();
        var page = await OpenStudioAsync(session, "palette-tools");

        foreach (var tool in new[] { "hand-tool", "lasso-tool", "space-tool", "global-connect-tool" })
        {
            await Assertions.Expect(page.Locator($".djs-palette .entry[data-action='{tool}']"))
                .ToHaveCountAsync(1);
        }
    }

    [Theory]
    [InlineData("create.adhoc-sub-process", "adHocSubProcess", null)]
    [InlineData("create.intermediate-throw-compensation", "intermediateThrowEvent", "compensateEventDefinition")]
    [InlineData("create.user-task", "userTask", null)]
    public async Task An_offered_entry_places_the_element_it_advertises(
        string entryId, string localName, string? eventDefinition)
    {
        // The catalog's `type` and `eventDefinitionType` are arguments to
        // elementFactory.createShape. A wrong one does not fail a JSON check --
        // it places the wrong element, or throws on click and leaves the canvas
        // untouched, and a bare count of shapes cannot tell those apart.
        await using var session = await NewSignedInAsAdminAsync();

        // The studio serialises the diagram and POSTs it to /prepare before it
        // will save, so this is bpmn-js's own output -- the evidence wanted here
        // -- and it is available whether or not the diagram then passes
        // validation. It has to be: an ad-hoc sub-process with no activities in it
        // is correctly refused at prepare, so a save-then-read assertion could
        // never see the element that #163 shipped.
        var prepared = new List<string>();
        session.Page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/workflows/prepare", StringComparison.Ordinal))
            {
                prepared.Add(request.PostData ?? string.Empty);
            }
        };

        var page = await OpenStudioAsync(session, "palette-place");

        var before = await page.Locator(".djs-container .djs-element").CountAsync();

        await page.Locator($".djs-palette .entry[data-action='{entryId}']").ClickAsync();
        var canvas = page.Locator(".djs-container svg").First;
        await canvas.ClickAsync(new LocatorClickOptions { Position = new Position { X = 420, Y = 300 } });

        await Assertions.Expect(page.Locator(".djs-container .djs-element"))
            .ToHaveCountAsync(before + 1, new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForTimeoutAsync(3_000);

        Assert.True(
            prepared.Count > 0,
            "The studio never posted the diagram to /prepare, so there is nothing " +
            "to read the placed element out of.");

        var xml = prepared[^1];

        Assert.True(
            xml.Contains($"bpmn:{localName}", StringComparison.Ordinal),
            $"Expected a bpmn:{localName} in the diagram the studio serialised " +
            $"after placing '{entryId}'. Got:{Environment.NewLine}{xml}");

        if (eventDefinition is not null)
        {
            // Without this, every event entry passes on the bare element name --
            // a compensation throw and a plain throw are the same localName, and
            // the event definition is the entire difference between them.
            Assert.True(
                xml.Contains($"bpmn:{eventDefinition}", StringComparison.Ordinal),
                $"Expected a bpmn:{eventDefinition} on the placed '{entryId}'. " +
                $"Got:{Environment.NewLine}{xml}");
        }
    }
}
