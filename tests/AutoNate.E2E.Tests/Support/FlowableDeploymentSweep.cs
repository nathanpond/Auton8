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

    internal static async Task<int> SweepAsync(HttpClient client)
    {
        List<(string Id, string Name)> deployments;
        try
        {
            using var response = await client.GetAsync("service/repository/deployments?size=1000");
            if (!response.IsSuccessStatusCode) return 0;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            deployments = document.RootElement.GetProperty("data").EnumerateArray()
                .Select(element => (
                    Id: element.GetProperty("id").GetString() ?? string.Empty,
                    Name: element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty))
                .Where(deployment => deployment.Name.StartsWith(SuitePrefix, StringComparison.Ordinal))
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
