namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// The one definition of a workflow execution's status string (#576).
/// </summary>
/// <remarks>
/// <para>
/// This used to be a private static in <c>FlowableExecutionProjection</c>, which
/// was fine while the projection was the only thing that needed it. It stopped
/// being fine when the in-memory authorization path started supplying
/// <c>status</c> as a selector fact: the projection writes the NORMALIZED string
/// into <c>workflow_execution_cache.status</c>, so a fact builder that passed
/// Flowable's raw value through would make <c>[status=running]</c> match in
/// memory and not in SQL — the exact defect #576 exists to close, reintroduced
/// one layer up.
/// </para>
/// <para>
/// So it lives here with three callers and no second copy. Adding a status
/// means editing one switch.
/// </para>
/// </remarks>
public static class WorkflowExecutionStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Terminated = "terminated";
    public const string Suspended = "suspended";

    /// <summary>
    /// Maps Flowable's status vocabulary onto the one the cache column and the
    /// selector facts both use. An unrecognised value passes through lowercased
    /// rather than being forced to a default: guessing "active" for a status
    /// nobody anticipated would grant on a state we do not understand.
    /// </summary>
    /// <summary>Has this run finished, one way or another (#634)?</summary>
    /// <remarks>
    /// A terminal run is not a deleted one. Flowable's RUNTIME endpoint returns
    /// 404 for every completed instance -- measured -- so a read-through that
    /// reads "not running" as "deleted" removes finished runs from the cache and
    /// then refuses their gates.
    /// </remarks>
    public static bool IsTerminal(string? status) => Normalize(status) switch
    {
        Completed or Cancelled or Terminated => true,
        _ => false
    };

    public static string Normalize(string? raw) => (raw?.ToLowerInvariant()) switch
    {
        null or "" => Active,
        "running" => Active,
        "complete" or "completed" => Completed,
        "cancelled" or "canceled" => Cancelled,
        "terminated" => Terminated,
        "suspended" => Suspended,
        var other => other
    };
}
