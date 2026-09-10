using System.Text.Json;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// The palette agrees with the BPMN support manifest (#241).
/// </summary>
/// <remarks>
/// <para>
/// This is the test #107's AC1 promised and did not write: "the palette, the
/// types modal and publish validation derive from one source, with a test that
/// fails if any consumer disagrees". The modal and the validation did derive from
/// it. The palette was a 51-entry array in <c>workflow.js</c> that nothing
/// imported, so what an author actually saw was bpmn-js's stock palette, which
/// consults nothing.
/// </para>
/// <para>
/// By the time it was measured the two had drifted in both directions at once:
/// the palette offered Cancel End, Transaction and Business Rule Task, each of
/// which publish then refuses with a validation error, and it had no entry at all
/// for the ad-hoc sub-process that #163 shipped and the manifest calls
/// <c>supported</c>. That is #103's founding complaint inverted — not "offered and
/// does nothing" but "offered and then refused", plus "works and cannot be
/// reached".
/// </para>
/// <para>
/// These tests run in CI: no engine, no browser. The complement — that the
/// provider is what the studio actually renders, rather than a module nothing
/// imports, which is precisely how the old array died — is
/// <c>WorkflowPaletteTests</c>.
/// </para>
/// </remarks>
public sealed class BpmnPaletteManifestTests
{
    private sealed record PaletteEntry(
        string Id, string Label, string Group, string LocalName, string? EventDefinition);

    private sealed record Exclusion(string LocalName, string? EventDefinition, string Reason);

    private sealed record ManifestElement(
        string Name, string Studio, string LocalName, string? EventDefinition);

    private static string PalettePath =>
        Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-palette.json");

    private static string ManifestPath =>
        Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-support.json");

    private static string ProviderPath => Path.Combine(
        RepoRoot.Path, "src", "AutoNate.Spa", "src", "lib", "bpmn", "palette.js");

    private static string ModelerPath => Path.Combine(
        RepoRoot.Path, "src", "AutoNate.Spa", "src", "lib", "bpmn", "workflow.js");

    private static JsonDocument PaletteDocument() =>
        JsonDocument.Parse(File.ReadAllText(PalettePath));

    private static IReadOnlyList<PaletteEntry> Entries()
    {
        using var document = PaletteDocument();
        return document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => new PaletteEntry(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("label").GetString()!,
                entry.GetProperty("group").GetString()!,
                entry.GetProperty("localName").GetString()!,
                entry.GetProperty("eventDefinition").GetString()))
            .ToArray();
    }

    private static IReadOnlyList<Exclusion> Exclusions()
    {
        using var document = PaletteDocument();
        return document.RootElement.GetProperty("notOnThePalette").EnumerateArray()
            .Select(entry => new Exclusion(
                entry.GetProperty("localName").GetString()!,
                entry.GetProperty("eventDefinition").GetString(),
                entry.GetProperty("reason").GetString()!))
            .ToArray();
    }

    private static IReadOnlyList<ManifestElement> Manifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        return document.RootElement.GetProperty("elements").EnumerateArray()
            .Select(element => new ManifestElement(
                element.GetProperty("name").GetString()!,
                element.GetProperty("studio").GetString()!,
                element.GetProperty("localName").GetString()!,
                element.GetProperty("eventDefinition").GetString()))
            .ToArray();
    }

    private static string Key(string localName, string? eventDefinition) =>
        $"{localName} {eventDefinition ?? string.Empty}";

    // ── The join holds ──────────────────────────────────────────────────────

    [Fact]
    public void Every_palette_entry_names_a_manifest_element()
    {
        var known = Manifest().Select(element => Key(element.LocalName, element.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = Entries()
            .Where(entry => !known.Contains(Key(entry.LocalName, entry.EventDefinition)))
            .Select(entry => $"{entry.Id} -> {Key(entry.LocalName, entry.EventDefinition)}")
            .ToList();

        // An entry whose key does not resolve is silently dropped by the
        // provider's filter, so the element vanishes from the palette with no
        // error anywhere. A typo in `localName` must fail here or not at all.
        Assert.True(
            orphans.Count == 0,
            "Palette entries naming a bpmn-support.json element that does not " +
            $"exist:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", orphans)}");
    }

    [Fact]
    public void Every_supported_element_is_either_on_the_palette_or_excluded_with_a_reason()
    {
        var placeable = Entries()
            .Select(entry => Key(entry.LocalName, entry.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);
        var excluded = Exclusions()
            .Select(exclusion => Key(exclusion.LocalName, exclusion.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);

        var unreachable = Manifest()
            .Where(element => element.Studio == "supported")
            .Where(element => !placeable.Contains(Key(element.LocalName, element.EventDefinition)))
            .Where(element => !excluded.Contains(Key(element.LocalName, element.EventDefinition)))
            .Select(element => $"{element.Name} ({Key(element.LocalName, element.EventDefinition)})")
            .ToList();

        // This is the half that caught the ad-hoc sub-process: #163 shipped it,
        // the manifest called it supported, and no author could place it. A
        // supported element an author cannot reach is not supported.
        Assert.True(
            unreachable.Count == 0,
            "Elements the manifest calls supported that have no palette entry and " +
            "no stated reason in notOnThePalette. Add an entry, or a reason saying " +
            $"how an author reaches it:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", unreachable));
    }

    [Fact]
    public void Every_exclusion_names_a_manifest_element_that_is_not_also_on_the_palette()
    {
        var known = Manifest().Select(element => Key(element.LocalName, element.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);
        var placeable = Entries()
            .Select(entry => Key(entry.LocalName, entry.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var exclusion in Exclusions())
        {
            var key = Key(exclusion.LocalName, exclusion.EventDefinition);

            Assert.True(
                known.Contains(key),
                $"notOnThePalette names '{key}', which no bpmn-support.json element " +
                "matches. A stale exclusion silently excuses nothing and hides the " +
                "next real gap.");

            // Otherwise an entry could be added without its excuse being removed,
            // and the excuse would then be excusing an element that IS offered.
            Assert.False(
                placeable.Contains(key),
                $"'{key}' is both a palette entry and excluded from the palette.");

            Assert.False(
                string.IsNullOrWhiteSpace(exclusion.Reason),
                $"notOnThePalette entry '{key}' has no reason. The reason is the " +
                "whole value of the list: it says how the author reaches the " +
                "element instead.");
        }
    }

    [Fact]
    public void Every_entry_belongs_to_a_declared_group_and_has_a_unique_id()
    {
        using var document = PaletteDocument();
        var groups = document.RootElement.GetProperty("groupOrder").EnumerateArray()
            .Select(group => group.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var entries = Entries();
        foreach (var entry in entries)
        {
            Assert.True(
                groups.Contains(entry.Group),
                $"Palette entry '{entry.Id}' is in group '{entry.Group}', which is " +
                "not in groupOrder, so it renders in an undefined position.");
        }

        var duplicates = entries.GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // bpmn-js keys its entries by id, so a duplicate silently replaces the
        // earlier element rather than rendering twice.
        Assert.True(
            duplicates.Count == 0,
            $"Duplicate palette entry ids: {string.Join(", ", duplicates)}");
    }

    // ── The specific drift #241 measured ────────────────────────────────────

    [Theory]
    [InlineData("create.end-event-cancel", "Cancel End")]
    [InlineData("create.transaction", "Transaction")]
    [InlineData("create.business-rule-task", "Business Rule Task")]
    [InlineData("create.manual-task", "Manual Task")]
    [InlineData("create.task", "Task (Generic)")]
    [InlineData("append.boundary-cancel", "Cancel Boundary")]
    [InlineData("create.participant", "Pool / Participant")]
    public void An_entry_whose_manifest_element_is_not_supported_is_not_offered(
        string entryId, string elementName)
    {
        var entry = Entries().Single(candidate => candidate.Id == entryId);
        var element = Manifest().Single(candidate => candidate.Name == elementName);

        Assert.Equal(Key(element.LocalName, element.EventDefinition),
            Key(entry.LocalName, entry.EventDefinition));

        // Each of these was on the palette when #241 was filed. Three are refused
        // by publish validation, so an author drew them, saved, and found out at
        // publish; the rest are withdrawn or unbuilt. The catalog still carries
        // the row -- label, icon, group -- so that promoting the element in the
        // manifest is the only edit needed.
        Assert.NotEqual("supported", element.Studio);
    }

    [Theory]
    [InlineData("create.adhoc-sub-process", "Ad-Hoc Sub-Process")]
    [InlineData("create.intermediate-throw-compensation", "Intermediate Throw (Compensation)")]
    [InlineData("create.user-task", "User Task")]
    [InlineData("create.complex-gateway", "Complex Gateway")]
    [InlineData("create.event-sub-process", "Event Sub-Process")]
    [InlineData("append.boundary-compensation", "Compensation Boundary")]
    public void A_supported_element_is_offered(string entryId, string elementName)
    {
        var entry = Entries().SingleOrDefault(candidate => candidate.Id == entryId);
        Assert.True(entry is not null, $"No palette entry '{entryId}'.");

        var element = Manifest().Single(candidate => candidate.Name == elementName);
        Assert.Equal("supported", element.Studio);
        Assert.Equal(Key(element.LocalName, element.EventDefinition),
            Key(entry!.LocalName, entry.EventDefinition));
    }

    // ── The provider is derived, and it is wired ────────────────────────────

    [Fact]
    public void The_provider_filters_on_the_manifest_rather_than_listing_entries()
    {
        var provider = File.ReadAllText(ProviderPath);

        Assert.Contains("@shared/bpmn-support.json", provider, StringComparison.Ordinal);
        Assert.Contains("@shared/bpmn-palette.json", provider, StringComparison.Ordinal);

        // The assertion that matters: membership comes from the manifest's studio
        // flag. Without this the provider could import the manifest and ignore it,
        // which is materially what the old arrangement did.
        Assert.Contains("studioStatusOf(entry) === \"supported\"", provider, StringComparison.Ordinal);
    }

    // ── #264: the menu filter's deny keys must match something ──────────────

    private static string BundlePath => Path.Combine(
        RepoRoot.Path, "src", "AutoNate.Spa", "public", "vendor", "bpmn-js",
        "bpmn-modeler.development.js");

    /// <summary>Every icon class the menu filter denies, from the same source it uses.</summary>
    private static IReadOnlyList<(string EntryId, string ClassName)> DenyKeys()
    {
        using var document = PaletteDocument();
        var manifest = Manifest().ToDictionary(
            element => Key(element.LocalName, element.EventDefinition),
            element => element.Studio, StringComparer.Ordinal);

        var keys = new List<(string, string)>();
        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            var key = Key(
                entry.GetProperty("localName").GetString()!,
                entry.GetProperty("eventDefinition").GetString());
            if (manifest.GetValueOrDefault(key) == "supported") continue;

            var id = entry.GetProperty("id").GetString()!;
            if (entry.TryGetProperty("className", out var c) && c.GetString() is { Length: > 0 } cls)
            {
                keys.Add((id, cls));
            }

            if (entry.TryGetProperty("menuClassNames", out var extra))
            {
                foreach (var name in extra.EnumerateArray())
                {
                    keys.Add((id, name.GetString()!));
                }
            }
        }

        return keys;
    }

    [Fact]
    public void Every_denied_icon_class_actually_occurs_in_the_vendored_bundle()
    {
        // This is the defect #264 was reopened for, made mechanical.
        //
        // `WITHHELD_ICON_CLASSES` is built from the catalog's own className
        // values, and three of them were names bpmn-js never emits —
        // `bpmn-icon-business-rule-task` occurs ZERO times in the bundle, where
        // bpmn-js uses `bpmn-icon-business-rule`. A deny key that matches nothing
        // removes nothing, silently, so Business Rule Task stayed one click from
        // an author on every menu and publish then refused it.
        //
        // A key is only a guard if it names something real.
        var bundle = File.ReadAllText(BundlePath);
        var keys = DenyKeys();

        Assert.NotEmpty(keys);

        // Per ELEMENT, not per key. The palette's own `className` is the class it
        // renders with and need not be one bpmn-js uses; what must hold is that
        // each withheld element has at least ONE key the bundle really emits,
        // because that is what removes it from the menus.
        var unmatched = keys
            .GroupBy(key => key.EntryId, StringComparer.Ordinal)
            .Where(group => !group.Any(key =>
                bundle.Contains($"\"{key.ClassName}\"", StringComparison.Ordinal)))
            .Select(group => $"{group.Key} -> tried {string.Join(", ", group.Select(k => k.ClassName))}")
            .ToList();

        Assert.True(
            unmatched.Count == 0,
            "Withheld elements whose every deny key is absent from the vendored " +
            $"bpmn-js bundle, so nothing removes them:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", unmatched));
    }

    [Fact]
    public void Every_withheld_element_has_a_deny_key_or_a_stated_reason()
    {
        // #264, second pass. The complement of the guard above, and the one whose
        // absence let Loop Marker through.
        //
        // `Every_denied_icon_class_actually_occurs_in_the_vendored_bundle` checks
        // that deny keys WHICH EXIST name something real. Nothing required a
        // withheld element to HAVE a deny key — so Loop Marker, with no catalog
        // row and no exclusion, was reachable from the replace menu's header and
        // invisible to every guard. Compensation Start, Lane and Message Flow had
        // the same hole.
        var catalogued = Entries()
            .Select(entry => Key(entry.LocalName, entry.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);
        var excused = Exclusions()
            .Select(exclusion => Key(exclusion.LocalName, exclusion.EventDefinition))
            .ToHashSet(StringComparer.Ordinal);

        var unguarded = Manifest()
            .Where(element => element.Studio != "supported")
            .Where(element => !catalogued.Contains(Key(element.LocalName, element.EventDefinition)))
            .Where(element => !excused.Contains(Key(element.LocalName, element.EventDefinition)))
            .Select(element => $"{element.Name} ({element.Studio})")
            .ToList();

        Assert.True(
            unguarded.Count == 0,
            "Elements the manifest withholds that have neither a catalog row (so " +
            "the menu filter has a deny key for them) nor a notOnThePalette reason " +
            $"saying why they need none:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", unguarded));
    }

    [Fact]
    public void The_filter_covers_the_header_row_as_well_as_the_entries()
    {
        // The two are separate reduces in bpmn-js (`_getEntries` vs
        // `_getHeaderEntries`, calling `getPopupMenuEntries` vs
        // `getPopupMenuHeaderEntries`). Implementing one and not the other left
        // the replace menu's `toggle-loop` button — Loop Marker, which publish
        // refuses — reachable in two clicks.
        var provider = File.ReadAllText(ProviderPath);

        Assert.Contains("getPopupMenuEntries", provider, StringComparison.Ordinal);
        Assert.Contains("getPopupMenuHeaderEntries", provider, StringComparison.Ordinal);

        var bundle = File.ReadAllText(BundlePath);

        // And the bundle really does have both hooks, so this is guarding a real
        // seam rather than a name I invented.
        Assert.Contains("getPopupMenuHeaderEntries", bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void The_filter_covers_every_element_menu_the_bundle_registers()
    {
        // #264 shipped covering two of three menus. `bpmn-append` — the context
        // pad's "Append element", fed by the same option table as the Create
        // popup — was missed, so the withheld elements stayed reachable there.
        //
        // Reading the menu ids out of the bundle means a fourth one appearing in
        // a future bpmn-js fails here instead of quietly opening a fourth door.
        var bundle = File.ReadAllText(BundlePath);
        var provider = File.ReadAllText(ProviderPath);

        var registered = System.Text.RegularExpressions.Regex
            .Matches(bundle, @"registerProvider\(""(?<id>[a-z-]+)""")
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            // Not an element menu — it aligns selected shapes.
            .Where(id => id != "align-elements")
            .ToList();

        Assert.NotEmpty(registered);

        var uncovered = registered
            .Where(id => !provider.Contains($"\"{id}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            "Popup menus the bundle registers that the manifest filter does not " +
            $"cover, so withdrawn elements remain reachable there: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void The_modeler_registers_the_menu_filter()
    {
        var modeler = File.ReadAllText(ModelerPath);
        Assert.Contains("createManifestMenuFilter", modeler, StringComparison.Ordinal);
    }

    [Fact]
    public void The_modeler_registers_the_manifest_palette_provider()
    {
        var modeler = File.ReadAllText(ModelerPath);

        // The old array's whole failure was being unreferenced, so a test that
        // only checks the catalog would have passed throughout. bpmn-js installs
        // its own paletteProvider unless additionalModules overrides it.
        Assert.Contains("createManifestPaletteProvider", modeler, StringComparison.Ordinal);
        Assert.Contains("additionalModules", modeler, StringComparison.Ordinal);

        Assert.DoesNotContain("BPMN_MENU_ENTRIES", modeler, StringComparison.Ordinal);
    }
}
