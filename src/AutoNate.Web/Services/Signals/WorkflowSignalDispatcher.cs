using System.Text.Json;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Services.Signals;

// Forwards bus messages to Flowable as signals when their `eventType` matches
// a configured signal name on the message's topic. The DaprStreamingSubscriber
// owns the subscription lifecycle and routes messages here directly — this
// type is a stateless message handler.
public sealed class WorkflowSignalDispatcher(
    IWorkflowSignalRegistry registry,
    IFlowableClient flowableClient,
    ILogger<WorkflowSignalDispatcher> logger)
{
    private readonly IWorkflowSignalRegistry _registry = registry;
    private readonly IFlowableClient _flowableClient = flowableClient;
    private readonly ILogger<WorkflowSignalDispatcher> _logger = logger;

    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message)
    {
        var signalsForTopic = _registry.GetSignalNamesForTopic(message.Topic);
        if (signalsForTopic.Count == 0)
        {
            return;
        }

        var eventType = TryReadEventType(message.Payload);
        if (eventType is null)
        {
            _logger.LogWarning(
                "Discarding bus message on topic {Topic}: payload is missing or malformed `eventType` field.",
                message.Topic);
            return;
        }

        if (!signalsForTopic.Contains(eventType))
        {
            return;
        }

        try
        {
            // Forward the raw JSON string as eventData. Flowable stores it as
            // a string variable; downstream script tasks `JSON.parse(eventData)`.
            await _flowableClient.BroadcastSignalAsync(
                eventType,
                new Dictionary<string, object?> { ["eventData"] = message.Payload });

            _logger.LogInformation(
                "Broadcasted signal '{SignalName}' from topic {Topic}.",
                eventType,
                message.Topic);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to broadcast signal '{SignalName}' from topic {Topic}.",
                eventType,
                message.Topic);
        }
    }

    private static string? TryReadEventType(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("eventType", out var eventTypeElement))
            {
                return null;
            }

            if (eventTypeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = eventTypeElement.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
