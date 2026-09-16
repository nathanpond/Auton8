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
        @"outside (?:of )?CI\b"
        + @"|outside the gate\b"
        + @"|CI(?:'s)? exclu(?:des|sion)"
        + @"|exclude[sd] from CI\b"
        + @"|CI (?:skips|never reaches|cannot run|does not run|doesn't run|won't run)"
        + @"|not run (?:in|by) CI\b",
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
        string[] roots = ["src", "tests", "docs", ".claude", ".github", "infra"];
        string[] extensions = [".cs", ".md", ".ts", ".tsx", ".js", ".yml", ".yaml", ".py", ".sh"];

        foreach (var root in roots)
        {
            var path = Path.Combine(RepoRoot.Path, root);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

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

    [Fact]
    public void No_file_still_teaches_the_single_tier_model()
    {
        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in Scannable())
        {
            scanned++;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SingleTierProse.IsMatch(lines[i])) continue;

                offenders.Add($"{Path.GetRelativePath(RepoRoot.Path, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        // A scan that looked at nothing reports a clean tree forever. Same
        // reasoning as SourceIdentifierScanner's own count assertion.
        Assert.True(scanned > 500,
            $"This guard scanned only {scanned} files. It is reporting a clean bill of health "
            + "against almost nothing — check the roots and extensions before believing it.");

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
}
