using NATS.Client.Core;
using NATS.Client.JetStream;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Removes the JetStream consumers a suite run creates (#660).
/// </summary>
/// <remarks>
/// <para>
/// The engine topics subscribe through a DURABLE consumer now, which is the
/// whole point — an ephemeral one is per process, so replicas never compete.
/// Durable means it outlives the process, and Dapr's component cannot set an
/// inactivity threshold, so nothing reaps them. On the one NATS every
/// developer and every run shares, that accumulates exactly the way Flowable
/// deployments did before <see cref="FlowableDeploymentSweep"/>.
/// </para>
/// <para>
/// The guard is the suite's own naming convention, as there: the fixture
/// writes its per-run components with an <c>e2e-</c> durable name, and only
/// that prefix is swept. A developer's <c>autonate-web-workflow-messages</c>
/// is left alone.
/// </para>
/// </remarks>
internal static class NatsConsumerSweep
{
    internal const string SuitePrefix = "e2e-";

    /// <summary>
    /// Mirrors <c>NatsStreamProvisioner</c>'s stream name. The E2E project does
    /// not reference the app, so this is the one place the two can drift; a
    /// stream that does not exist makes the sweep a no-op rather than a failure.
    /// </summary>
    internal const string StreamName = "workflow-execution";

    /// <summary>
    /// Old enough that no live run owns it. A run that never reached
    /// <c>DisposeAsync</c> leaves its consumers behind; this is what collects
    /// them without racing a run in progress.
    /// </summary>
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(2);

    private static string Url =>
        Environment.GetEnvironmentVariable("AUTONATE_NATS_URL") ?? "nats://127.0.0.1:4222";

    public static async Task<string> SweepAsync()
    {
        await using var connection = new NatsConnection(new NatsOpts { Url = Url });
        var js = new NatsJSContext(connection);

        var cutoff = DateTimeOffset.UtcNow - MinimumAge;
        var removed = 0;
        var kept = 0;
        await foreach (var consumer in js.ListConsumersAsync(StreamName))
        {
            var name = consumer.Info.Name;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(SuitePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (consumer.Info.Created > cutoff)
            {
                kept++;
                continue;
            }
            await js.DeleteConsumerAsync(StreamName, name);
            removed++;
        }

        return $"removed {removed} stale suite consumer(s), kept {kept} younger than {MinimumAge.TotalHours:0}h";
    }

    /// <summary>Deletes this run's consumers by name; missing is success.</summary>
    public static async Task DeleteAsync(IEnumerable<string> names)
    {
        var wanted = names.Distinct(StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0)
        {
            return;
        }

        await using var connection = new NatsConnection(new NatsOpts { Url = Url });
        var js = new NatsJSContext(connection);
        foreach (var name in wanted)
        {
            try
            {
                await js.DeleteConsumerAsync(StreamName, name);
            }
            catch (NatsJSApiException exception) when (exception.Error.Code == 404)
            {
                // Never created, or already gone.
            }
        }
    }
}
