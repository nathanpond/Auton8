using System.Text.RegularExpressions;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Which execution read routes may still read live Flowable (#104).
/// </summary>
/// <remarks>
/// <para>
/// The list below is the point. Each entry is a route that still injects
/// <c>IFlowableClient</c> for a read, with the reason it cannot come from the
/// cache — so the set is reviewable in one place, and adding a tenth live-reading
/// route is a decision someone has to write down rather than something that
/// happens.
/// </para>
/// <para>
/// It asserts in both directions: a route that leaves the list must stop
/// injecting the client, and a route that starts injecting it must be added here.
/// A one-directional guard would let the live path creep back, which is the thing
/// this story is actually protecting.
/// </para>
/// </remarks>
public sealed class ExecutionReadSourceGuardTests
{
    private const string EndpointsPath = "src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs";

    /// <summary>
    /// GET routes permitted to read live Flowable, and why.
    /// </summary>
    private static readonly Dictionary<string, string> LiveReadsAllowed = new(StringComparer.Ordinal)
    {
        ["/{processInstanceId}/diagram"] =
            "Needs BPMN XML and variables. workflow_execution_cache is 17 scalar columns and holds neither; "
            + "serving it means sibling read-throughs over the variable cache, which is its own story.",
        ["/{processInstanceId}/history"] =
            "Needs workflow_event_log_cache, not the execution cache. Sibling read-through, own story.",
        ["/{processInstanceId}/log"] =
            "Same as /history.",
        ["/{processInstanceId}/children"] =
            "STRUCTURAL: workflow_execution_cache has no parent-instance column, so the parent/child "
            + "relation cannot be expressed in it at all. Adding one is a projection and schema change.",
        ["/{processInstanceId}/activities/{activityId}/completed-assignees"] =
            "History-derived; same sibling read-through as /history.",
        ["/{processInstanceId}/adhoc"] =
            "STRUCTURAL: enabled ad-hoc activities are live engine state, not a projection of anything. "
            + "There is nothing to cache.",
    };

    [Fact]
    public void Only_the_named_execution_reads_still_inject_the_flowable_client()
    {
        var source = File.ReadAllText(RepoPath(EndpointsPath));

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        var allRoutes = new SortedSet<string>(StringComparer.Ordinal);

        // Each MapGet's parameter list runs from the route literal to the opening
        // brace of the handler body.
        foreach (Match match in Regex.Matches(
                     source, @"executions\.MapGet\(""(?<route>[^""]+)"".*?\n(?<params>.*?)\n\s*\{", RegexOptions.Singleline))
        {
            var route = match.Groups["route"].Value;
            allRoutes.Add(route);
            if (match.Groups["params"].Value.Contains("IFlowableClient", StringComparison.Ordinal))
            {
                actual.Add(route);
            }
        }

        // Vacuity guard: if the regex stops matching, every assertion below is
        // trivially satisfied by an empty set.
        Assert.True(
            allRoutes.Count >= 9,
            $"Expected to find the execution GET routes; found {allRoutes.Count}. "
            + "If MapGet's shape changed, this guard is looking at nothing and is no longer checking anything.");

        var expected = new SortedSet<string>(LiveReadsAllowed.Keys, StringComparer.Ordinal);

        var crept = actual.Except(expected).ToList();
        Assert.True(crept.Count == 0,
            "These execution read routes inject IFlowableClient but are not on the allowed list. "
            + "Either serve them from the cache, or add them with the reason they cannot be: "
            + string.Join(", ", crept));

        var stale = expected.Except(actual).ToList();
        Assert.True(stale.Count == 0,
            "These routes are listed as live-reading but no longer inject IFlowableClient. "
            + "Remove them from the list so it keeps meaning what it says: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// `/tasks` is served from the cache, asserted at the source (#104).
    /// </summary>
    /// <remarks>
    /// The list above would still pass if `/tasks` read live and were quietly
    /// added to it. This pins the direction of travel for the one route this
    /// story moved.
    /// </remarks>
    [Fact]
    public void The_tasks_route_is_not_on_the_live_read_list()
    {
        Assert.DoesNotContain("/{processInstanceId}/tasks", LiveReadsAllowed.Keys);
    }

    // RepoRoot, not a walk up to a `.git` DIRECTORY. In a git worktree `.git` is
    // a FILE, so that form throws instead of running -- and `/n8-verify` works in
    // worktrees. RepoRootAnchorTests forbids it repo-wide, and caught the first
    // version of this helper.
    private static string RepoPath(string relative)
    {
        var path = Path.Combine(RepoRoot.Path, relative);
        Assert.True(File.Exists(path), $"Expected to find {relative} at {path}.");
        return path;
    }
}
