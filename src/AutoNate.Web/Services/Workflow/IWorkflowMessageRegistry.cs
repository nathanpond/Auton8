namespace AutoNate.Web.Services.Workflow;

// #524. Which (Dapr topic, message name) pairs published workflows currently
// offer as a way IN — a message START event a bus event may trigger. Backed by
// an in-memory snapshot rebuilt from the workflow store, refreshed on publish
// and delete, exactly like IWorkflowSignalRegistry.
//
// A SEPARATE registry, and that is the feature rather than duplication. It is
// what makes "a message and a signal with the same name do not trigger each
// other" true, and true regardless of topic: each dispatcher consults only its
// own registrations, so a bus event named `x` starts a message start named `x`
// and never a signal start named `x`, even where an author has pointed both
// kinds at one topic. A shared registry keyed on a bare name would satisfy every
// other requirement in #524 and silently fail that one.
public interface IWorkflowMessageRegistry
{
    IReadOnlyCollection<string> GetSubscribedTopics();

    IReadOnlySet<string> GetMessageNamesForTopic(string topic);

    IReadOnlyList<WorkflowMessageRegistration> GetRegistrationsForTopic(string topic);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
