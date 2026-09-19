using System.Text.Json.Serialization;

namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// The engine's jobs — what is scheduled, what is retrying, what is stuck (#172).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IFlowableClient"/> has thirty-odd methods and not one touches jobs.
/// A timer that does not fire, a service task retrying in the background, a step
/// that exhausted its retries and went to the dead-letter queue — none of it was
/// visible anywhere in Auton8. As timer boundary events, retry points and
/// multi-instance land, the number of things that can be silently stuck grows.
/// </para>
/// <para>
/// A separate interface, for the same reason as #106's DMN client: it shares the
/// host and <see cref="FlowableClient.ConfigureHttpClient"/>, and folding a
/// fourth vocabulary into <see cref="IFlowableClient"/> would make "which part of
/// the engine am I talking to" a question a reader answers per method.
/// </para>
/// <para>
/// <b>Read live, not cached (#172, discretion).</b> #104's executions are served
/// from <c>workflow_execution_cache</c>; jobs deliberately are not. The question
/// an operator opens this to answer is "what is stuck <i>right now</i>", and a
/// cache would put a staleness question in front of exactly the reader who cannot
/// tolerate one. Jobs are small and the audience is operators, so the round trip
/// is affordable. The difference from executions is stated rather than left to be
/// discovered.
/// </para>
/// <para>
/// <b>The routes were read out of the shipped engine, not recalled.</b>
/// <c>flowable-rest-8.0.0.jar</c>'s <c>JobResource</c> carries the literals:
/// <c>/management/jobs/{jobId}</c> takes only <c>execute</c>;
/// <c>/management/timer-jobs/{jobId}</c> takes <c>move</c> or <c>reschedule</c>
/// ("Reschedule timer actions must have a valid due date");
/// <c>/management/deadletter-jobs/{jobId}</c> takes <c>move</c> or
/// <c>moveToHistoryJob</c>. Retrying a dead-lettered job is therefore a MOVE
/// back to the executable queue, not an execution in place — which is why
/// <see cref="RetryJobAsync"/> is asserted on the process advancing rather than
/// on the call returning.
/// </para>
/// </remarks>
public interface IFlowableJobClient
{
    /// <summary>Every job attached to one execution, across all four queues.</summary>
    Task<IReadOnlyList<WorkflowJobSummary>> GetJobsForExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Jobs across every execution, dead-lettered first.
    /// </summary>
    /// <param name="deadLetteredOnly">
    /// True by default at the call site that serves the stuck list: "what is
    /// stuck right now" is the question, and a list that also carried every
    /// healthy scheduled timer would bury it.
    /// </param>
    Task<IReadOnlyList<WorkflowJobSummary>> GetJobsAsync(
        bool deadLetteredOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// The exception stack the engine retained for a job, or null when it kept none.
    /// </summary>
    /// <remarks>
    /// Separate from the summary because it is large and only wanted on demand —
    /// and because a null here is a real answer: a job can be dead-lettered with
    /// a message and no stack.
    /// </remarks>
    Task<string?> GetJobExceptionStackAsync(
        string jobId, WorkflowJobQueue queue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a failed or dead-lettered job back in front of the engine.
    /// </summary>
    /// <remarks>
    /// For a dead-lettered job this is a <c>move</c>: the engine returns it to the
    /// executable queue and the async executor picks it up. So the call returning
    /// means "it is queued again", NOT "the step succeeded" — a caller that
    /// reports success from this alone is reporting something it does not know.
    /// </remarks>
    Task RetryJobAsync(
        string jobId, WorkflowJobQueue queue, CancellationToken cancellationToken = default);

    /// <summary>Changes when a timer job will fire.</summary>
    Task RescheduleTimerJobAsync(
        string jobId, DateTimeOffset dueAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Which of the engine's four job collections a job is sitting in.</summary>
/// <remarks>
/// Not cosmetic: the queue decides both the route that acts on a job and what an
/// action means there. Executable jobs take <c>execute</c>; timer jobs take
/// <c>move</c> or <c>reschedule</c>; dead-lettered jobs take <c>move</c>. A single
/// "retry" that guessed would be right two times in three.
///
/// Serialized by NAME, following <c>SystemHealthService</c>'s enums. Without it
/// the list answers <c>"queue": 2</c> while the retry endpoint takes
/// <c>"deadletter"</c> -- the read and the write speaking different languages
/// about the same concept, which no caller can be expected to bridge and which
/// nothing on the server would have failed on.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowJobQueue
{
    /// <summary>Waiting for the async executor, or retrying.</summary>
    Executable,

    /// <summary>Scheduled for a due date that has not arrived.</summary>
    Timer,

    /// <summary>Out of retries. Nothing will run it until someone moves it back.</summary>
    DeadLetter,

    /// <summary>Its process is suspended.</summary>
    Suspended
}

/// <summary>One job, as an operator needs to see it.</summary>
public sealed record WorkflowJobSummary
{
    public required string Id { get; init; }

    public required WorkflowJobQueue Queue { get; init; }

    public string? ProcessInstanceId { get; init; }

    public string? ProcessDefinitionId { get; init; }

    /// <summary>The BPMN element this job belongs to — the step an operator recognises.</summary>
    public string? ElementId { get; init; }

    public string? ElementName { get; init; }

    /// <summary>
    /// How many attempts remain. Zero on a dead-lettered job, which is what put
    /// it there.
    /// </summary>
    public int Retries { get; init; }

    public string? ExceptionMessage { get; init; }

    /// <summary>When a timer will fire, or when a retrying job is next due.</summary>
    public DateTimeOffset? DueAtUtc { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }
}
