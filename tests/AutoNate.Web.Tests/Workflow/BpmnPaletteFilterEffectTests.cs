using System.Diagnostics;
using System.Text.Json;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// The withheld-element popup filter is asserted by its EFFECT (#392).
/// </summary>
/// <remarks>
/// <para>
/// Every earlier palette guard read <c>bpmn-palette.json</c> or parsed
/// <c>palette.js</c> as text. Each pinned a better proxy for "a withdrawn element
/// is not one click away" and none pinned the thing itself, so #392 could happen:
/// deleting the entire filter at <c>palette.js:296</c> left <b>32/32 green</b>
/// while Pool, Transaction, Cancel End, Business Rule Task and
/// <c>toggle-loop</c> all returned to Create, Append and Replace.
/// </para>
/// <para>
/// So this runs the module. It imports <c>palette.js</c> in node, calls
/// <c>createManifestMenuFilter</c>, and pushes the entry shapes bpmn-js actually
/// produces through the filter's own <c>strip</c>. It cannot be satisfied by a
/// comment (#393), by a deny set nobody consults, or by a read that is never
/// used — only by the entries being removed.
/// </para>
/// <para>
/// <b>Why node and not a JS test tier.</b> The SPA has none — no vitest, no
/// <c>test</c> script, zero <c>*.test.*</c> files — and adding one is a bigger
/// decision than this bug. Running the real module from the existing backend
/// suite gets the property asserted today, and keeps it inside the CI test-count
/// reconciliation, which a new tier would not be.
/// </para>
/// <para>
/// <b>This fails rather than skips when node is missing.</b> A guard that opts
/// out when its tool is absent is the failure this milestone has found more than
/// any other. <c>ci.yml</c>'s backend job declares node for exactly this reason.
/// </para>
/// </remarks>
public sealed class BpmnPaletteFilterEffectTests
{
    private sealed record ProbeResult(
        string[] Menus,
        int WithheldRows,
        int ShapesTested,
        string[] Unreachable,
        string[] Survivors,
        string[] HeaderSurvivors);

    private static ProbeResult RunProbe()
    {
        var repo = RepoRoot.Path;
        var shared = Path.Combine(repo, "src", "shared");
        var paletteSource = File.ReadAllText(
            Path.Combine(repo, "src", "AutoNate.Spa", "src", "lib", "bpmn", "palette.js"));

        // node has no Vite, so the "@shared/*" alias is rewritten to file URLs.
        // Nothing else about the module is touched -- the code under test is the
        // shipped code, byte for byte apart from these two import specifiers.
        var rewritten = paletteSource
            .Replace(
                "from \"@shared/bpmn-palette.json\"",
                $"from \"file://{Path.Combine(shared, "bpmn-palette.json")}\" with {{ type: \"json\" }}",
                StringComparison.Ordinal)
            .Replace(
                "from \"@shared/bpmn-support.json\"",
                $"from \"file://{Path.Combine(shared, "bpmn-support.json")}\" with {{ type: \"json\" }}",
                StringComparison.Ordinal);

        Assert.DoesNotContain("@shared/", rewritten, StringComparison.Ordinal);

        var work = Directory.CreateTempSubdirectory("autonate-palette-probe-");
        try
        {
            var modulePath = Path.Combine(work.FullName, "palette.mjs");
            File.WriteAllText(modulePath, rewritten);

            var probe = Path.Combine(repo, "tests", "AutoNate.Web.Tests", "Workflow", "js", "palette-filter-probe.mjs");
            Assert.True(File.Exists(probe), $"The probe script is missing: {probe}");

            var psi = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work.FullName,
            };
            psi.ArgumentList.Add(probe);
            psi.ArgumentList.Add(new Uri(modulePath).AbsoluteUri);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("node did not start");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 120_000);

            Assert.True(
                process.ExitCode == 0,
                $"The palette filter probe failed (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}\n\n"
                + "If node is missing, install it -- this guard runs the real module and "
                + "deliberately does not skip, because a guard that opts out when its tool "
                + "is absent is not a guard (#392).");

            return JsonSerializer.Deserialize<ProbeResult>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void No_withheld_element_survives_the_popup_filter_on_any_surface()
    {
        var result = RunProbe();

        Assert.True(
            result.WithheldRows > 10 && result.ShapesTested > 20,
            $"The probe exercised {result.WithheldRows} withheld rows over {result.ShapesTested} "
            + "entry shapes. If either collapsed toward zero this guard is asserting "
            + "nothing -- which is how three earlier palette guards passed while the "
            + "hole was open.");

        Assert.True(
            result.Unreachable.Length == 0,
            "These withheld entries carry no key the filter could judge them by, so they "
            + "are invisible to it by construction:\n  "
            + string.Join("\n  ", result.Unreachable));

        Assert.True(
            result.Survivors.Length == 0,
            "These withheld elements SURVIVE the popup filter and are one click away:\n  "
            + string.Join("\n  ", result.Survivors)
            + "\n\nPublish refuses every one of them, so the author draws it, saves, and is "
            + "told no (#264, #282, #381, #392).");

        Assert.True(
            result.HeaderSurvivors.Length == 0,
            "These withheld elements survive on the HEADER row, which walks the providers "
            + "separately from the entry row -- #264's second pass:\n  "
            + string.Join("\n  ", result.HeaderSurvivors));
    }

    [Fact]
    public void The_filter_is_registered_on_every_menu_it_declares()
    {
        var result = RunProbe();

        // Read from the module's own FILTERED_POPUP_MENUS via the probe, so a menu
        // added there without a registration fails here.
        Assert.Equal(
            "bpmn-append,bpmn-create,bpmn-replace",
            string.Join(",", result.Menus.Order(StringComparer.Ordinal)));
    }
}
