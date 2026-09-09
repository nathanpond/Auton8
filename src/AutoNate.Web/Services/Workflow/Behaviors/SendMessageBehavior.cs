using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Workflow.Behaviors;

// #112. The send half of message correlation.
//
// Every message-throwing element runs through here: a send task the author wired
// up directly, and — after publish-time expansion — an intermediate throw
// (Message) event and a message end event, neither of which Flowable 8.0.0
// executes as written. One behaviour rather than three send paths is what makes
// "the throw side and the receive side agree on one correlation model" true by
// construction instead of by inspection.
//
// Configuration is read from the AUTHORED diagram by activity id, not from
// process variables. The published copy has been rewritten into a service task,
// but the stored diagram still carries the message event the author drew, so this
// resolves what to send from the thing they actually configured.
public sealed class SendMessageBehavior : IWorkflowBehavior
{
    public const string BehaviorKey = "autonate.send-message";
    public const string ResultVariableName = "sendMessageResult";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SendMessageBehavior> _log;

    public SendMessageBehavior(IServiceScopeFactory scopeFactory, ILogger<SendMessageBehavior> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public string Key => BehaviorKey;

    public string DisplayName => "Send Message";

    public string? Description =>
        "Delivers a message to a waiting instance of another workflow, addressed by " +
        "the correlation value configured on this element. Sets a `sendMessageResult` " +
        "variable so the workflow can branch on whether it arrived.";

    public async Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var models = scope.ServiceProvider.GetRequiredService<IWorkflowModelStore>();

        var model = await models.GetByProcessKeyAsync(context.ProcessDefinitionKey, cancellationToken);
        if (model is null || string.IsNullOrWhiteSpace(model.BpmnXml))
        {
            return Fail("senderNotFound",
                $"No stored diagram for process key '{context.ProcessDefinitionKey}'.");
        }

        var declaration = WorkflowBpmnXml
            .ExtractMessageSendDeclarations(model.BpmnXml)
            .FirstOrDefault(d => string.Equals(d.ElementId, context.ActivityId, StringComparison.Ordinal));

        if (declaration is null)
        {
            return Fail("notASend",
                $"Element '{context.ActivityId}' does not send a message in the stored diagram.");
        }

        if (string.IsNullOrWhiteSpace(declaration.MessageName))
        {
            return Fail("noMessageName", "This element has no message name to send.");
        }

        // Broadcast is deliberately not a feature, so a send with nowhere to go
        // fails loudly rather than fanning out. Reported as a result variable so
        // the author sees it in history rather than only in a log.
        if (string.IsNullOrWhiteSpace(declaration.TargetProcessKey))
        {
            return Fail("noTargetProcess",
                $"Element '{context.ActivityId}' does not say which workflow to send to.");
        }

        string? correlationValue = null;
        if (!string.IsNullOrWhiteSpace(declaration.CorrelationKey))
        {
            if (!context.Variables.TryGetValue(declaration.CorrelationKey, out var raw))
            {
                return Fail("missingCorrelationValue",
                    $"Process variable '{declaration.CorrelationKey}' carries the correlation " +
                    "value for this send, and is not set.");
            }

            correlationValue = raw.ValueKind == System.Text.Json.JsonValueKind.String
                ? raw.GetString()
                : raw.ToString();
        }

        var correlator = scope.ServiceProvider.GetRequiredService<WorkflowMessageCorrelator>();
        var result = await correlator.CorrelateAsync(
            declaration.TargetProcessKey,
            declaration.MessageName,
            correlationValue,
            variables: null,
            cancellationToken);

        _log.LogInformation(
            "SendMessageBehavior: '{Message}' from {SenderKey}/{ActivityId} to {TargetKey} -> {Outcome}.",
            declaration.MessageName, context.ProcessDefinitionKey, context.ActivityId,
            declaration.TargetProcessKey, result.Outcome);

        // A send that reached nobody is NOT a failure of this process: "nothing
        // was waiting" is a legitimate outcome the author may want to branch on,
        // and failing the activity would dead-letter a job over someone else's
        // process not being ready. The outcome is surfaced instead.
        return BehaviorResult.Ok(Variable(result.Outcome switch
        {
            WorkflowMessageCorrelator.Outcome.Delivered => "delivered",
            WorkflowMessageCorrelator.Outcome.Started => "started",
            WorkflowMessageCorrelator.Outcome.NoMatch => "noMatch",
            WorkflowMessageCorrelator.Outcome.MultipleMatches => "multipleMatches",
            WorkflowMessageCorrelator.Outcome.UnknownProcess => "unknownTargetProcess",
            WorkflowMessageCorrelator.Outcome.UnknownMessage => "unknownMessage",
            WorkflowMessageCorrelator.Outcome.AmbiguousMessage => "ambiguousMessage",
            _ => "unknown"
        }));
    }

    private static BehaviorResult Fail(string code, string message) =>
        BehaviorResult.Fail(code, message, Variable(code));

    private static IReadOnlyDictionary<string, BehaviorVariableValue> Variable(string value) =>
        new Dictionary<string, BehaviorVariableValue>(StringComparer.Ordinal)
        {
            [ResultVariableName] = BehaviorVariableValue.String(value),
        };
}
