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
        new(HttpStatusCode.InternalServerError, "deploy process",
            $"Flowable could not deploy process. HTTP 500 Internal Server Error. {rawResponseBody}");

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

    /// <summary>There is no length by which a refusal can grow (#344, #349).</summary>
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
