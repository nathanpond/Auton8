using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Services.Workflow;

/// <summary>
/// Fires a named signal into the engine, from outside any instance (#523).
/// </summary>
/// <remarks>
/// <para>
/// Sibling of <see cref="WorkflowMessageCorrelator"/>, and deliberately shaped
/// like it: the outcome is an enum, every outcome is auditable, and a refusal
/// names what was refused. What differs is addressing. A message is addressed to
/// one process by key plus a correlation value; a signal is addressed by NAME to
/// everything listening, which is why this has no process key and why a
/// multi-match is a success rather than the message path's 409.
/// </para>
/// <para>
/// <b>Fan-out is wake-all</b>, matching the bus path that
/// <see cref="WorkflowSignalDispatcher"/> already takes. Refusing an ambiguous
/// broadcast — the message endpoint's rule — would make one signal behave two
/// different ways depending on how it arrived, which is the kind of difference
/// nobody discovers until it matters.
/// </para>
/// </remarks>
public sealed class WorkflowSignalBroadcaster(
    IWorkflowModelStore models,
    IFlowableClient flowable)
{
    public enum Outcome
    {
        Broadcast,
        UnknownSignal
    }

    /// <param name="Declaring">
    /// The process keys whose published diagrams CATCH this name. Not a count of
    /// what woke: the engine decides that, and an instance may be anywhere.
    /// Carried so a refusal can say what is available and a success can say who
    /// was listening at the time.
    /// </param>
    public sealed record Result(
        Outcome Outcome,
        string SignalName,
        IReadOnlyList<string> Declaring,
        IReadOnlyList<string>? Available = null);

    public async Task<Result> BroadcastAsync(
        string signalName,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken = default)
    {
        var published = await models.ListAsync(cancellationToken);

        var declaring = new List<string>();
        var available = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var model in published)
        {
            if (string.IsNullOrWhiteSpace(model.BpmnXml)) continue;

            var caught = WorkflowBpmnXml.ExtractCaughtSignalNames(model.BpmnXml);
            if (caught.Count == 0) continue;

            foreach (var name in caught)
            {
                available.Add(name);
            }

            if (caught.Contains(signalName)
                && !string.IsNullOrWhiteSpace(model.ProcessKey))
            {
                declaring.Add(model.ProcessKey);
            }
        }

        // REFUSED, NOT SILENTLY BROADCAST (#523 AC4). Flowable accepts a
        // broadcast for a name nothing subscribes to and answers 204, so a
        // pass-through endpoint would return success for a signal that reached
        // nobody -- indistinguishable from the feature being broken, which is
        // the shape this repo keeps finding.
        if (declaring.Count == 0)
        {
            return new Result(
                Outcome.UnknownSignal,
                signalName,
                Array.Empty<string>(),
                available.ToList());
        }

        await flowable.BroadcastSignalAsync(signalName, variables, cancellationToken);

        return new Result(Outcome.Broadcast, signalName, declaring);
    }
}
