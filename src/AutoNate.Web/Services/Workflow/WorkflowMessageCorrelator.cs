using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Services.Workflow;

// #112. Advancing a waiting process from outside it.
//
// Addressing is an explicit correlation key: the author declares, on the message
// element, which process variable carries the value that identifies an instance;
// a sender supplies the process key and that value. Chosen in planning over
// record-based correlation (which only covers processes started from a record)
// and over instance-id-only (which pushes the problem onto callers who know a
// business identifier, not an instance id).
//
// M5's full-collaboration work deploys a two-pool diagram as two process
// definitions, and a message flow drawn between them is a send from one and a
// catch in the other. That is exactly the shape below: the sender names a target
// process key and a correlation value, the receiver declares which variable
// carries it. Nothing here is pool-aware and nothing needs to be — a message flow
// is addressing by another name, so M5 reuses this rather than growing a second
// mechanism. Recorded here so the mismatch, if there is one, is found by reading
// this paragraph rather than by building the pool work first.
//
// A multi-match REFUSES. A correlation key is meant to be unique among waiting
// instances, so more than one match means the process is modelled wrong or the
// key is badly chosen; delivering to an arbitrary or oldest instance hides that
// until it causes something worse. Broadcast is deliberately not a feature.
public sealed class WorkflowMessageCorrelator(
    IWorkflowModelStore models,
    IFlowableClient flowable)
{
    public enum Outcome
    {
        Delivered,
        Started,
        UnknownProcess,
        UnknownMessage,
        AmbiguousMessage,
        NoMatch,
        MultipleMatches
    }

    public sealed record Result(
        Outcome Outcome,
        string? ProcessInstanceId = null,
        int MatchCount = 0,
        string? MessageName = null,
        IReadOnlyList<string>? AvailableMessages = null);

    public async Task<Result> CorrelateAsync(
        string processKey,
        string? messageName,
        string? correlationValue,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken = default)
    {
        var model = await models.GetByProcessKeyAsync(processKey, cancellationToken);
        if (model is null || string.IsNullOrWhiteSpace(model.BpmnXml))
        {
            return new Result(Outcome.UnknownProcess);
        }

        var declarations = WorkflowBpmnXml.ExtractMessageDeclarations(model.BpmnXml);
        if (declarations.Count == 0)
        {
            return new Result(Outcome.UnknownMessage, AvailableMessages: Array.Empty<string>());
        }

        // Addressable names: a message event is named by its message, a receive
        // task by its own element id — it has no message subscription at all, so
        // there is nothing else to call it.
        static string NameOf(WorkflowMessageDeclaration d) =>
            d.Kind == WorkflowMessageTargetKind.ReceiveTask ? d.ElementId : d.MessageName;

        var available = declarations.Select(NameOf).Distinct(StringComparer.Ordinal).ToList();

        WorkflowMessageDeclaration[] selected;
        if (!string.IsNullOrWhiteSpace(messageName))
        {
            selected = declarations
                .Where(d => string.Equals(NameOf(d), messageName, StringComparison.Ordinal))
                .ToArray();
            if (selected.Length == 0)
            {
                return new Result(Outcome.UnknownMessage, AvailableMessages: available);
            }
        }
        else if (available.Count == 1)
        {
            // The common case, and the one the acceptance criteria describe: a
            // process with a single way in needs no disambiguation.
            selected = declarations.ToArray();
        }
        else
        {
            // Refused rather than guessed. Picking one would deliver to whichever
            // the parser happened to see first, which is not something a caller
            // could reason about.
            return new Result(Outcome.AmbiguousMessage, AvailableMessages: available);
        }

        // A catch beats a start when both exist for the same message: the message
        // start event's job is to start an instance "when its message arrives with
        // nothing waiting", so something waiting takes precedence.
        var catching = selected
            .Where(d => d.Kind != WorkflowMessageTargetKind.Start)
            .ToArray();

        foreach (var declaration in catching)
        {
            var waiting = declaration.Kind == WorkflowMessageTargetKind.ReceiveTask
                ? await flowable.ListExecutionsAwaitingReceiveTaskAsync(
                    processKey, declaration.ElementId,
                    declaration.CorrelationKey, correlationValue, cancellationToken)
                : await flowable.ListExecutionsAwaitingMessageAsync(
                    processKey, declaration.MessageName,
                    declaration.CorrelationKey, correlationValue, cancellationToken);

            if (waiting.Count == 0) continue;

            if (waiting.Count > 1)
            {
                return new Result(
                    Outcome.MultipleMatches,
                    MatchCount: waiting.Count,
                    MessageName: NameOf(declaration));
            }

            var executionId = waiting[0];
            if (declaration.Kind == WorkflowMessageTargetKind.ReceiveTask)
            {
                await flowable.TriggerExecutionAsync(executionId, variables, cancellationToken);
            }
            else
            {
                await flowable.DeliverMessageToExecutionAsync(
                    executionId, declaration.MessageName, variables, cancellationToken);
            }

            return new Result(
                Outcome.Delivered,
                ProcessInstanceId: executionId,
                MatchCount: 1,
                MessageName: NameOf(declaration));
        }

        // Nothing was waiting. A message start event is the one case where that is
        // not an error — it means "start a new one".
        var start = selected.FirstOrDefault(d => d.Kind == WorkflowMessageTargetKind.Start);
        if (start is not null)
        {
            var instanceId = await flowable.StartProcessInstanceByMessageAsync(
                start.MessageName, variables, cancellationToken);
            return new Result(
                Outcome.Started,
                ProcessInstanceId: instanceId,
                MatchCount: 1,
                MessageName: start.MessageName);
        }

        // Reported distinctly, never silently discarded: a discarded message is
        // indistinguishable from the feature being broken.
        return new Result(
            Outcome.NoMatch,
            MessageName: selected.Length == 1 ? NameOf(selected[0]) : messageName);
    }
}
