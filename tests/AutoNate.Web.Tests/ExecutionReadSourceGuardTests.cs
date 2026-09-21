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
        ["/{processInstanceId}/activities/{activityId}/instances"] =
            "STRUCTURAL, twice over (#173). The per-instance rows are history-derived, so the same "
            + "sibling read-through as /history; and each instance's element value is a variable "
            + "Flowable scopes to that instance's OWN execution, which nothing projects -- "
            + "workflow_execution_cache is per PROCESS instance and has no row to hang it on. The four "
            + "owner decisions after #222 took no schema change here deliberately, and this route only "
            + "runs when an operator expands a row, which is the point of it being a separate route.",
        // #635. NEWLY VISIBLE. The scan used to read only `executions.MapGet`
        // parameter lists for the literal `IFlowableClient`, so these were not
        // absent from the list -- they were absent from the QUESTION.
        ["/{processInstanceId}/tasks"] =
            "#604: a DETAIL view, read through on every call. The cache learns of a task the "
            + "engine created on its own -- a timer firing, a boundary event, an ad-hoc activity, "
            + "the successor to a task just completed -- only on the next poll, which is a minute, "
            + "while the things watching for it give up in thirty seconds. Reached through "
            + "WorkflowTaskCacheRefresher rather than the client directly, which is exactly how it "
            + "escaped this guard for a whole story.",
        ["tasks:/assigned-to-me"] =
            "NOT REVIEWED against the cache, and saying so rather than inventing a 'cannot'. "
            + "workflow_task_cache holds these rows, so this is a candidate for cache-serving that "
            + "#104 never scoped. Listed to make it visible; see #637.",
        ["tasks:/assigned-to-team"] =
            "Same as /assigned-to-me: a candidate #104 never scoped, listed rather than hidden.",
        ["tasks:/{taskId}/form-config"] =
            "Reads the task's form key from the engine to resolve a form. The cache carries "
            + "form_key, so this is also a candidate; not reviewed, listed.",
        ["/{processInstanceId}/jobs"] =
            "STRUCTURAL: jobs are live engine state -- what is scheduled, retrying or dead-lettered "
            + "right now -- and nothing projects them. Reached through IFlowableJobClient, which is "
            + "not a substring of IFlowableClient and so was outside the old scan.",
        ["/{processInstanceId}/jobs/{jobId}/exception"] =
            "Same as /jobs: live engine state, no projection.",
        ["/jobs"] =
            "Same as /jobs, cross-execution.",
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

        // #635. EVERY MapGet in the file, and every type that reaches the engine.
        //
        // The old scan asked `executions.MapGet` only, for the literal
        // `IFlowableClient` in the parameter list. Three holes, and one had
        // already been walked through:
        //
        //   * a live read reached through an INJECTED HELPER was invisible, so
        //     #604 made /tasks read live on every request while this guard went
        //     on calling it cache-served;
        //   * `tasks.MapGet` was not scanned at all, and all three of its routes
        //     inject the client;
        //   * `IFlowableJobClient` is not a substring of `IFlowableClient`, so
        //     the three jobs routes were outside it too.
        //
        // A guard whose scope is narrower than its promise is worse than none:
        // its allow-list reads as the complete set of live reads and is not.
        string[] engineReaching =
        [
            "IFlowableClient",
            "IFlowableJobClient",
            "IFlowableDecisionClient",
            // Injected helpers that call the engine on the handler's behalf.
            "WorkflowTaskCacheRefresher",
            "IFlowableReadThrough"
        ];

        foreach (Match match in Regex.Matches(
                     source, @"(?<group>executions|tasks)\.MapGet\(""(?<route>[^""]+)"".*?\n(?<params>.*?)\n\s*\{", RegexOptions.Singleline))
        {
            // `tasks:` prefixed so two groups' routes cannot collide in one set.
            var group = match.Groups["group"].Value;
            var route = group == "tasks"
                ? $"tasks:{match.Groups["route"].Value}"
                : match.Groups["route"].Value;

            allRoutes.Add(route);

            var parameters = match.Groups["params"].Value;

            // IFlowableReadThrough is the cache's OWN read-through, which is the
            // thing #104 built -- it is not a live read escaping the cache, so it
            // does not put a route on the list by itself.
            if (engineReaching
                .Where(t => t != "IFlowableReadThrough")
                .Any(t => parameters.Contains(t, StringComparison.Ordinal)))
            {
                actual.Add(route);
            }
        }

        // Vacuity guard: if the regex stops matching, every assertion below is
        // trivially satisfied by an empty set.
        // EXACT, not a floor. The old `>= 9` was set to #104's inventory and never
        // moved; the file has carried 17 GET routes since, so five could have gone
        // invisible to the regex with the threshold still passing -- and it is the
        // crept arm, the one that catches NEW live reads, that depends on the
        // regex seeing them. House style for pins here is exact (tests/tiers.env,
        // ExecutionOracleSizeTests): growth has to be as visible as loss.
        Assert.True(
            allRoutes.Count == 17,
            $"Expected 17 GET routes in {EndpointsPath}; found {allRoutes.Count}. "
            + "If a route was added or removed, move this number in the same commit. "
            + "If MapGet's shape changed, this guard is looking at nothing.");

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
    /// `/tasks` reads LIVE, and that is now said out loud (#604, #635).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inverted, because it was asserting something false.</b> #104 wrote this
    /// as "`/tasks` is served from the cache, asserted at the source", to pin the
    /// direction of travel for the one route that story moved. #604 then moved it
    /// back: `ReadThroughOpenTasksAsync` calls the engine unconditionally, with no
    /// freshness window, on every request — deliberately, and for a stated reason
    /// — while this test went on claiming the opposite.
    /// </para>
    /// <para>
    /// It could not notice because the scan above read the handler's parameter
    /// list for `IFlowableClient`, and the live read had moved behind an injected
    /// helper. So the story's own pin outlived the story's own decision by a
    /// milestone.
    /// </para>
    /// <para>
    /// Kept rather than deleted: the direction of travel is still worth pinning,
    /// it just points the other way now, and a future attempt to serve `/tasks`
    /// from the cache should have to come here and say so.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_tasks_route_reads_live_and_is_listed_with_its_reason()
    {
        Assert.Contains("/{processInstanceId}/tasks", LiveReadsAllowed.Keys);

        // Listed WITH a reason, not merely listed. An entry whose reason is empty
        // is an exemption nobody had to justify.
        Assert.False(
            string.IsNullOrWhiteSpace(LiveReadsAllowed["/{processInstanceId}/tasks"]),
            "A live-reading route must carry the reason it cannot be cache-served.");
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
