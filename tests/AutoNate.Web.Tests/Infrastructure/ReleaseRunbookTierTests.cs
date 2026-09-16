using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The release runbook requires the full tier before tagging (#475).
/// </summary>
/// <remarks>
/// <para>
/// A <c>v*</c> tag publishes four images to GHCR, and the release workflow runs
/// no tests — it builds and publishes whatever it is pointed at. The green tick
/// a releaser checks is the <b>slim</b> tier, which stands up no workflow
/// engine, so nothing about it says a BPMN element executes.
/// </para>
/// <para>
/// The owner chose a documented step over an automated block. That choice only
/// holds while the document actually says it, which is what this asserts: the
/// step cannot be quietly dropped while the runbook still reads like a process.
/// </para>
/// <para>
/// What this does NOT cover: whether anyone ran it. Deliberate — see above.
/// </para>
/// </remarks>
public sealed class ReleaseRunbookTierTests
{
    private static string Runbook => File.ReadAllText(Path.Combine(
        RepoRoot.Path, ".claude", "skills", "cut-a-release", "SKILL.md"));

    /// <summary>
    /// The command is named, in a heading that says when to run it.
    /// </summary>
    /// <remarks>
    /// A bare substring match is not enough. <c>make test-full-local</c> could
    /// appear in a "see also" line, or in the "what this does not do" section,
    /// and satisfy a naive contains-check while telling a releaser nothing. The
    /// heading is what a person scanning the runbook actually reads.
    /// </remarks>
    [Fact]
    public void The_runbook_requires_the_full_tier_before_tagging()
    {
        var text = Runbook;

        Assert.Contains("make test-full-local", text, StringComparison.Ordinal);

        var headings = text.Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        Assert.True(headings.Count > 0, "The runbook has no headings; it is not the file this expects.");

        var step = headings.FirstOrDefault(h =>
            h.Contains("full tier", StringComparison.OrdinalIgnoreCase)
            && h.Contains("before tagging", StringComparison.OrdinalIgnoreCase));

        Assert.True(step is not null,
            "No heading in the release runbook names running the full tier BEFORE tagging. "
            + "A mention of `make test-full-local` somewhere in the file is not a required step — "
            + "it has to be where a releaser scanning headings will see it (#475). Headings found:\n  "
            + string.Join("\n  ", headings));

        // And it must come BEFORE the tagging step, or the instruction is
        // advice about something the releaser has already done.
        var fullTierAt = text.IndexOf(step!, StringComparison.Ordinal);
        var tagAt = headings
            .Where(h => h.Contains("Tag and push", StringComparison.OrdinalIgnoreCase))
            .Select(h => text.IndexOf(h, StringComparison.Ordinal))
            .DefaultIfEmpty(-1)
            .First();

        Assert.True(tagAt > 0, "The runbook has no 'Tag and push' step to order against.");
        Assert.True(fullTierAt < tagAt,
            "The full-tier step appears AFTER 'Tag and push' in the runbook. A releaser reads "
            + "top to bottom; a pre-tag requirement printed after the tag is not one (#475).");
    }

    /// <summary>
    /// The step still says it is required, and still shows a pass (#490).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The heading and ordering checks close the failure the AC named — a
    /// passing mention somewhere in the file. They do not notice the
    /// instruction being <em>inverted</em>. Measured: retitling the step
    /// "Why you do NOT need the full tier before tagging" and changing
    /// <c>**Required.**</c> to <c>**Not required.**</c> left every assertion
    /// green, because the heading still contained both phrases. So did deleting
    /// the entire "what a pass looks like" block, which is itself an AC item.
    /// </para>
    /// <para>
    /// A guard that survives the opposite instruction is worse than none: it
    /// reads as protection.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_step_is_stated_as_required_and_shows_what_a_pass_looks_like()
    {
        var text = Runbook;
        var lines = text.Split('\n');

        var start = Array.FindIndex(lines, l =>
            l.StartsWith("## ", StringComparison.Ordinal)
            && l.Contains("full tier", StringComparison.OrdinalIgnoreCase)
            && l.Contains("before tagging", StringComparison.OrdinalIgnoreCase));

        Assert.True(start >= 0, "No pre-tag full-tier heading; see the sibling test.");

        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var step = string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);

        Assert.Contains("Required", step, StringComparison.OrdinalIgnoreCase);

        foreach (var reversal in new[] { "not required", "optional", "skip this step" })
        {
            Assert.False(
                step.Contains(reversal, StringComparison.OrdinalIgnoreCase),
                $"The pre-tag step says \"{reversal}\". The heading can keep its wording while "
                + "the instruction is inverted, which is how this guard was got past (#490).");
        }

        // The AC says "with what a pass looks like". Nothing asserted it, so
        // deleting the whole sample block kept the guard green.
        Assert.Contains("result  : PASS", step, StringComparison.Ordinal);
        Assert.Contains("Tier integrity ok", step, StringComparison.Ordinal);
    }

    /// <summary>
    /// It says what slim does not cover, as a consequence rather than a percentage.
    /// </summary>
    /// <remarks>
    /// A number goes stale and then teaches the wrong thing with authority —
    /// which is how the percentages in <c>.n8/config.yml</c> got where they are.
    /// The consequence does not go stale: slim runs no engine, so slim proves no
    /// element executes, whatever the count happens to be that month.
    /// </remarks>
    [Fact]
    public void The_runbook_says_what_the_slim_tick_does_not_cover()
    {
        var text = Runbook;

        Assert.Contains("no BPMN element is proven to execute", text, StringComparison.OrdinalIgnoreCase);

        // The consequence, not a coverage percentage. A `%` in the runbook is not
        // itself wrong -- but one attached to a coverage claim is the stale-number
        // failure this AC exists to prevent.
        var stalePercentages = text.Split('\n')
            .Where(line => line.Contains('%', StringComparison.Ordinal))
            .Where(line => line.Contains("cover", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("tier", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("element", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(stalePercentages.Count == 0,
            "The runbook states tier coverage as a percentage. Percentages go stale and then "
            + "mislead with authority; state the consequence instead (#475):\n  "
            + string.Join("\n  ", stalePercentages));
    }
}
