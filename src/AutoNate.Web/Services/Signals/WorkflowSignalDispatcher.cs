using System.Text.Json;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Services.Signals;

// Forwards bus messages to Flowable as signals when their `eventType` matches
// a configured signal name on the message's topic. The DaprStreamingSubscriber
// owns the subscription lifecycle and routes messages here directly — this
// type is a stateless message handler.
public sealed class WorkflowSignalDispatcher(
    IWorkflowSignalRegistry registry,
    IFlowableClient flowableClient,
    IRecordTypeShortCodeResolver recordTypeResolver,
    ILogger<WorkflowSignalDispatcher> logger)
{
    private readonly IWorkflowSignalRegistry _registry = registry;
    private readonly IFlowableClient _flowableClient = flowableClient;
    private readonly IRecordTypeShortCodeResolver _recordTypeResolver = recordTypeResolver;
    private readonly ILogger<WorkflowSignalDispatcher> _logger = logger;

    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message)
    {
        var registrations = _registry.GetRegistrationsForTopic(message.Topic);
        if (registrations.Count == 0)
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

        var matching = registrations
            .Where(r => string.Equals(r.SignalName, eventType, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length == 0)
        {
            return;
        }

        // Resolve the payload's recordTypeId (if any) once up front: every
        // filtered registration tests against the same shortcode.
        var payloadRecordTypeId = TryReadGuid(message.Payload, "recordTypeId");
        string? resolvedShortCode = null;
        if (payloadRecordTypeId is Guid id
            && _recordTypeResolver.TryGetShortCode(id, out var sc))
        {
            resolvedShortCode = sc;
        }

        // Start one process per matching registration. Each call gets its own
        // try/catch so a single Flowable failure can't abort siblings.
        foreach (var registration in matching)
        {
            // Per-registration record-type filter. An empty filter set means
            // "no filter" and always passes; a non-empty set requires the
            // payload's recordTypeId to resolve to a shortcode in the set.
            if (registration.RecordTypeShortCodes.Count > 0)
            {
                if (resolvedShortCode is null
                    || !registration.RecordTypeShortCodes.Contains(resolvedShortCode))
                {
                    _logger.LogInformation(
                        "Skipping {ProcessDefinitionKey} for signal '{SignalName}': record-type filter excluded payload (recordTypeId={RecordTypeId}, shortCode={ShortCode}).",
                        registration.ProcessDefinitionKey, eventType, payloadRecordTypeId, resolvedShortCode);
                    continue;
                }
            }

            try
            {
                // Forward the raw JSON string as eventData. Flowable stores it as
                // a string variable; downstream script tasks `JSON.parse(eventData)`.
                await _flowableClient.StartProcessInstanceAsync(
                    registration.ProcessDefinitionKey,
                    variables: new Dictionary<string, object?> { ["eventData"] = message.Payload });

                _logger.LogInformation(
                    "Started workflow {ProcessDefinitionKey} from signal '{SignalName}' on topic {Topic}.",
                    registration.ProcessDefinitionKey, eventType, message.Topic);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to start workflow {ProcessDefinitionKey} from signal '{SignalName}' on topic {Topic}.",
                    registration.ProcessDefinitionKey, eventType, message.Topic);
            }
        }

        // Wake any waiting intermediate-catch executions on this signal.
        IReadOnlyList<string> waitingExecutionIds;
        try
        {
            waitingExecutionIds = await _flowableClient
                .ListExecutionsBySignalSubscriptionAsync(eventType);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to list executions waiting on signal '{SignalName}'. Skipping intermediate-catch dispatch.",
                eventType);
            return;
        }

        foreach (var executionId in waitingExecutionIds)
        {
            try
            {
                await _flowableClient.SignalExecutionAsync(
                    executionId,
                    new Dictionary<string, object?> { ["eventData"] = message.Payload });
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to signal execution {ExecutionId} for signal '{SignalName}'.",
                    executionId, eventType);
            }
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

    private static Guid? TryReadGuid(string payload, string fieldName)
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

            if (!document.RootElement.TryGetProperty(fieldName, out var element))
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return Guid.TryParse(element.GetString(), out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
