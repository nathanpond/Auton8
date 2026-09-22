using AutoNate.Web.Configuration;
using AutoNate.Web.Services.Agent;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Dashboards;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.Workflow;
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
    ILogger<NatsStreamProvisioner> logger,
    ISystemIssueRecorder? issues = null)
{
    private readonly NatsOptions _options = natsOptions.Value;

    // Stale messages on this stream are useless and actively harmful: any new
    // ephemeral Dapr consumer with deliver_policy=all replays the whole
    // backlog on reconnect, which fan-outs into spurious signal-start
    // workflow runs. Cap retention to one day so the backlog stays bounded
    // even if a consumer is briefly misconfigured. Combined with the
    // `deliverPolicy: new` setting on the pub/sub component, this is the
    // defence-in-depth pair against replay storms.
    private static readonly TimeSpan StreamMaxAge = TimeSpan.FromHours(24);

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
            $"{DaprRecordEventPublisher.TopicRoot}.>",
            $"{DaprApplicationEventPublisher.TopicRoot}.>",
            $"{DaprNotificationEventPublisher.TopicRoot}.>",
            $"{AuthEventTopic.TopicRoot}.>",
            $"{IamEventTopic.TopicRoot}.>",
            $"{RecordSchemaEventTopic.TopicRoot}.>",
            $"{SiteEventTopic.TopicRoot}.>",
            $"{WorkflowAdminEventTopic.TopicRoot}.>",
            // system.issues lifecycle events from the self-healing platform.
            // SiteEventTopic uses TopicRoot="site" and SystemIssueEventTopic
            // uses TopicRoot="system" — distinct prefixes so the wildcards
            // don't overlap.
            $"{SystemIssueEventTopic.TopicRoot}.>",
            $"{AgentEventTopic.TopicRoot}.>",
            $"{ExternalConnectionEventTopic.TopicRoot}.>",
            // Content hierarchy (project/cabinet/notebook/page/note) lifecycle
            // events. Without this, publishes to `content.events` get
            // "no response from stream" → Dapr returns HTTP 500 to the
            // outbox dispatcher.
            $"{ContentEventTopic.TopicRoot}.>",
            // User-owned dashboards lifecycle events (dashboards.dashboard.*,
            // dashboards.widget.*, dashboards.layout.*). Without this, the
            // outbox dispatcher loops with HTTP 500 on every dashboard CRUD.
            $"{DashboardEventTopic.TopicRoot}.>",
            // AQL query.executed / query.failed events emitted by the /api/query
            // endpoint. Same outbox-loop failure mode as the dashboards stream
            // if this isn't registered.
            $"{QueryEventTopic.TopicRoot}.>",
            // DataStore CRUD + file/folder operations + CSV ingest. Critical
            // for "who took bytes out" audit via datastore.file.downloaded.
            $"{DataStoreEventTopic.TopicRoot}.>",
            // #524. The default topic a message START event listens on. NOT a
            // `.>` wildcard: Dapr publishes to the topic name itself, so the
            // subject is `workflow.messages` exactly and a wildcard on it would
            // match `workflow.messages.anything` and miss the one that is used.
            // `workflow.*` would be wide enough and would also swallow
            // `workflow.execution`, which has its own retention story.
            //
            // Without this the publish fails at the sidecar with
            // "nats: no response from stream" and HTTP 500 -- measured, when the
            // queue-start E2E first ran -- which is the same failure mode the
            // content and dashboards entries above were added for.
            WorkflowBpmnXml.DefaultMessageTopic,
            // #540. The default topic a SIGNAL start event listens on, missing
            // for as long as signals have had one. A signal start whose author
            // set no topic gets this -- and the studio writes it, so it is the
            // ordinary case, not an edge -- and anything published to it failed
            // at the sidecar with "nats: no response from stream".
            //
            // It survived because no test crossed that hop: every signal test
            // reached the engine or the dispatcher directly. The bus-crossing
            // test added alongside this is what makes the subject list something
            // a run can disagree with rather than something a reader has to
            // notice.
            WorkflowBpmnXml.DefaultSignalTopic
        })
        {
            MaxAge = StreamMaxAge
        },
        // NOTE: `pipeline-code-run.>` (code-node execution) is deliberately NOT
        // a stream. It is core request/reply between JetStreamCodeNodeRunner and
        // the services/executor queue subscriber; capturing it in a stream made
        // JetStream answer every request with a PubAck before the sidecar could
        // reply (archived-141). See LegacyStreamsToRemove.
    ];

    // Streams that previous versions of the app provisioned but no longer
    // owns. We delete them so their subject filters don't collide with the
    // current DesiredStreams (JetStream rejects overlapping subjects across
    // streams). Removing an entry from DesiredStreams should be paired with
    // an entry here for one release.
    private static readonly string[] LegacyStreamsToRemove =
    [
        "autonate-records",
        // Captured `pipeline-code-run.>` and shadowed executor replies (archived-141).
        "pipeline-code-runs"
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
            await CheckStorageFidelityAsync(js, streamConfig.Name!, cancellationToken);
        }
    }

    /// <summary>
    /// The subject the fidelity probe publishes on (#636). Under the `system.>`
    /// wildcard the stream already captures, so it needs no subject of its own.
    /// </summary>
    public const string FidelityProbeSubject = "system.stream-fidelity-probe";

    public const string FidelityDetectorId = "nats_stream_fidelity";

    /// <summary>
    /// Publish one message, count what the stream stored (#636). Measured on the
    /// shared dev server: the `workflow-execution` stream stored ONE inbound
    /// message FOUR times -- from a plain NATS client, no sidecar involved --
    /// and every stored copy is delivered once, so one bus message started
    /// four workflow instances. Nothing in the stream's config produces that
    /// (identical updates, reorders, remove/re-add cycles and live consumers
    /// were each tried on a throwaway stream and stored once); it is state the
    /// server holds, and only recreating the stream has cleared it. The app
    /// cannot repair that. It can refuse to be silent about it.
    /// </summary>
    internal static async Task<int> MeasureStorageFidelityAsync(
        NatsJSContext js, string streamName, string probeSubject, CancellationToken cancellationToken)
    {
        var before = await StoredOnSubjectAsync(js, streamName, probeSubject, cancellationToken);
        var ack = await js.PublishAsync(
            probeSubject,
            "{\"eventType\":\"stream.fidelity.probe\"}",
            cancellationToken: cancellationToken);
        ack.EnsureSuccess();
        var after = await StoredOnSubjectAsync(js, streamName, probeSubject, cancellationToken);
        return (int)(after - before);
    }

    private static async Task<long> StoredOnSubjectAsync(NatsJSContext js, string streamName, string subject, CancellationToken cancellationToken)
    {
        var stream = await js.GetStreamAsync(streamName, new StreamInfoRequest { SubjectsFilter = subject }, cancellationToken);
        return stream.Info.State.Subjects is { } subjects && subjects.TryGetValue(subject, out var count) ? count : 0;
    }

    private async Task CheckStorageFidelityAsync(NatsJSContext js, string streamName, CancellationToken cancellationToken)
    {
        int copies;
        try
        {
            copies = await MeasureStorageFidelityAsync(js, streamName, FidelityProbeSubject, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The probe is a diagnostic; a failure to run it must not stop the app.
            logger.LogWarning(exception, "Could not measure storage fidelity of JetStream stream '{StreamName}'.", streamName);
            return;
        }

        var fingerprint = $"stream-fidelity:{streamName}";
        try
        {
            if (copies == 1)
            {
                logger.LogInformation("JetStream stream '{StreamName}' stores each message once.", streamName);
                if (issues is not null)
                {
                    await issues.MarkResolvedByFingerprintAsync(
                        fingerprint, SystemIssueResolutionKinds.NoLongerPresent,
                        "The stream stores each message once again.", cancellationToken);
                }
                return;
            }

            // Each placeholder ONCE: the logger rejects a template that repeats a
            // name, and the first version of this line did -- which turned a
            // diagnostic into a startup crash on every test host that met the
            // multiplying stream. A report path must not be able to take the
            // app down; hence the try around all of it.
            logger.LogError(
                "JetStream stream '{StreamName}' stores each message {Copies} times: every bus message is delivered "
                + "that many times and a message-start workflow starts that many instances (#636). Recreate the stream "
                + "with `nats stream rm <name>`; the app re-provisions it on the next start.",
                streamName, copies);
            if (issues is not null)
            {
                await issues.RecordAsync(new SystemIssueDraft(
                    DetectorId: FidelityDetectorId,
                    Category: SystemIssueCategories.Bus,
                    Severity: SystemIssueSeverities.Critical,
                    Fingerprint: fingerprint,
                    Title: $"JetStream stream '{streamName}' stores each message {copies} times",
                    Summary: $"One published message was stored {copies} times, so every bus message is delivered {copies} times "
                        + $"and a workflow started by message starts {copies} instances. Recreate the stream "
                        + $"(`nats stream rm {streamName}`); the app re-provisions it on its next start. See #636.",
                    FactsJson: System.Text.Json.JsonSerializer.Serialize(new { stream = streamName, copies })), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not report the storage fidelity of JetStream stream '{StreamName}'.", streamName);
        }
    }
}
