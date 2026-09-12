using System.Text.RegularExpressions;
using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// What Auton8 says when Flowable refuses something (#334, #339, #344, #349, #350).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing the engine wrote is forwarded.</b> The engine's message can carry a
/// JDBC URL with its password, internal hostnames and container ids, absolute
/// filesystem paths, Java stack frames, and an <c>Extra info</c> tail with
/// process-definition ids — it is the raw HTTP body of a system that can see all
/// of that. Three designs tried to decide what to <em>remove</em> from it and
/// three leaked. This one keeps an identifier and writes its own sentence.
/// </para>
/// <para>
/// The identifier is allowlisted, not merely shaped. #344 echoed any code matching
/// <c>flowable-[a-z0-9-]+</c> on the grounds that the shape was safe. It is not
/// author-proof: a schema-level refusal carries <b>no</b> genuine <c>Problem:</c>
/// marker, and Xerces echoes an invalid attribute value verbatim — so an author
/// could write <c>signalRef="Problem: 'flowable-call-it-support-on-555-0100'"</c>
/// and put their own sentence into a <em>publisher's</em> error banner, 5,092
/// characters of it. Only a code this class already knows is ever repeated (#349).
/// </para>
/// <para>
/// This lives in its own class because it took three rounds to notice that the
/// publish route was not the only one handing back a raw engine body — four
/// routes in <c>ExecutionEndpoints</c> did too, to a much wider audience (#350).
/// A describer that only one file can reach invites exactly that.
/// <c>NoEndpointReturnsARawEngineMessageTests</c> is the mechanical half.
/// </para>
/// </remarks>
internal static class EngineRefusal
{
    /// <summary>The marker Flowable writes around a validation problem code.</summary>
    /// <remarks>
    /// First match, deliberately: the <c>Extra info</c> tail can contain
    /// author-controlled text (an activity name), so a later match may not be the
    /// engine's. Pinned by <c>The_first_marker_wins</c>.
    /// </remarks>
    internal static readonly Regex ProblemCode = new(
        @"Problem:\s*'(?<code>flowable-[a-z0-9-]+)'", RegexOptions.Compiled);

    /// <summary>
    /// Our words for the refusals we have actually captured from a live engine.
    /// </summary>
    /// <remarks>
    /// Every key here was taken from a real refusal, not from Flowable's source or
    /// from memory. #344's table had three keys that were near-miss spellings and
    /// one — <c>flowable-bpmn-parse-failure</c> — that the engine never emits at
    /// all, so a quarter of it could never fire (#349).
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Reasons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Corrected in #349 against captured refusals. The old spellings are
            // named so nobody "fixes" them back.
            ["flowable-process-definition-not-executable"] =            // was flowable-executable-process
                "this workflow is not executable as drawn",
            ["flowable-signal-event-invalid-signal-ref"] =              // was flowable-signal-invalid-signal-ref
                "an event points at a signal this workflow does not declare",
            ["flowable-subprocess-multiple-start-event"] =              // was ...-start-events
                "a subprocess has more than one start event",

            ["flowable-servicetask-missing-implementation"] =
                "a service task has no behaviour chosen",
            ["flowable-multi-instance-missing-collection"] =
                "a repeating step has nothing to repeat over",
            ["flowable-signal-missing-name"] =
                "a signal in this workflow has no name",
            ["flowable-signal-event-missing-signal-ref"] =
                "an event does not say which signal it uses",
            ["flowable-message-event-missing-message-ref"] =
                "an event does not say which message it uses",
            ["flowable-message-event-invalid-message-ref"] =
                "an event points at a message this workflow does not declare",
            ["flowable-signal-duplicate-name"] =
                "two signals in this workflow share a name",
            ["flowable-message-duplicate-name"] =
                "two messages in this workflow share a name",
            ["flowable-sendtask-invalid-implementation"] =
                "a send task has no way to send",
            ["flowable-mailtask-no-recipient"] =
                "a mail task has no recipient",
            ["flowable-mailtask-no-content"] =
                "a mail task has nothing to send",
            ["flowable-eventsubprocess-invalid-start-event-definition"] =
                "an event subprocess starts with something that cannot trigger it",
            ["flowable-signal-invalid-scope"] =
                "a signal declares a scope Flowable does not recognise",
        };

    /// <summary>
    /// Runtime refusals, which carry no problem code at all (#354).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deployment table above is keyed by Flowable's own validation codes.
    /// Runtime refusals have none — they are a plain sentence in an
    /// <c>exception</c> field. Captured from a live engine rather than read out of
    /// Flowable's source:
    /// </para>
    /// <para>
    /// <code>
    ///   400 {"exception":"No process definition found for key 'x'"}
    ///   404 {"exception":"Could not find a task with id 'x'."}
    ///   404 {"exception":"Could not find a process instance with id 'x'."}
    ///   404 {"exception":"Could not find an execution with id 'x'."}
    ///   400 {"exception":"signalName is required"}
    ///   400 {"exception":"Cannot start process instance by message: no
    ///                     subscription to message with name 'x' found."}
    /// </code>
    /// </para>
    /// <para>
    /// <b>Nothing is extracted from these, not even the identifier.</b> Matching a
    /// prefix and pulling out a quoted id would be safe in each case I looked at,
    /// which is precisely the reasoning that leaked three times — and it is
    /// unnecessary here, because the caller already knows which task or instance
    /// they asked about: it is in their own request URL.
    /// </para>
    /// <para>
    /// This exists because #350 cost something real. Sanitising the execution
    /// routes turned
    /// <c>"Variable 'escalate' is already present on execution 'proc-1'"</c> into
    /// "the reason is in the server log", which is worse than what operators had.
    /// #354 is the other half of that work.
    /// </para>
    /// </remarks>
    private static readonly (string Fragment, string Reason)[] RuntimeReasons =
    [
        ("No process definition found for key",
            "no published workflow has that key"),
        ("Could not find a task with id",
            "that task does not exist, or has already been completed"),
        ("Could not find a process instance with id",
            "that workflow run does not exist, or has already finished"),
        ("Could not find an execution with id",
            "that step of the workflow run does not exist, or has already moved on"),
        ("no subscription to message with name",
            "nothing in any published workflow is waiting for that message"),
        ("is already present on execution",
            "that variable is already set on this step"),
        ("signalName is required",
            "the signal was sent without a name"),
        ("Process definition null was not found",
            "a step calls a workflow that is not published"),
    ];

    /// <summary>The code, only if we know it — otherwise nothing (#349).</summary>
    internal static string? KnownCode(string? message)
    {
        var match = ProblemCode.Match(message ?? string.Empty);
        if (!match.Success) return null;

        var code = match.Groups["code"].Value;
        return Reasons.ContainsKey(code) ? code : null;
    }

    /// <summary>
    /// Whether the engine blamed the DIAGRAM rather than itself (#349).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <c>IsCallerError</c>: measured, Flowable answers a validation refusal —
    /// a diagram drawn badly — with HTTP <b>500</b>, so branching on the status
    /// gave authors 502 Bad Gateway for their own mistake.
    /// </para>
    /// <para>
    /// Nor "is there a problem code": a <b>parse</b> failure (truncated XML, a
    /// non-BPMN root, a bad QName) carries no marker at all, and #344 sent that
    /// whole family to 502 with no reason — precisely the defect it claimed to
    /// close. A parse failure is always the diagram's fault.
    /// </para>
    /// </remarks>
    internal static bool IsTheDiagramsFault(string? message)
    {
        if (KnownCode(message) is not null) return true;

        var text = message ?? string.Empty;
        return text.Contains("SAXParseException", StringComparison.Ordinal)
            || text.Contains("XMLStreamException", StringComparison.Ordinal)
            || text.Contains("cvc-", StringComparison.Ordinal)          // Xerces schema codes
            || text.Contains("XMLStreamReader", StringComparison.Ordinal)
            || text.Contains("Validation set:", StringComparison.Ordinal);
    }

    /// <summary>Flowable's refusal, as a sentence of ours.</summary>
    internal static string Describe(FlowableRequestException exception, string what = "this workflow")
    {
        var message = exception.Message ?? string.Empty;

        // A deployment validation code, if the engine named one we know.
        if (KnownCode(message) is { } code)
        {
            return $"The workflow engine refused {what}: {Reasons[code]} ({code}).";
        }

        // A runtime refusal, recognised by its sentence (#354). Nothing from the
        // engine's text travels -- the fragment only selects which of OUR
        // sentences to use.
        foreach (var (fragment, reason) in RuntimeReasons)
        {
            if (message.Contains(fragment, StringComparison.Ordinal))
            {
                return $"The workflow engine refused {what}: {reason}.";
            }
        }

        return $"The workflow engine refused {what}. The reason is in the server log.";
    }
}
