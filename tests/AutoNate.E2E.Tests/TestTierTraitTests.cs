using System.Reflection;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Every `RequiresService` trait names a service the tiers know about (#472).
/// </summary>
/// <remarks>
/// <para>
/// The tier boundary is the trait, and the slim filter excludes the services
/// named in <c>tests/tiers.env</c>. A trait carrying anything else — a typo, a
/// service nobody added to the list — is excluded from nothing, so its test runs
/// in slim on GitHub, where that service does not exist.
/// </para>
/// <para>
/// It reads <b>compiled attributes</b>, not source. There are around eight
/// mentions of <c>RequiresService</c> in comments and XML docs across this
/// suite; a grep-based guard would report them as traits and be either noisy or,
/// once someone silenced the noise, blind.
/// </para>
/// <para>
/// No trait: this runs in slim, which is the point — a mis-traited test is a
/// GitHub problem and GitHub should be the one to say so.
/// </para>
/// </remarks>
public sealed class TestTierTraitTests
{
    [Fact]
    public void Every_requires_service_trait_names_a_known_service()
    {
        var known = File.ReadAllLines(Path.Combine(Support.RepoRoot.Path, "tests", "tiers.env"))
            .First(line => line.StartsWith("AUTONATE_TIER_SERVICES=", StringComparison.Ordinal))
            .Split('=', 2)[1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(known.Count > 0, "tests/tiers.env named no services, so this guard would "
            + "accept every trait value — a clean bill of health against nothing.");

        // TraitAttribute exposes its name and value as CONSTRUCTOR ARGUMENTS
        // only -- there are no properties to read -- so this goes through
        // CustomAttributeData rather than GetCustomAttributes<T>().
        var traits = typeof(TestTierTraitTests).Assembly.GetTypes()
            .SelectMany(type => CustomAttributeData.GetCustomAttributes(type)
                .Concat(type.GetMethods().SelectMany(CustomAttributeData.GetCustomAttributes)))
            .Where(data => data.AttributeType == typeof(TraitAttribute))
            .Where(data => data.ConstructorArguments.Count == 2)
            .Where(data => (string?)data.ConstructorArguments[0].Value == "RequiresService")
            .Select(data => (string?)data.ConstructorArguments[1].Value ?? "")
            .ToList();

        Assert.True(traits.Count > 0, "No RequiresService trait was found by reflection. Either "
            + "the suite has stopped using them, or this guard has stopped seeing them — and the "
            + "second reads exactly like the first (#472).");

        var unknown = traits
            .Where(value => !known.Contains(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "These RequiresService values are not in tests/tiers.env's service list:\n  "
            + string.Join("\n  ", unknown)
            + "\n\nA value the slim filter does not exclude runs in slim, on GitHub, where that "
            + "service is not running. Add it to AUTONATE_TIER_SERVICES — the filter derives from "
            + "that line and a guard checks the derivation (#472).");
    }
}
