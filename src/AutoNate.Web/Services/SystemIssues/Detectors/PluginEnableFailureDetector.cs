using System.Text.Json;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.BusWatcher;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Reactive detector. Subscribes to the in-process bus watcher (which already
// receives every application.events message via DaprStreamingSubscriber) and
// opens a category=plugin issue whenever a plugin.enable_failed event arrives.
//
// Plugins fail to enable for many reasons — bad assembly, missing dependency,
// migration error, schema provisioning failure. The audit envelope itself
// already records the failure, but it's just one row in a busy firehose; an
// open issue makes it visible in the System Issues page until an operator
// actually fixes the plugin.
//
// Mirrors WorkflowExecutionErrorRecorder's bus-listener shape exactly.
public sealed class PluginEnableFailureDetector(
    BusWatcherStreamService busWatcher,
    ISystemIssueRecorder recorder,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<PluginEnableFailureDetector> logger) : IHostedService
{
    public const string DetectorIdValue = "plugin_enable_failure";

    private readonly SystemIssueOptions _options = systemIssueOptions.Value;
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.DetectorsEnabled)
        {
            logger.LogInformation(
                "Detector {DetectorId} disabled via {Section}:DetectorsEnabled.",
                DetectorIdValue, SystemIssueOptions.SectionName);
            return Task.CompletedTask;
        }
        _subscription = busWatcher.Subscribe(HandleAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    // Public so tests can drive a single message without spinning up the
    // subscription. Mirrors RunOnceAsync on the periodic detectors.
    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(message.Topic, DaprApplicationEventPublisher.TopicName, StringComparison.Ordinal))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(message.Payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            var eventType = ReadString(root, "eventType");
            if (!string.Equals(eventType, ApplicationEventTypes.PluginEnableFailed, StringComparison.Ordinal))
            {
                return;
            }

            var (pluginId, pluginName, errorMessage) = ReadPluginPayload(root);
            // Fingerprint per plugin id keeps repeated failures of the same
            // plugin folded into one row; a different plugin gets its own.
            var fingerprint = pluginId is null
                ? "plugin:enable_failed:unknown"
                : $"plugin:enable_failed:{pluginId}";

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Plugin,
                Severity: SystemIssueSeverities.Error,
                Fingerprint: fingerprint,
                Title: pluginName is null
                    ? "Plugin failed to enable"
                    : $"Plugin '{pluginName}' failed to enable",
                Summary: errorMessage,
                RelatedEntityKind: "plugin",
                RelatedEntityId: pluginId,
                FactsJson: JsonSerializer.Serialize(new
                {
                    pluginId,
                    pluginName,
                    errorMessage
                })), cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "PluginEnableFailureDetector could not parse application event payload.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PluginEnableFailureDetector failed to record an issue for a plugin.enable_failed message.");
        }
    }

    private static (string? PluginId, string? PluginName, string? ErrorMessage) ReadPluginPayload(JsonElement root)
    {
        // ApplicationEventEnvelope wraps the per-event payload in a `payload`
        // property. Plugin events put { id, name, error } there.
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null);
        }
        return (
            ReadString(payload, "id") ?? ReadString(payload, "pluginId"),
            ReadString(payload, "name") ?? ReadString(payload, "pluginName"),
            ReadString(payload, "error") ?? ReadString(payload, "errorMessage") ?? ReadString(payload, "message"));
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            _ => null
        };
    }
}
