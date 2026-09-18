using System.Text.Json;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Services.Signals;

// #524. Starts a workflow from a queue message, the way WorkflowSignalDispatcher
// fires a signal from one. A stateless handler; DaprStreamingSubscriber owns the
// subscription lifecycle and routes messages here.
//
// TWO DISPATCHERS, TWO REGISTRIES, and that pairing is what makes "a message and
// a signal with the same name do not trigger each other" true. Each consults only
// its own registrations, so the separation holds even where an author has pointed
// both kinds at one topic — which a topic-based boundary would not.
public sealed class WorkflowMessageDispatcher(
    IWorkflowMessageRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowMessageDispatcher> logger)
{
    private readonly IWorkflowMessageRegistry _registry = registry;

    // A SCOPE PER MESSAGE, not an injected correlator. This handler is a
    // singleton -- DaprStreamingSubscriber routes to it and is itself a hosted
    // singleton -- and WorkflowMessageCorrelator is scoped because it reaches
    // IWorkflowModelStore and so a DbContext. Holding one would be a captive
    // dependency: the same DbContext for the lifetime of the process, which
    // fails at startup under scope validation and leaks if it does not.
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    private readonly ILogger<WorkflowMessageDispatcher> _logger = logger;

    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message)
    {
        var registrations = _registry.GetRegistrationsForTopic(message.Topic);
        if (registrations.Count == 0)
        {
            // Not a topic any published workflow offers a message start on.
            // Silent: most traffic on most topics is nobody's workflow, and a
            // warning here would bury the one below that means something.
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
            .Where(r => string.Equals(r.MessageName, eventType, StringComparison.Ordinal))
            .ToArray();

        if (matching.Length == 0)
        {
            // #524 AC3. NOT SILENT. This topic does carry workflow messages, so a
            // name that matches none of them is a real mismatch somebody wants to
            // know about — a renamed message, a publisher and a diagram that have
            // drifted apart. Discarding it quietly is indistinguishable from the
            // feature being broken, which is the same reason the API path refuses
            // an unknown signal rather than answering 204 (#523).
            _logger.LogWarning(
                "No published workflow starts on message '{MessageName}' (topic {Topic}). "
                    + "Nothing was started. Messages this topic does start on: {Available}.",
                eventType,
                message.Topic,
                string.Join(", ", _registry.GetMessageNamesForTopic(message.Topic).Order(StringComparer.Ordinal)));
            return;
        }

        // The payload travels as `eventData`, the same name and shape the signal
        // path uses, so an author reading either kind finds their data in one
        // place rather than two.
        var variables = new Dictionary<string, object?> { ["eventData"] = message.Payload };

        foreach (var registration in matching)
        {
            try
            {
                // THROUGH THE CORRELATOR, not straight to the engine. It already
                // owns catch-beats-start precedence and multi-match refusal, and
                // a second message path with its own rules is the thing worth
                // avoiding — the same reasoning that made signal fan-out
                // wake-all rather than inventing a second behaviour (#523).
                await using var scope = _scopeFactory.CreateAsyncScope();
                var correlator = scope.ServiceProvider
                    .GetRequiredService<WorkflowMessageCorrelator>();

                var result = await correlator.CorrelateAsync(
                    registration.ProcessDefinitionKey,
                    registration.MessageName,
                    correlationValue: null,
                    variables);

                if (result.Outcome is WorkflowMessageCorrelator.Outcome.Started
                                   or WorkflowMessageCorrelator.Outcome.Delivered)
                {
                    _logger.LogInformation(
                        "Message '{MessageName}' from topic {Topic} {Outcome} workflow "
                            + "{ProcessKey} (instance {InstanceId}).",
                        registration.MessageName, message.Topic, result.Outcome,
                        registration.ProcessDefinitionKey, result.ProcessInstanceId);
                }
                else
                {
                    _logger.LogWarning(
                        "Message '{MessageName}' from topic {Topic} did not advance workflow "
                            + "{ProcessKey}: {Outcome}.",
                        registration.MessageName, message.Topic,
                        registration.ProcessDefinitionKey, result.Outcome);
                }
            }
            catch (Exception exception)
            {
                // One workflow's failure must not cost the others their message.
                // Mirrors the signal dispatcher, which swallows per-registration
                // so a single bad definition cannot take down message handling.
                _logger.LogError(
                    exception,
                    "Failed starting workflow {ProcessKey} from message '{MessageName}' on topic {Topic}.",
                    registration.ProcessDefinitionKey, registration.MessageName, message.Topic);
            }
        }
    }

    // Same field and same tolerance as the signal dispatcher's reader: a
    // non-object payload, an absent field or a non-string value are all "not
    // addressed to us" rather than an error.
    private static string? TryReadEventType(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty("eventType", out var eventType)) return null;
            if (eventType.ValueKind != JsonValueKind.String) return null;

            var value = eventType.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
