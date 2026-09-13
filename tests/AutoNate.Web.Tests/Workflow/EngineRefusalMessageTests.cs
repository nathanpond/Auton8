using System.Net;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Flowable;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// What an author is told when the engine refuses a deployment (#334, #339, #344).
/// </summary>
/// <remarks>
/// <para>
/// Two designs failed before this one, and both failures were in the tests as
/// much as the code:
/// </para>
/// <list type="number">
/// <item>#334 truncated on real control characters while the caller supplies
/// JSON, where the escapes are two characters. The tests hand-wrote unescaped
/// strings, so they asserted against a shape no caller produces.</item>
/// <item>#339 extracted bounded prose and discarded it if it "looked like"
/// internals. 22 of 32 adversarial payloads walked past that check. The tests
/// covered the shapes I had imagined.</item>
/// </list>
/// <para>
/// So the rule now forwards <b>nothing</b> the engine wrote, and the payload
/// table below is the one that beat the previous version — kept as rows rather
/// than deleted, because the design changed specifically to defeat them.
/// </para>
/// </remarks>
public sealed class EngineRefusalMessageTests
{
    /// <summary>
    /// Built the way <c>FlowableClient.EnsureSuccessAsync</c> builds it:
    /// <c>$"Flowable could not {operation}. HTTP {code} {reason}. {rawResponseBody}"</c>.
    /// </summary>
    private static FlowableRequestException AsTheClientBuildsIt(string rawResponseBody) =>
        // The operation is the REAL literal the client passes -- "deploy process"
        // was close enough while nothing keyed on it, and stopped being so when
        // #371 made the deploy operation the gate for reading a problem code.
        new(HttpStatusCode.InternalServerError, EngineRefusal.DeployOperation,
            $"Flowable could not {EngineRefusal.DeployOperation}. HTTP 500 Internal Server Error. {rawResponseBody}");

    /// <summary>Everything that defeated the #339 post-condition (#344).</summary>
    /// <remarks>
    /// Each row is prefixed with the genuine marker, so the extraction path is
    /// the real one. None of this text may appear in the output — not redacted,
    /// not truncated: absent, because prose is never forwarded.
    /// </remarks>
    [Theory]
    // secrets and infrastructure
    [InlineData("Could not acquire a connection: jdbc:postgresql://flowable-db.internal:5432/flowabledb?user=flowable&password=Hunter2!", "Hunter2")]
    [InlineData("Authentication failed for user 'flowable_admin' with password 'S3cr3t!'", "S3cr3t")]
    [InlineData("FLOWABLE_DATASOURCE_PASSWORD=hunter2 was rejected by the pool", "hunter2")]
    [InlineData("Could not reach 10.0.3.17:5432 from container flowable-rest-7d9c", "10.0.3.17")]
    [InlineData("Deployment rejected by engine node flowable-node-3.prod.internal:8080", "prod.internal")]
    // paths the old regex could not see
    [InlineData("Failed reading /My Documents/My Projects/process definition", "My Documents")]
    [InlineData(@"Failed reading \\file server\shared docs\keystore", "shared docs")]
    [InlineData("Failed reading c:/temp", "c:/temp")]
    [InlineData("Could not read /secretstore.pem", "secretstore")]
    [InlineData("Could not read %2Fopt%2Fflowable%2Fsecrets%2Fkeystore.p12", "keystore")]
    [InlineData("Could not read \u2215Users\u2215npond\u2215.ssh\u2215id_rsa", "id_rsa")]
    // the Extra-info tail, in all three spellings
    [InlineData("Service task has no implementation -[Extra info : processDefinitionId = secretProc:3:9f2c | id = st7 ]", "secretProc")]
    [InlineData("Service task has no implementation -  [Extra info : processDefinitionId = secretProc:3:9f2c ]", "secretProc")]
    [InlineData("Service task has no implementation [Extra info : processDefinitionId = secretProc:3:9f2c ]", "secretProc")]
    // source files, FQCNs, ids
    [InlineData("Script1.groovy: 12: unexpected token in /srv", "groovy")]
    [InlineData("Could not load flowable-secrets.properties from the classpath", "secrets")]
    [InlineData("org.flowable.common.engine.api.FlowableObjectNotFoundException: no deployed process with key 'internal-secret-key'", "internal-secret-key")]
    [InlineData("Caused by: java.lang.OutOfMemoryError: Java heap space", "OutOfMemoryError")]
    [InlineData("Process definition secretProc:3:9f2c1a44-8b1e-11ef-9a1b-0242ac120002 is gone", "9f2c1a44")]
    public void Nothing_the_engine_wrote_is_ever_forwarded(string body, string mustNotAppear)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            $"[Validation set: 'flowable-executable-process' | Problem: 'flowable-bpmn-parse-failure'] : {body}"));

        Assert.DoesNotContain(mustNotAppear, described, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The complement: a mapped code still produces a useful sentence (#344).
    /// </summary>
    /// <remarks>
    /// A rule that emits a constant would pass every row above while making the
    /// feature worthless. This is what stops that.
    /// </remarks>
    [Theory]
    [InlineData("flowable-servicetask-missing-implementation", "no behaviour chosen")]
    [InlineData("flowable-multi-instance-missing-collection", "nothing to repeat over")]
    [InlineData("flowable-signal-missing-name", "has no name")]
    [InlineData("flowable-signal-duplicate-name", "share a name")]
    [InlineData("flowable-mailtask-no-recipient", "no recipient")]
    public void A_known_refusal_is_explained_in_our_own_words(string code, string expected)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            $"[Validation set: 'flowable-executable-process' | Problem: '{code}'] : "
            + "some engine prose that must not travel"));

        Assert.Contains(expected, described, StringComparison.Ordinal);
        Assert.Contains(code, described, StringComparison.Ordinal);
        Assert.DoesNotContain("must not travel", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unmapped code is NOT echoed — the shape was never enough (#349).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #344 echoed any code matching the shape, reasoning that
    /// <c>[a-z0-9-]</c> cannot express a path or a credential. True, and beside
    /// the point: the marker itself is author-reachable, so the shape constrains
    /// the <em>characters</em> an attacker picks, not <em>whether</em> they pick
    /// them. <c>flowable-your-account-is-suspended-email-attacker-example-com</c>
    /// satisfies it perfectly.
    /// </para>
    /// <para>
    /// So the allowlist is the table, not the regex. The cost is that a genuinely
    /// new engine code reads generically until someone adds it — which the server
    /// log makes recoverable, and which is the right side of this trade.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_unmapped_code_is_not_echoed()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'x' | Problem: 'flowable-something-new'] : /Users/npond/secret.pem"));

        Assert.DoesNotContain("flowable-something-new", described, StringComparison.Ordinal);
        Assert.Contains("server log", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_with_no_code_at_all_says_so()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(
            AsTheClientBuildsIt("something entirely unstructured with /Users/npond/secret in it"));

        Assert.Contains("server log", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A code cannot be smuggled from the tail into something dangerous (#344).
    /// </summary>
    /// <remarks>
    /// #339 matched the code against the whole message, so the Extra-info tail
    /// could supply one. That still holds here — and it no longer matters, because
    /// the code's own shape admits nothing but <c>[a-z0-9-]</c>. This row pins
    /// that the shape is what makes it safe, not where it was found.
    /// </remarks>
    [Theory]
    [InlineData("Problem: '/Users/npond/secrets/keys.java:31'")]
    [InlineData("Problem: 'jdbc:postgresql://host/db?password=hunter2'")]
    [InlineData("Problem: 'flowable-ok' | Problem: '/etc/shadow'")]
    public void A_path_cannot_pose_as_a_problem_code(string problem)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            $"[Validation set: 'x' | {problem}] : reason"));

        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/etc/", described, StringComparison.Ordinal);
        Assert.DoesNotContain("password", described, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// An author cannot inject a problem code (#349).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #344 echoed any code matching <c>flowable-[a-z0-9-]+</c> on the grounds
    /// that the shape was safe. It is not author-proof: a schema-level refusal
    /// carries <b>no</b> genuine <c>Problem:</c> marker, and Xerces echoes an
    /// invalid attribute value verbatim. So a diagram author could write
    /// </para>
    /// <para>
    /// <code>signalRef="Problem: 'flowable-call-it-support-on-555-0100-to-unlock'"</code>
    /// </para>
    /// <para>
    /// and put their own sentence into a <em>publisher's</em> error banner — 5,092
    /// characters of it in the reported case. Only a code the table already knows
    /// is repeated now.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("flowable-call-it-support-on-555-0100-to-unlock")]
    [InlineData("flowable-the-db-password-is-hunter2-and-the-host-is-prod-db-internal")]
    [InlineData("flowable-your-account-is-suspended-email-attacker-example-com")]
    public void An_author_invented_code_is_not_echoed(string invented)
    {
        // The real shape: Xerces reporting an invalid QName, which contains no
        // genuine marker of its own.
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            $"javax.xml.stream.XMLStreamException: cvc-datatype-valid.1.2.1: "
            + $"'Problem: '{invented}'' is not a valid value for 'QName'."));

        Assert.DoesNotContain(invented, described, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0100", described, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", described, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A parse failure is the diagram's fault, not the engine's (#349).
    /// </summary>
    /// <remarks>
    /// #344 keyed the status on "did the engine name a problem code". A parse
    /// failure — truncated XML, a non-BPMN root, a bad QName — carries no marker,
    /// so the whole family returned <b>502 Bad Gateway</b> for a diagram the author
    /// drew: precisely the defect #344's own comment claims to have closed.
    /// </remarks>
    [Theory]
    [InlineData("javax.xml.stream.XMLStreamException: ParseError at [row,col]:[5,163]")]
    [InlineData("org.xml.sax.SAXParseException; lineNumber: 5; columnNumber: 163")]
    [InlineData("cvc-datatype-valid.1.2.1: 'x' is not a valid value for 'QName'.")]
    [InlineData("[Validation set: 'flowable-executable-process' | Problem: 'flowable-unknown-code'] : x")]
    public void A_parse_failure_is_the_authors_fault(string body)
    {
        Assert.True(
            AutoNate.Web.Endpoints.EngineRefusal.IsTheDiagramsFault(
                $"Flowable could not deploy process. HTTP 500 Internal Server Error. {body}"),
            "a parse failure was attributed to the engine, so the author gets 502 for their own diagram");
    }

    [Fact]
    public void A_transport_failure_is_not_the_authors_fault()
    {
        // The complement: something that is genuinely the engine's problem must
        // NOT be reported as a bad diagram.
        Assert.False(
            AutoNate.Web.Endpoints.EngineRefusal.IsTheDiagramsFault(
                "Flowable could not deploy process. HTTP 500 Internal Server Error. "
                + "Could not acquire a connection from the pool"));
    }

    /// <summary>The FIRST marker wins (#349).</summary>
    /// <remarks>
    /// The <c>Extra info</c> tail can carry author-controlled text such as an
    /// activity name, so a later match may not be the engine's. Taking the last
    /// match instead of the first left 56/56 green.
    /// </remarks>
    [Fact]
    public void The_first_marker_wins()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'x' | Problem: 'flowable-servicetask-missing-implementation'] : reason "
            + "- [Extra info : activityName = Problem: 'flowable-mailtask-no-recipient' ]"));

        Assert.Contains("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.DoesNotContain("no recipient", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runtime refusals get our words, keyed on what Auton8 controls (#354, #357).
    /// </summary>
    /// <remarks>
    /// The key is <c>(Operation, StatusCode)</c> — a literal this codebase passes
    /// to <c>EnsureSuccessAsync</c>, and the engine's own classification. The
    /// engine's sentence is not read, so the body below is deliberately hostile
    /// and irrelevant to the outcome.
    /// </remarks>
    [Theory]
    [InlineData("create the process variables", HttpStatusCode.Conflict, "already set on this step")]
    [InlineData("create the process variables", HttpStatusCode.NotFound, "no longer exists")]
    [InlineData("complete the user task", HttpStatusCode.NotFound, "already been completed")]
    [InlineData("start the process instance", HttpStatusCode.BadRequest, "no published workflow matches")]
    [InlineData("query the process instance", HttpStatusCode.NotFound, "already finished")]
    public void A_runtime_refusal_is_explained_in_our_own_words(
        string operation, HttpStatusCode status, string expected)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(new FlowableRequestException(
            status, operation,
            $"Flowable could not {operation}. HTTP {(int)status}. "
            + "{\"exception\":\"anything at all, including /Users/npond/secret and hunter2\"}"));

        Assert.Contains(expected, described, StringComparison.Ordinal);
        Assert.DoesNotContain("server log", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", described, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A caller cannot choose which sentence Auton8 says (#357).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the five that worked. Variable names are caller-supplied and
    /// Flowable echoes them into its 409, and the old fragment table was
    /// first-match-wins — so naming a variable after another row's fragment made
    /// the product confidently say the wrong thing about a variable conflict.
    /// </para>
    /// <para>
    /// #349 bounded the character set of what travels. It did not bound who
    /// decides what is said, and the two are not the same property.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Could not find a task with id")]
    [InlineData("No process definition found for key")]
    [InlineData("Could not find a process instance with id")]
    [InlineData("Could not find an execution with id")]
    [InlineData("no subscription to message with name")]
    public void A_caller_cannot_pick_the_sentence_by_naming_a_variable(string plantedName)
    {
        // The real 409 shape, with the caller's variable name echoed by the engine.
        var described = WorkflowEndpoints.DescribeEngineRefusal(new FlowableRequestException(
            HttpStatusCode.Conflict, "create the process variables",
            "Flowable could not create the process variables. HTTP 409 Conflict. "
            + $"{{\"exception\":\"Variable '{plantedName}' is already present on execution 'proc-1'.\"}}"));

        // The TRUE answer, every time.
        Assert.Contains("already set on this step", described, StringComparison.Ordinal);
        Assert.DoesNotContain(plantedName, described, StringComparison.Ordinal);
    }

    /// <summary>
    /// An author cannot influence what Auton8 asserts about their diagram (#357, #363).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written from the property, not from a counterexample.</b> Two rounds
    /// failed here by doing the opposite: #349 constrained the code's shape
    /// because a free-text leak was the last counterexample; #357 constrained the
    /// marker's surroundings because a bare marker was the last counterexample.
    /// Seven of twelve payloads still landed.
    /// </para>
    /// <para>
    /// The property is: <i>a caller cannot influence what Auton8 asserts about
    /// someone else's diagram.</i> The author's text reaches the message only when
    /// the PARSER echoes it, so the rule is structural — a parse refusal yields no
    /// code at all, whatever it contains.
    /// </para>
    /// <para>
    /// Every row below is a payload that defeated #357. They are kept as rows
    /// rather than deleted, because the design changed specifically to defeat them
    /// and the next person to touch this needs to see what it is holding back.
    /// </para>
    /// </remarks>
    [Theory]
    // the whole envelope planted, which is what beat #357
    [InlineData("javax.xml.stream.XMLStreamException: cvc-datatype-valid.1.2.1: '[Validation set: 'x' | Problem: 'flowable-mailtask-no-recipient']' is not a valid value for 'QName'.")]
    // no-quote cvc-attribute form
    [InlineData("cvc-attribute.3: The value [Validation set: 'x' | Problem: 'flowable-mailtask-no-recipient'] of attribute 'signalRef' is not valid.")]
    // newline-separated parts
    [InlineData("org.xml.sax.SAXParseException: bad value\n[Validation set: 'x' | Problem: 'flowable-signal-duplicate-name']\n")]
    // tab-separated parts
    [InlineData("javax.xml.stream.XMLStreamException:\t[Validation set: 'x'\t| Problem: 'flowable-servicetask-missing-implementation']")]
    // no spaces at all
    [InlineData("cvc-datatype-valid.1.2.1: '[Validation set:'x'|Problem:'flowable-mailtask-no-recipient']' is not valid.")]
    // planted via a documentation parse error
    [InlineData("javax.xml.transform.TransformerException: [Validation set: 'x' | Problem: 'flowable-signal-missing-name'] in documentation")]
    // planted in an Extra info tail with no genuine envelope
    [InlineData("Deployment failed - [Extra info : activityName = [Validation set: 'x' | Problem: 'flowable-mailtask-no-recipient'] ]")]
    public void An_author_cannot_influence_what_auton8_asserts(string planted)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(planted));

        // Not "does not contain the planted code" -- does not contain ANY of our
        // sentences. The property is that the author selected nothing.
        Assert.Contains("The reason is in the server log", described, StringComparison.Ordinal);
        Assert.DoesNotContain("no recipient", described, StringComparison.Ordinal);
        Assert.DoesNotContain("share a name", described, StringComparison.Ordinal);
        Assert.DoesNotContain("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.DoesNotContain("has no name", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A genuine envelope beats a planted one in the same message (#363).
    /// </summary>
    /// <remarks>
    /// The `[Extra info` tail carries author-controlled element names, so a real
    /// validation refusal can contain both. The code is taken from before the
    /// tail, so the engine's own answer wins.
    /// </remarks>
    [Fact]
    public void A_planted_code_in_the_tail_does_not_displace_the_real_one()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task has no implementation "
            + "- [Extra info : activityName = Problem: 'flowable-mailtask-no-recipient' ]"));

        Assert.Contains("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.DoesNotContain("no recipient", described, StringComparison.Ordinal);
    }

    /// <summary>And a genuine envelope still works (#357).</summary>    /// <summary>And a genuine envelope still works (#357).</summary>
    /// <remarks>
    /// Without this, anchoring the marker could have disabled the whole deployment
    /// table and every row above would still pass.
    /// </remarks>
    [Fact]
    public void A_real_validation_envelope_is_still_recognised()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task has no implementation"));

        Assert.Contains("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.Contains("flowable-servicetask-missing-implementation", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// No channel by which caller data reaches the message can select a sentence (#371).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Round 14 reasoned that "the author's text can only be in the message if the
    /// parser echoed it". True of the <b>diagram</b>; false of the <b>request</b>.
    /// <c>EnsureSuccessAsync</c> interpolates the operation into the message, and
    /// 13 operations interpolate caller data — a URL route segment, a variable
    /// name, a message name. A caller could put a validation envelope in a URL
    /// path and pick one of our sentences, with no XML anywhere.
    /// </para>
    /// <para>
    /// So this theory is the <b>channel inventory</b>, not a payload list: one row
    /// per way caller-supplied text reaches a <c>FlowableRequestException</c>. That
    /// is the thing I failed to enumerate, and enumerating it is the test.
    /// </para>
    /// </remarks>
    [Theory]
    // a URL route segment (the one that worked)
    [InlineData("start the ad-hoc activity", HttpStatusCode.NotFound,
        "Flowable could not start the ad-hoc activity '[Validation set: 'x' | Problem: 'flowable-mailtask-no-recipient']'. HTTP 404 Not Found.")]
    // a caller-named variable, echoed by the engine in a real 409
    [InlineData("create the process variables", HttpStatusCode.Conflict,
        "Flowable could not create the process variables. HTTP 409 Conflict. {\"exception\":\"Variable '[Validation set: 'x' | Problem: 'flowable-signal-duplicate-name']' is already present.\"}")]
    // a caller-supplied message name, echoed in a real 400
    [InlineData("start the process instance", HttpStatusCode.BadRequest,
        "Flowable could not start the process instance. HTTP 400 Bad Request. {\"exception\":\"no subscription to message with name '[Validation set: 'x' | Problem: 'flowable-servicetask-missing-implementation']' found.\"}")]
    // an operation that is not in the table at all, carrying a planted envelope
    [InlineData("broadcast signal", HttpStatusCode.InternalServerError,
        "Flowable could not broadcast signal '[Validation set: 'x' | Problem: 'flowable-mailtask-no-recipient']'. HTTP 500.")]
    public void No_caller_channel_can_select_a_sentence(
        string operation, HttpStatusCode status, string message)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(
            new FlowableRequestException(status, operation, message));

        // None of the planted sentences. The row may legitimately return OUR
        // sentence for that (operation, status) -- what it must never do is return
        // the one the caller asked for.
        Assert.DoesNotContain("no recipient", described, StringComparison.Ordinal);
        Assert.DoesNotContain("share a name", described, StringComparison.Ordinal);
        Assert.DoesNotContain("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.DoesNotContain("[Validation set", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only a deploy may have its message read for a code (#371).
    /// </summary>
    /// <remarks>
    /// The structural rule that closes every channel at once: a deployment
    /// validation envelope can only appear on a deploy, so nothing else looks.
    /// </remarks>
    [Fact]
    public void A_runtime_refusal_never_consults_the_deployment_table()
    {
        // A genuine, perfectly-formed envelope -- on a runtime operation.
        var described = WorkflowEndpoints.DescribeEngineRefusal(new FlowableRequestException(
            HttpStatusCode.InternalServerError, "complete the user task",
            "Flowable could not complete the user task. HTTP 500. "
            + "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task has no implementation"));

        Assert.DoesNotContain("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.Contains("server log", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// An author's element name cannot suppress a genuine code (#371).
    /// </summary>
    /// <remarks>
    /// `ParserRefusal` scanned the whole message including the `[Extra info` tail,
    /// where Flowable puts the author's `activityName`. So naming an activity
    /// "cvc-check step" turned a real, correctly-coded refusal into "the reason is
    /// in the server log" — the fix suppressing the very thing it exists to show.
    /// </remarks>
    [Theory]
    [InlineData("cvc-check step")]
    [InlineData("Handle ParseError")]
    [InlineData("SAXParseException triage")]
    public void An_element_name_in_the_tail_cannot_suppress_a_genuine_code(string activityName)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task has no implementation "
            + $"- [Extra info : activityName = {activityName} ]"));

        Assert.Contains("no behaviour chosen", described, StringComparison.Ordinal);
        Assert.DoesNotContain("server log", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ad-hoc rows, asserted where CI can run them (#372).
    /// </summary>
    /// <remarks>
    /// #362 added the 409 row and its only assertion was in
    /// <c>AdhocSubProcessExecutionTests</c>, which carries
    /// <c>RequiresService=Flowable</c> — the suite CI excludes. Deleting the row
    /// left 781/781 green. A fix for a regression CI could not see, guarded only
    /// by a test CI cannot run.
    /// <c>EngineRefusal.Describe</c> is a pure function; there was never a reason
    /// for these to need an engine.
    /// </remarks>
    [Theory]
    [InlineData("complete the ad-hoc sub-process", HttpStatusCode.Conflict, "work in progress")]
    [InlineData("complete the ad-hoc sub-process", HttpStatusCode.NotFound, "no longer exists")]
    [InlineData("start the ad-hoc activity", HttpStatusCode.NotFound, "not available in this section")]
    [InlineData("start the ad-hoc activity", HttpStatusCode.Conflict, "already running in this section")]
    [InlineData("update the process variables", HttpStatusCode.Conflict, "already set on this step")]
    [InlineData("update the process variables", HttpStatusCode.BadRequest, "not a type the engine can store")]
    [InlineData("create the process variables", HttpStatusCode.BadRequest, "not a type the engine can store")]
    public void Each_reachable_refusal_has_our_words(
        string operation, HttpStatusCode status, string expected)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(new FlowableRequestException(
            status, operation, $"Flowable could not {operation}. HTTP {(int)status}."));

        Assert.Contains(expected, described, StringComparison.Ordinal);
        Assert.DoesNotContain("server log", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every declared operation is one some route actually passes (#372).
    /// </summary>
    /// <remarks>
    /// A row keyed on an operation string nothing emits is decoration that reads
    /// as coverage — #349 found exactly that in the deployment table
    /// (<c>flowable-bpmn-parse-failure</c>, which Flowable never emits), and #372
    /// found it again here (<c>list the ad-hoc subprocess activities</c>, whose
    /// route has no try/catch at all).
    /// </remarks>
    [Fact]
    public void Every_declared_operation_is_one_the_client_actually_passes()
    {
        var client = File.ReadAllText(Path.Combine(
            AutoNate.Web.Tests.Infrastructure.RepoRoot.Path,
            "src", "AutoNate.Web", "Services", "Flowable", "FlowableClient.cs"));

        var declared = typeof(WorkflowEndpoints).Assembly
            .GetType("AutoNate.Web.Endpoints.EngineRefusal")!
            .GetField("RuntimeReasons", System.Reflection.BindingFlags.NonPublic
                                        | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

        var operations = ((System.Collections.IEnumerable)declared)
            .Cast<object>()
            .Select(row => (string)row.GetType().GetField("Item1")!.GetValue(row)!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(operations);

        var unreachable = operations
            .Where(op => !client.Contains($"\"{op}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            unreachable.Count == 0,
            "These operations are keys in RuntimeReasons but FlowableClient never passes "
            + "them to EnsureSuccessAsync as a literal, so their rows can never fire:\n  "
            + string.Join("\n  ", unreachable)
            + "\n\nA row nothing can reach reads as coverage and is not (#372).");
    }

    /// <summary>There is no length by which a refusal can grow (#344, #349).</summary>    /// <summary>There is no length by which a refusal can grow (#344, #349).</summary>
    /// <remarks>
    /// The previous version had no cap at all — 4000 characters in, 4090 out.
    /// A code-only design caps by construction, and this says so out loud.
    /// </remarks>
    [Fact]
    public void The_output_is_bounded_however_long_the_engine_was()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(
            // Lowercase, so it CAN match the code shape. The #344 version used
            // 'A' x 40000, which never matches [a-z0-9-] -- so it passed while the
            // real bound was 5,092 characters (#349).
            AsTheClientBuildsIt($"[Validation set: 'x' | Problem: '{new string('a', 40_000)}'] : "
                + new string('b', 40_000)));

        Assert.True(described.Length < 300, $"output was {described.Length} characters");
    }
}
