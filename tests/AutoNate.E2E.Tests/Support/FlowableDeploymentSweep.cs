using System.Text;
using System.Text.Json;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Removes Flowable deployments the E2E suite created (#214).
/// </summary>
/// <remarks>
/// <para>
/// Unlike a test database, these accumulate in the **one** engine every developer and
/// every run shares — 388 when this was written, 303 of them from the suite. A
/// deployment carries its process definitions, instances and history with it, so the
/// cost is not only tidiness.
/// </para>
/// <para>
/// **The guard is the suite's own naming convention, not a heuristic.**
/// <see cref="TestNames.Prefixed"/> produces <c>e2e-…</c>, and only that prefix is
/// swept. The same engine held deployments named <c>autonate</c>, <c>default</c>,
/// <c>car</c> and <c>account</c> — a developer's real work, which a looser rule would
/// have deleted.
/// </para>
/// </remarks>
internal static class FlowableDeploymentSweep
{
    private const string SuitePrefix = "e2e-";

    /// <summary>
    /// What the fixture sets `Flowable:DeploymentNamePrefix` to (#257).
    /// </summary>
    /// <remarks>
    /// One constant, so the thing that CREATES the name and the thing that
    /// matches it cannot drift — which is precisely how the original prefix came
    /// to match nothing while its test stayed green.
    /// </remarks>
    internal const string SuiteDeploymentPrefix = SuitePrefix;

    // #257, second pass. The prefix now matches, because the suite MAKES it match.
    //
    // First pass swept by age instead, and that removed the orphans by removing
    // the guard: it deleted any deployment older than three hours whatever its
    // name, including a developer's own work on the same engine — the exact thing
    // the remarks above promise it will not do, and 348 such deployments went in
    // one observed sweep. Age is not the honest signal; it was the only one
    // available while the suite deployed under names it could not recognise.
    //
    // `Flowable:DeploymentNamePrefix` (unset in production) makes the E2E fixture
    // deploy as `e2e-<key>.bpmn20.xml`, so matching by name is exact again and
    // the remarks above are true again.
    //
    // For the record, the original defect:
    //
    // A deployment is named after the model's process key
    // (`FlowableClient.cs:51` -> "{ProcessKey}.bpmn20.xml"), and every E2E
    // process key is a short generated stem — `adh…`, `cgx…`, `mc…`, `rp_on_…`.
    // Underscore, not hyphen, and never "e2e-". So the sweep removed nothing for
    // the whole of M4 while its one test stayed green, because that test
    // deployed its own `e2e-…` fixture directly instead of going through the
    // publish path the suite uses. The engine reached 1,306 deployments.
    //
    /// <summary>
    /// Deployments created at or after this instant are never swept (#297).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set once, at the moment the suite starts. The prefix says <b>this suite</b>
    /// made it; this says <b>an earlier run</b> made it. Both are needed, because
    /// "the suite made it" is true of a run happening right now on the same engine.
    /// </para>
    /// <para>
    /// #248 named two halves and only one was fixed. The database half became a
    /// per-run <c>autonate_e2e_&lt;guid&gt;</c>; this half kept deleting every
    /// <c>e2e-*</c> deployment with <c>cascade=true</c> at fixture startup, on the
    /// reasoning that they were "from earlier runs". They are not necessarily from
    /// earlier runs. A verifier watched a live <c>boom</c> job vanish from the
    /// jobs, timer-jobs and dead-letter tables mid-retry because another agent's
    /// suite had just started — the same test passed in 39 s once the engine was
    /// quiet.
    /// </para>
    /// <para>
    /// That matters here more than it would elsewhere: this milestone's execution
    /// evidence is CI-excluded by design, so the local run is the only thing that
    /// exercises it, and it has been verified six times by parallel agents — which
    /// is exactly the workload the unscoped sweep destroys.
    /// </para>
    /// </remarks>
    internal static readonly DateTimeOffset RunStartedAt = DateTimeOffset.UtcNow;

    internal static async Task<int> SweepAsync(HttpClient client) =>
        await SweepAsync(client, RunStartedAt);

    /// <param name="createdBefore">
    /// Only deployments older than this are swept. Injected so the tests can prove
    /// the cut-off rather than race it.
    /// </param>
    internal static async Task<int> SweepAsync(HttpClient client, DateTimeOffset createdBefore)
    {
        List<(string Id, string Name)> deployments;
        try
        {
            // Oldest first, so a backlog larger than one page is drained from the
            // end that matters. The engine reached 1,306 deployments while this
            // swept nothing; a single unsorted page would keep missing the
            // oldest ones even once the matching is fixed.
            using var response = await client.GetAsync(
                "service/repository/deployments?size=1000&sort=deployTime&order=asc");
            if (!response.IsSuccessStatusCode) return 0;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            deployments = document.RootElement.GetProperty("data").EnumerateArray()
                .Select(element => (
                    Id: element.GetProperty("id").GetString() ?? string.Empty,
                    Name: element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    CreatedAt: element.TryGetProperty("deploymentTime", out var deployedAt)
                        && DateTimeOffset.TryParse(
                            deployedAt.GetString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal
                                | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var parsed)
                        ? parsed
                        : (DateTimeOffset?)null))
                // Only what the suite deployed. A deployment this rule does not
                // match is somebody's work, at any age.
                .Where(deployment =>
                    deployment.Name.StartsWith(SuitePrefix, StringComparison.Ordinal))
                // #297. And only what already existed when this run began. A
                // deployment created since is a CONCURRENT run's, and deleting it
                // cascades away its live instances, jobs and history.
                //
                // A deployment with no readable time is left alone rather than
                // swept: the whole point is that we could not tell whose it is.
                .Where(deployment => deployment.CreatedAt is { } createdAt
                                     && createdAt < createdBefore)
                .Select(deployment => (deployment.Id, deployment.Name))
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            // No engine, or an engine that answered something unexpected. Cleanup
            // never fails a run.
            return 0;
        }

        var deleted = 0;
        foreach (var (id, _) in deployments)
        {
            try
            {
                // cascade takes the definitions, instances and history with it —
                // without it the rows outlive the deployment and nothing is gained.
                using var response = await client.DeleteAsync(
                    $"service/repository/deployments/{Uri.EscapeDataString(id)}?cascade=true");
                if (response.IsSuccessStatusCode) deleted++;
            }
            catch (HttpRequestException)
            {
                // Another run took it, or it is busy. The next sweep gets it.
            }
        }

        return deleted;
    }

    internal static HttpClient CreateClient(string baseUrl, string user, string password)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        return client;
    }
}
