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

    // #257. The prefix above matched NOTHING the suite actually produces.
    //
    // A deployment is named after the model's process key
    // (`FlowableClient.cs:51` -> "{ProcessKey}.bpmn20.xml"), and every E2E
    // process key is a short generated stem — `adh…`, `cgx…`, `mc…`, `rp_on_…`.
    // Underscore, not hyphen, and never "e2e-". So the sweep removed nothing for
    // the whole of M4 while its one test stayed green, because that test
    // deployed its own `e2e-…` fixture directly instead of going through the
    // publish path the suite uses. The engine reached 1,306 deployments.
    //
    // Age is the honest signal, and the only one available: the suite drops its
    // database every run, so anything it deployed is orphaned by definition, and
    // an orphan cannot be told apart from a developer's work by NAME without the
    // guessing #214 was careful to avoid. A generous cutoff keeps a developer's
    // current session safe; the deployment they made three hours ago on the test
    // engine is not something this suite can preserve and also do its job.
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(3);

    internal static async Task<int> SweepAsync(HttpClient client)
    {
        var cutoff = DateTimeOffset.UtcNow - OrphanAge;
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
                    DeployedAt: element.TryGetProperty("deploymentTime", out var time)
                                && time.ValueKind == JsonValueKind.String
                                && DateTimeOffset.TryParse(time.GetString(), out var parsed)
                        ? parsed
                        : (DateTimeOffset?)null))
                // The legacy prefix at any age, plus anything old enough to be an
                // orphan. A deployment with no timestamp is left alone rather than
                // guessed at.
                .Where(deployment =>
                    deployment.Name.StartsWith(SuitePrefix, StringComparison.Ordinal)
                    || (deployment.DeployedAt is { } at && at < cutoff))
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
