using System.Net;
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
    /// <remarks>
    /// Anchored on the full <c>[Validation set: … | Problem: …]</c> envelope
    /// (#357). Matching a bare <c>Problem: '…'</c> let a diagram author supply one:
    /// a schema refusal carries no genuine marker, and Xerces echoes an invalid
    /// attribute value verbatim — so <c>signalRef="Problem: 'flowable-mailtask-no-recipient'"</c>
    /// made a <b>publisher</b> read "a mail task has no recipient" about a diagram
    /// with no mail task.
    /// </remarks>
    internal static readonly Regex ProblemCode = new(
        @"\[Validation set:\s*'[a-z0-9-]+'\s*\|\s*Problem:\s*'(?<code>flowable-[a-z0-9-]+)'\s*\]",
        RegexOptions.Compiled);

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
    /// Runtime refusals, keyed on what AUTON8 controls (#354, #357).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #354 keyed these on a fragment of the engine's sentence. That was wrong for
    /// a reason the text-leak work had already taught and I did not carry over:
    /// <b>a caller can reach the engine's sentence.</b> Variable names are
    /// caller-supplied and Flowable echoes them into its 409, so naming a variable
    /// <c>Could not find a task with id</c> made the product say "that task does
    /// not exist" about a variable conflict. Five such sentences were confirmed
    /// against the real body.
    /// </para>
    /// <para>
    /// The allowlist in #349 bounded the character set of what travels. It did not
    /// bound <em>who decides what is said</em>, and I treated the second as
    /// following from the first.
    /// </para>
    /// <para>
    /// So the key is now <c>(Operation, StatusCode)</c>. <c>Operation</c> is a
    /// literal this codebase passes to <c>EnsureSuccessAsync</c> — "create the
    /// process variables", "complete the user task" — and the status is the
    /// engine's own classification. Neither is reachable by a caller, and the
    /// engine's sentence is not read at all.
    /// </para>
    /// <para>
    /// Coarser than a fragment, deliberately. Where one pair covers two real
    /// causes the sentence says what they have in common; where it would be
    /// misleading there is no row, and the caller gets the generic plus a log
    /// line. A vague true answer beats a precise false one.
    /// </para>
    /// </remarks>
    private static readonly (string Operation, HttpStatusCode Status, string Reason)[] RuntimeReasons =
    [
        // #372: the six pairs the routes can actually produce that had no row.
        ("update the process variables", HttpStatusCode.Conflict,
            "one of those variables is already set on this step"),
        ("update the process variables", HttpStatusCode.BadRequest,
            "one of those values is not a type the engine can store"),
        ("create the process variables", HttpStatusCode.BadRequest,
            "one of those values is not a type the engine can store"),
        ("start the ad-hoc activity", HttpStatusCode.NotFound,
            "that step is not available in this section right now"),
        ("start the ad-hoc activity", HttpStatusCode.Conflict,
            "that step is already running in this section"),
        ("start the ad-hoc activity", HttpStatusCode.BadRequest,
            "that step cannot be started in this section"),

        ("create the process variables", HttpStatusCode.Conflict,
            "one of those variables is already set on this step"),
        ("create the process variables", HttpStatusCode.NotFound,
            "that step of the workflow run no longer exists"),
        ("update the process variables", HttpStatusCode.NotFound,
            "that step of the workflow run no longer exists"),
        ("fetch process instance variables", HttpStatusCode.NotFound,
            "that step of the workflow run no longer exists"),

        ("start the process instance", HttpStatusCode.BadRequest,
            "no published workflow matches that key or message"),
        ("start the process instance", HttpStatusCode.NotFound,
            "no published workflow matches that key"),

        ("complete the user task", HttpStatusCode.NotFound,
            "that task does not exist, or has already been completed"),
        ("complete the user task", HttpStatusCode.Conflict,
            "that task was changed by someone else while you were working on it"),
        ("fetch runtime task", HttpStatusCode.NotFound,
            "that task does not exist, or has already been completed"),
        ("reassign the user task", HttpStatusCode.NotFound,
            "that task does not exist, or has already been completed"),
        ("update the user task due date", HttpStatusCode.NotFound,
            "that task does not exist, or has already been completed"),

        ("query the process instance", HttpStatusCode.NotFound,
            "that workflow run does not exist, or has already finished"),
        // 409 is the one an operator actually hits, and it was missing until #362.
        // Flowable says "running child executions"; this says the same thing in
        // words an operator can act on, which is what #163's AC7 asks for -- a
        // refusal "handled in a defined, documented way", not the engine's wording
        // specifically.
        ("complete the ad-hoc sub-process", HttpStatusCode.Conflict,
            "that section still has work in progress — finish or cancel it first"),
        ("complete the ad-hoc sub-process", HttpStatusCode.NotFound,
            "that step of the workflow run no longer exists"),
        // NOTE (#372): `list the ad-hoc subprocess activities` had a row here and
        // that route has no try/catch, so it could never fire. Removed rather than
        // left as decoration -- #349 found the same shape in the deployment table.
    ];

    /// <summary>The code, only if we know it — otherwise nothing (#349).</summary>    /// <summary>
    /// A refusal the XML parser produced, rather than the validator (#363).
    /// </summary>
    /// <remarks>
    /// This is the structural fact the whole anti-planting argument rests on.
    /// <b>A parse failure is the only way an author's own text is echoed back</b> —
    /// Xerces quotes the offending attribute value verbatim. A validator refusal
    /// never quotes the author; it emits its own envelope.
    /// </remarks>
    private static readonly Regex ParserRefusal = new(
        @"cvc-|XMLStreamException|SAXParseException|TransformerException|ParseError",
        RegexOptions.Compiled);

    /// <summary>The code, only if we know it and only if the engine wrote it (#349, #363).</summary>
    /// <remarks>
    /// <para>
    /// Two rounds tried to stop an author planting a code by constraining its
    /// <em>shape</em> (#349) and then its <em>surroundings</em> (#357). Both took
    /// the previous counterexample as the specification, and both lost: the author
    /// controls the entire attribute value, so any marker they can type they can
    /// plant — 7 of 12 payloads still landed after #357.
    /// </para>
    /// <para>
    /// The property being defended is <b>"a caller cannot influence what Auton8
    /// asserts about someone else's diagram"</b>. Stated that way the fix is not a
    /// better pattern, it is a different question: <em>could the author's text be
    /// in this message at all?</em> It can only be there if the parser echoed it,
    /// so a parse refusal yields no code — ever, regardless of what it contains.
    /// </para>
    /// <para>
    /// And within a validation refusal, the code is taken from before the
    /// <c>[Extra info</c> tail, which is where author-controlled element names and
    /// ids appear. First match, before the tail, non-parse: the author has no
    /// reachable position left.
    /// </para>
    /// <para>
    /// The cost: a parse failure now always reads generically. That is the right
    /// side of the trade — the parse-error family is the one whose text is most
    /// likely to be author-supplied, and its problem codes were mostly invented
    /// anyway (#349 found <c>flowable-bpmn-parse-failure</c> is never emitted).
    /// </para>
    /// </remarks>
    internal static string? KnownCode(string? message)
    {
        var text = message ?? string.Empty;

        // Bound FIRST. #371: `ParserRefusal` scanned the whole message, including
        // the `[Extra info` tail where Flowable puts the author's activityName --
        // so an activity called "cvc-check step" or "Handle ParseError" turned a
        // genuine, correctly-coded validation refusal into "the reason is in the
        // server log". The two protections disagreed about where the tail was.
        var tail = text.IndexOf("[Extra info", StringComparison.OrdinalIgnoreCase);
        var beforeTail = tail > 0 ? text[..tail] : text;

        // The parser echoed something, so nothing here is trustworthy as ours.
        if (ParserRefusal.IsMatch(beforeTail)) return null;

        var match = ProblemCode.Match(beforeTail);
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
    /// <summary>The one operation whose message may be read for a problem code.</summary>
    /// <remarks>
    /// A deployment validation envelope is the ONLY thing worth parsing out of an
    /// engine message, and it can only appear on a deploy. Consulting the
    /// deployment table on a runtime refusal is what made #371 possible.
    /// </remarks>
    internal const string DeployOperation = "deploy the BPMN workflow";

    internal static string Describe(FlowableRequestException exception, string what = "this workflow")
    {
        // (Operation, Status) FIRST -- both ours, neither caller-reachable.
        //
        // #371: this loop used to run SECOND, after `KnownCode(message)`. The
        // message contains the operation (`EnsureSuccessAsync` interpolates it),
        // and 13 operations interpolate caller data -- a route segment, a variable
        // name, a message name. So a caller could put a validation envelope in a
        // URL path and select one of our sentences, with no parser and no diagram
        // involved. The comment here read "neither reachable by a caller", which
        // was true of the KEY and false of the check that ran before it.
        foreach (var (operation, status, reason) in RuntimeReasons)
        {
            if (string.Equals(exception.Operation, operation, StringComparison.Ordinal)
                && exception.StatusCode == status)
            {
                return $"The workflow engine refused {what}: {reason}.";
            }
        }

        // And the message is parsed for a code ONLY on a deploy. Nothing else can
        // carry a validation envelope, so nothing else has a reason to look.
        if (string.Equals(exception.Operation, DeployOperation, StringComparison.Ordinal)
            && KnownCode(exception.Message) is { } code)
        {
            return $"The workflow engine refused {what}: {Reasons[code]} ({code}).";
        }

        return $"The workflow engine refused {what}. The reason is in the server log.";
    }
}
