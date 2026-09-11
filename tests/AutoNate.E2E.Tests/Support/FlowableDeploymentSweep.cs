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
    /// How old a suite deployment must be before the sweep will take it (#304).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two hours, matching <c>PostgresTestDatabase.SweepAbandonedDatabasesAsync</c>
    /// — the sibling sweep that has used exactly this rule, for exactly this
    /// reason, since #191. A two-hour-old <c>e2e-*</c> deployment cannot belong to
    /// a live run; a two-minute-old one might.
    /// </para>
    /// <para>
    /// <b>#297's cut-off was wrong twice over and this replaces it.</b> It was
    /// <c>static readonly DateTimeOffset RunStartedAt = DateTimeOffset.UtcNow</c>,
    /// and a static initialiser runs on first access to the TYPE — which here is
    /// <c>CreateClient</c> on the line above the sweep call. Measured: the field
    /// was set 1206 ms after a marker placed before that line, i.e. it recorded
    /// when the sweep ran, not when the run began. And even set correctly, "older
    /// than the moment I started" only spares runs that started AFTER me — the
    /// runs it destroys are the ones already going, which is the whole population
    /// #297 was about.
    /// </para>
    /// <para>
    /// #257's history is not an argument against age. It records that sweeping by
    /// <b>age alone</b> deleted a developer's real work, because at that time the
    /// prefix matched nothing and age was the only signal available. Age AND a
    /// prefix that now genuinely matches is the combination that was never
    /// available before.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromHours(2);

    internal static async Task<int> SweepAsync(HttpClient client) =>
        await SweepAsync(client, DateTimeOffset.UtcNow - MinimumAge);

    /// <param name="onlyNamed">
    /// When given, only deployments whose name contains this survive the prefix
    /// filter (#308). The sweep's own tests use it so they exercise the real
    /// predicate against a population they created, instead of cascade-deleting
    /// every <c>e2e-*</c> deployment on a shared engine — which is the destruction
    /// #297 was filed about, committed by the file that guards the rule.
    /// </param>
    internal static async Task<int> SweepAsync(
        HttpClient client, DateTimeOffset createdBefore, string? onlyNamed = null)
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
                // #297/#304. And only what is old enough that no live run can own
                // it. A deployment younger than this may be a CONCURRENT run's, and
                // deleting it cascades away its live instances, jobs and history.
                //
                // A deployment with no readable time is left alone rather than
                // swept: the whole point is that we could not tell whose it is.
                .Where(deployment => deployment.CreatedAt is { } createdAt
                                     && createdAt < createdBefore)
                // #308. A test's own plants, when it says so.
                .Where(deployment => onlyNamed is null
                                     || deployment.Name.Contains(onlyNamed, StringComparison.Ordinal))
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
