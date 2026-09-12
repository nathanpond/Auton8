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
        var code = KnownCode(exception.Message);

        if (code is not null)
        {
            return $"The workflow engine refused {what}: {Reasons[code]} ({code}).";
        }

        return $"The workflow engine refused {what}. The reason is in the server log.";
    }
}
