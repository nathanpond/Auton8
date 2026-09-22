using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Every E2E class that touches the engine runs in the sequential collection
/// (#643, #652). xUnit runs collections in parallel by default, and the engine
/// is shared, so a class outside the collection that starts executions would
/// race every other one -- and a bulk delete in one would wipe another's
/// mid-flight run and read as THAT spec's flake.
/// </summary>
public sealed class E2EEngineCollectionGuardTests
{
    [Fact]
    public void Every_engine_touching_E2E_class_runs_in_the_sequential_collection()
    {
        var e2eDir = Path.Combine(RepoRoot.Path, "tests", "AutoNate.E2E.Tests");
        var offenders = new List<string>();
        var checkedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(e2eDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("[Trait(\"RequiresService\", \"Flowable\")]", StringComparison.Ordinal)
                && !source.Contains("[Trait(\"RequiresService\", \"Dapr\")]", StringComparison.Ordinal))
            {
                continue;
            }
            checkedFiles++;
            if (!source.Contains("[Collection(AutoNateE2ECollection.Name)]", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        // Vacuity: the scan has to have found the engine classes at all.
        Assert.True(checkedFiles >= 20, $"only {checkedFiles} engine-traited E2E files found; the trait scan is looking at nothing");
        Assert.True(offenders.Count == 0,
            "These E2E classes carry an engine trait and run OUTSIDE AutoNateE2ECollection: "
            + string.Join(", ", offenders)
            + ". Add [Collection(AutoNateE2ECollection.Name)] so they run sequentially against the shared engine (#643).");
    }
}
