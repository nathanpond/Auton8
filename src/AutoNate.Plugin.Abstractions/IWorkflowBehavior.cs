namespace AutoNate.Plugins.Abstractions;

// Predefined named routine that a workflow service task invokes at runtime.
// The studio shows registered behaviors in a dropdown; the BPMN serviceTask
// stores the chosen Key; Flowable invokes the host's bridge delegate, which
// HTTP-POSTs the BehaviorContext to AutoNate.Web, which dispatches to the
// matching IWorkflowBehavior.
//
// Idempotency contract: ExecuteAsync may be invoked more than once for the
// same logical step. Flowable's job-executor retries failed activities, and
// the Flowable activity transaction may roll back after the behavior has
// already committed its own DB writes. Implementations MUST be safe to
// re-run with the same BehaviorContext — return a status branch (e.g.
// "alreadyDone") rather than failing on the second invocation.
public interface IWorkflowBehavior
{
    // Stable, globally-unique identifier persisted in the BPMN XML. Use a
    // namespaced shape ("autonate.unlock-account", "myplugin.send-receipt")
    // so plugin keys never collide with built-ins.
    string Key { get; }

    string DisplayName { get; }

    string? Description { get; }

    // Receives all process variables and execution metadata. Returns variable
    // updates plus an optional predictable failure code/message which the
    // workflow author handles via gateway, not by exception. For unexpected
    // failures (DB down, etc.), throw — the bridge converts that to a
    // FlowableException so the engine retries.
    Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken);
}
