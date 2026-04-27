using AutoNate.Web.Configuration;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace AutoNate.Web.Services.Nats;

// Ensures the JetStream streams that back our pub/sub topics exist before the
// app starts publishing. JetStream requires every published subject to be
// covered by some stream — without this, publishes either error or silently
// drop.
//
// Streams are derived from the topics our publishers use: the only source of
// truth lives in the publisher classes, which are referenced here so a renamed
// topic can't drift out of sync with its stream.
public sealed class NatsStreamProvisioner(
    IOptions<NatsOptions> natsOptions,
    ILogger<NatsStreamProvisioner> logger)
{
    private readonly NatsOptions _options = natsOptions.Value;

    // The Dapr pub/sub component (infra/dapr/components/pubsub.yaml) is bound
    // to one stream via `streamName: workflow-execution`, so every Dapr
    // subscription — regardless of topic — consumes from this stream. That
    // means a single stream must cover the subjects for every topic the app
    // publishes or subscribes to. New top-level topic prefixes need a new
    // entry here.
    private static readonly StreamConfig[] DesiredStreams =
    [
        new StreamConfig(name: "workflow-execution", subjects: new[]
        {
            $"{BusWatcherStreamService.TopicRoot}.>",
            $"{DaprRecordEventPublisher.TopicRoot}.>"
        })
    ];

    // Streams that previous versions of the app provisioned but no longer
    // owns. We delete them so their subject filters don't collide with the
    // current DesiredStreams (JetStream rejects overlapping subjects across
    // streams). Removing an entry from DesiredStreams should be paired with
    // an entry here for one release.
    private static readonly string[] LegacyStreamsToRemove =
    [
        "autonate-records"
    ];

    public async Task EnsureStreamsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            logger.LogInformation(
                "Skipping JetStream stream provisioning: Nats:Url is not configured.");
            return;
        }

        await using var connection = new NatsConnection(new NatsOpts { Url = _options.Url });
        await connection.ConnectAsync();

        var js = new NatsJSContext(connection);

        foreach (var legacyName in LegacyStreamsToRemove)
        {
            try
            {
                await js.DeleteStreamAsync(legacyName, cancellationToken);
                logger.LogInformation(
                    "Removed legacy JetStream stream '{StreamName}'.", legacyName);
            }
            catch (NatsJSApiException exception) when (exception.Error.Code == 404)
            {
                // Stream wasn't there — nothing to clean up.
            }
        }

        foreach (var streamConfig in DesiredStreams)
        {
            await js.CreateOrUpdateStreamAsync(streamConfig, cancellationToken);
            logger.LogInformation(
                "JetStream stream '{StreamName}' is ready (subjects: {Subjects}).",
                streamConfig.Name,
                string.Join(", ", streamConfig.Subjects ?? Array.Empty<string>()));
        }
    }
}
