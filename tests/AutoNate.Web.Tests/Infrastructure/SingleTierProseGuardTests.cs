using System.Text.RegularExpressions;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// No file in the tree still teaches the single-tier model (#477).
/// </summary>
/// <remarks>
/// <para>
/// "CI" used to mean one thing while the repo had three, and nineteen places
/// across seventeen files encoded the assumption — production code, two skills,
/// a backlog, and comments sitting next to the tests they describe. A reader who
/// hits any of them learns the old model from a source that looks authoritative.
/// </para>
/// <para>
/// <b>The phrase set is deliberately wide.</b> The three obvious spellings —
/// "outside CI", "CI excludes", "outside the gate" — matched only <b>8 of the
/// 19</b>. The rest said "excluded from CI", "inherits CI's exclusion", "CI
/// skips them by filter", "CI never reaches this", "CI's exclusion stops
/// holding", "CI skips it like the Flowable and Dapr specs". A guard built to
/// three literals would have left eleven of its own sweep free to return.
/// </para>
/// <para>
/// What this does NOT cover: prose that describes the old model without using
/// any of these phrases. There is no mechanical check for that.
/// </para>
/// </remarks>
public sealed class SingleTierProseGuardTests
{
    /// <summary>
    /// "CI" as a synonym for "the whole test suite", however it is spelled.
    /// </summary>
    /// <remarks>
    /// Each alternative is a phrasing that actually shipped, not a hypothetical.
    /// The verbs are grouped rather than listed, because the eleven misses were
    /// all the same claim in a different grammatical mood.
    /// </remarks>
    private static readonly Regex SingleTierProse = new(
        // Built from what "CI" is DOING, not from a list of sentences. The first
        // version was six alternatives drawn from the locations it had already
        // found, and 15 more survived it -- one of them escaping on a two-word
        // insertion (`exclude IT from CI`). The story's own thesis, one level up.
        @"outside (?:of )?CI\b"
        + @"|outside the gate\b"
        + @"|\bin-CI\b"
        + @"|CI(?:'s)? exclu(?:des|sion)"
        + @"|exclude[sd]?\s+(?:\w+\s+){0,2}from CI\b"
        + @"|CI (?:skips|never reaches|cannot run|can(?:no|')t run|does not run|doesn't run|won't run"
        + @"|never runs|hosts|is exactly where|can exclude|can see)"
        + @"|(?:not |never )run (?:in|by) CI\b"
        + @"|where CI can see it"
        + @"|(?:runs? )?everywhere CI runs"
        + @"|(?:These |Those )?tests run in CI\b"
        + @"|CI E2E job"
        + @"|CI test-count"
        + @"|(?:does not|doesn't) exist in CI\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Append-only records of what was true when they were written. Rewriting
    // them would falsify the history the rest of this repo cites.
    private static readonly string[] Exempt =
    [
        Path.Combine(".n8", "decisions.md"),
        Path.Combine("docs", "history", "pull-requests.md"),
    ];

    private static IEnumerable<string> Scannable()
    {
        foreach (var file in ScannableFiles()) yield return file;
    }

    // Every root that can carry prose about the tiers. `.n8` and the repo root
    // were both missing (#486) -- and the repo root holds CLAUDE.md, which is
    // the canonical Test-tiers document and therefore the likeliest place for
    // someone to write this prose next.
    internal static readonly string[] Roots =
        ["src", "tests", "docs", ".claude", ".github", "infra", ".n8", "plugins", "services", "tools", "scripts"];

    private static readonly string[] Extensions =
        [".cs", ".md", ".ts", ".tsx", ".js", ".yml", ".yaml", ".py", ".sh"];

    private static IEnumerable<string> ScannableFiles()
    {
        // The repo root itself, NON-recursively -- CLAUDE.md, README.md,
        // CONTRIBUTING.md, Makefile. `Makefile` has no extension, so the
        // extension filter would drop it.
        foreach (var file in Directory.EnumerateFiles(RepoRoot.Path, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                || string.Equals(name, "Makefile", StringComparison.Ordinal))
            {
                yield return file;
            }
        }

        foreach (var root in Roots)
        {
            var path = Path.Combine(RepoRoot.Path, root);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

                var relative = Path.GetRelativePath(RepoRoot.Path, file);

                if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                if (relative.Contains("node_modules", StringComparison.Ordinal)) continue;
                if (Exempt.Contains(relative, StringComparer.Ordinal)) continue;

                // This file names the phrases in order to forbid them.
                if (relative.EndsWith(nameof(SingleTierProseGuardTests) + ".cs", StringComparison.Ordinal)) continue;

                yield return file;
            }
        }
    }

    /// <summary>
    /// The offender scan, over any tree — so the walk itself can be tested (#486).
    /// </summary>
    /// <remarks>
    /// A seam, not decoration. Nothing proved this path could report a hit: the
    /// positive cases exercised the <em>regex</em>, and a regex mutant says
    /// nothing about the file walk that feeds it. That is the same shape as
    /// #485, where a gate read an attribute nobody sets and every test agreed
    /// with it.
    /// </remarks>
    internal static List<string> OffendersIn(IEnumerable<(string Name, string[] Lines)> files)
    {
        var offenders = new List<string>();

        foreach (var (name, lines) in files)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SingleTierProse.IsMatch(lines[i])) continue;

                offenders.Add($"{name}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return offenders;
    }

    [Fact]
    public void No_file_still_teaches_the_single_tier_model()
    {
        var scanned = 0;
        var perRoot = new Dictionary<string, int>(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var file in Scannable())
        {
            scanned++;

            var relative = Path.GetRelativePath(RepoRoot.Path, file);
            var root = relative.Contains(Path.DirectorySeparatorChar)
                ? relative[..relative.IndexOf(Path.DirectorySeparatorChar)]
                : "(repo root)";
            perRoot[root] = perRoot.GetValueOrDefault(root) + 1;

            offenders.AddRange(OffendersIn([(relative, File.ReadAllLines(file))]));
        }

        // A scan that looked at nothing reports a clean tree forever. Same
        // reasoning as SourceIdentifierScanner's own count assertion.
        Assert.True(scanned > 500,
            $"This guard scanned only {scanned} files. It is reporting a clean bill of health "
            + "against almost nothing — check the roots and extensions before believing it.");

        // PER ROOT, not one total (#486). `src` alone is over a thousand files,
        // so a single threshold survives deleting every root that holds the
        // prose this guard exists for — 18 of the original 19 locations were
        // under tests/, docs/ and .claude/. Same reasoning as the per-service
        // tier pins in tests/tiers.env.
        var empty = new[] { "(repo root)", "tests", "docs", ".claude", ".github", "src" }
            .Where(root => perRoot.GetValueOrDefault(root) == 0)
            .ToList();

        Assert.True(empty.Count == 0,
            "These roots contributed no files to the scan, so anything they contain is "
            + "unguarded — most likely a root was dropped from the list or an extension "
            + "stopped matching:\n  " + string.Join("\n  ", empty)
            + "\n\nScanned per root: "
            + string.Join(", ", perRoot.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}")));

        Assert.True(offenders.Count == 0,
            "These say \"CI\" where they mean a tier. \"CI\" names three different things here — "
            + "slim (what GitHub runs), full-local, and full-keycloak — so prose has to say which "
            + "one. See CLAUDE.md > Test tiers (#477).\n  "
            + string.Join("\n  ", offenders.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The guard sees every phrasing the sweep actually found.
    /// </summary>
    /// <remarks>
    /// Without this, a regex that matches nothing — a lost escape, a narrowed
    /// alternative — reports a clean tree forever. Every string below is quoted
    /// from a file this sweep rewrote, and the last three are the ones that beat
    /// the three-literal version.
    /// </remarks>
    [Theory]
    [InlineData("puts it outside CI, alongside ~49% of this suite")]
    [InlineData("so it is outside the gate")]
    [InlineData("CI excludes `RequiresService=Flowable`")]
    [InlineData("and therefore excluded from CI, so its own")]
    [InlineData("inherits CI's exclusion rather than relying on someone")]
    [InlineData("so CI skips them by filter and never reaches this")]
    [InlineData("CI never reaches this — the specs carry")]
    [InlineData("or CI's exclusion stops holding")]
    [InlineData("so CI skips it like the Flowable and Dapr specs")]
    [InlineData("guarded only by a test CI cannot run")]
    public void The_guard_can_see_the_phrasings_it_forbids(string shipped)
    {
        Assert.Matches(SingleTierProse, shipped);
    }

    /// <summary>
    /// It does not fire on the vocabulary it protects.
    /// </summary>
    /// <remarks>
    /// A guard that cannot coexist with the documentation explaining the rule is
    /// one somebody deletes the documentation to satisfy.
    /// </remarks>
    [Theory]
    [InlineData("GitHub runs the slim tier; full-local runs everything but Keycloak.")]
    [InlineData("This is full-local only, so no merge gate runs it.")]
    [InlineData("`RequiresService=Flowable` puts this spec in the full-local tier.")]
    [InlineData("Neither slim nor full-local selects these specs.")]
    [InlineData("CI runs the slim tier on every push.")]
    public void The_guard_does_not_fire_on_tier_vocabulary(string allowed)
    {
        Assert.DoesNotMatch(SingleTierProse, allowed);
    }

    /// <summary>
    /// The scan reports a hit when there is one — the half nothing proved (#486).
    /// </summary>
    /// <remarks>
    /// The positive <c>InlineData</c> cases below exercise the regex. This
    /// exercises the walk: a synthetic two-file tree where one line offends, so
    /// a scan that silently returned nothing could not pass.
    /// </remarks>
    [Fact]
    public void The_scan_reports_an_offender_and_names_where_it_is()
    {
        var offenders = OffendersIn(
        [
            ("clean.cs", ["// GitHub runs the slim tier.", "// full-local runs the engine."]),
            ("bad.cs", ["// fine", "// this spec is outside CI", "// also fine"]),
        ]);

        var only = Assert.Single(offenders);
        Assert.Equal("bad.cs:2: // this spec is outside CI", only);
    }

    /// <summary>
    /// Every phrasing that escaped the first sweep is now seen (#486).
    /// </summary>
    /// <remarks>
    /// Quoted from the 15 locations the first guard missed. The first entry is
    /// the one that matters most: it escaped <c>exclude[sd] from CI</c> on a
    /// two-word insertion, which is how every one of these escapes — a small
    /// rephrasing, never a new idea.
    /// </remarks>
    [Theory]
    [InlineData("traiting the conversion test would exclude it from CI for no reason")]
    [InlineData("CI hosts neither Flowable nor Dapr")]
    [InlineData("This is the in-CI half: it cannot run an engine")]
    [InlineData("asserted where CI can see it (#447)")]
    [InlineData("which the CI E2E job does not host")]
    [InlineData("Traited so CI can exclude it by capability")]
    [InlineData("These tests run in CI: no engine, no browser.")]
    [InlineData("so it runs everywhere CI runs")]
    [InlineData("keeps it inside the CI test-count reconciliation")]
    [InlineData("but it does not exist in CI")]
    [InlineData("CI is exactly where you want to catch it")]
    public void The_guard_sees_the_phrasings_that_escaped_the_first_sweep(string shipped)
    {
        Assert.Matches(SingleTierProse, shipped);
    }
}
