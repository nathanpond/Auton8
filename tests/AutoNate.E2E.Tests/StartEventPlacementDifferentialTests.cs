using System.Text;
using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Publish validation and the engine agree about start-event placement (#309).
/// </summary>
/// <remarks>
/// <para>
/// <b>This test exists because three hand-written placement rules in a row were
/// complete with respect to the defect already known and blind to the next one.</b>
/// #158 covered `conditional × process level`. #289 widened the definition axis
/// and left the container axis binary. #309 found the cell in neither list —
/// `{message, timer, signal} × plain subProcess`, which publishes with zero
/// errors and makes Flowable refuse the whole deployment.
/// </para>
/// <para>
/// Each fix added the cells that had just been found. So the instrument is the
/// problem, not the cells: an enumerated rule cannot be checked by an enumerated
/// test written by the same person on the same day.
/// </para>
/// <para>
/// This one enumerates nothing by hand. It builds the <b>cross-product</b> of
/// every start-event definition the vendored modeller can place against every
/// container an author can put one in, deploys each to a live Flowable, and
/// asserts <c>ValidateProcess</c> reaches the same verdict. A cell nobody thought
/// of is still in the product, so it is still in the test.
/// </para>
/// <para>
/// Deliberately <c>RequiresService=Flowable</c> and therefore outside CI: the
/// engine IS the oracle here. There is no version of this test that runs without
/// one, and a mocked oracle would be the enumerated rule again wearing a costume.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class StartEventPlacementDifferentialTests : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    public StartEventPlacementDifferentialTests(AutoNateE2EFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    /// <summary>Every start-event event definition bpmn-js offers, plus the bare one.</summary>
    private static readonly (string Name, string Xml)[] Definitions =
    [
        ("none", ""),
        ("message", """<message id="Msg_1" name="m" />""" + "|" + """<messageEventDefinition messageRef="Msg_1" />"""),
        ("timer", "|" + """<timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition>"""),
        ("signal", """<signal id="Sig_1" name="s" />""" + "|" + """<signalEventDefinition signalRef="Sig_1" />"""),
        ("conditional", "|" + """<conditionalEventDefinition><condition>${ok}</condition></conditionalEventDefinition>"""),
        ("error", """<error id="Err_1" name="e" errorCode="E" />""" + "|" + """<errorEventDefinition errorRef="Err_1" />"""),
        ("escalation", """<escalation id="Esc_1" name="x" escalationCode="X" />""" + "|" + """<escalationEventDefinition escalationRef="Esc_1" />"""),
        // #321. `compensate` was missing, which is why restoring a wrong allow-list
        // row stayed green: there was no cell to fail.
        ("compensate", "|" + """<compensateEventDefinition />"""),
    ];

    /// <summary>Every container an author can put a start event in.</summary>
    private static readonly string[] Containers = ["process", "eventSubProcess", "embeddedSubProcess", "adHocSubProcess"];

    /// <summary>
    /// Cells where Auton8 deliberately refuses something the engine accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modelled on <c>BpmnSupportManifestTests</c>'s declared engine-axis
    /// departures, and for the same reason: the honest way to be stricter than the
    /// engine is to say so in one place, not to weaken the test that would
    /// otherwise catch it.
    /// </para>
    /// <para>
    /// This table found its first entry on the differential test's first run,
    /// which is the argument for the instrument: 27 of 28 cells agreed, and the
    /// 28th turned out to be a decision rather than a defect.
    /// </para>
    /// </remarks>
    private readonly record struct Departure(string Reason, string RefusalPhrase);

    private static readonly Dictionary<(string Definition, string Container), Departure> StricterThanTheEngine =
        new()
        {
            [("none", "eventSubProcess")] = new(
                Reason:
                    "an event subprocess is triggered BY its start event, so a bare one can never " +
                    "trigger at all -- Flowable deploys it and it silently never runs, which looks " +
                    "exactly like its event never happening",

                // #321. Each departure names the phrase that proves ITS OWN refusal,
                // because a departure is refused by a different rule than the one this
                // test is about. Matching the placement rule's phrase here would assert
                // the wrong thing; matching anything at all would assert nothing.
                // This one comes from BuildEventSubProcessErrors.
                RefusalPhrase: "starts with a plain start event"),
        };

    public static TheoryData<string, string> Cells()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, _) in Definitions)
        foreach (var container in Containers)
        {
            data.Add(name, container);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Publish_validation_agrees_with_the_engine(string definitionName, string container)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var (roots, definitionXml) = Split(Definitions.Single(d => d.Name == definitionName).Xml);
        var key = $"pl{Guid.NewGuid():N}"[..18];
        var xml = Diagram(key, roots, definitionXml, container);

        // What Auton8 says -- through /api/workflows/prepare, which is the route
        // the studio itself calls, rather than the validation function. The E2E
        // project cannot reference AutoNate.Web, and going through the API is the
        // better test anyway: it is the path an author takes.
        // #321. The oracle, narrowed. This was `Contains("start event")`, which the
        // #115 descope message also satisfies -- "Compensation **Start Event**
        // ('x') cannot be deployed: ..." -- so all four compensate cells reported
        // a refusal that came from somewhere else entirely. Short-circuiting the
        // placement rule turned 15 of 28 cells red and left those four green.
        //
        // "cannot start " is BuildStartEventPlacementErrors' own phrase and is
        // produced by nothing else in the product. A comment there says to keep it
        // stable, or to fix this line with it.
        var errors = await PrepareErrorsAsync(api, key, xml);

        // AGREEMENT is rule-agnostic: the question is whether publish refuses what
        // the engine refuses, and which rule did it is irrelevant to that question.
        // Narrowing this to one rule's phrase made the test fail on cells another
        // rule legitimately owns.
        var refusedByAutoNate = errors.Count > 0;

        // ATTRIBUTION is rule-specific, and is what #321 was actually about: with a
        // loose oracle the placement rule could be switched off while unrelated
        // refusals kept the cells green. Only asked where the placement rule is the
        // one that should have fired -- which is derived from the manifest, not
        // enumerated here: an element the manifest WITHDRAWS is refused by
        // BuildUnsupportedElementErrors first, and that is correct.
        var withdrawnByManifest = WithdrawnStartEventDefinitions.Contains(definitionName);
        var refusedByPlacement = errors.Any(e => e.Contains(PlacementRefusalPhrase, StringComparison.Ordinal));

        // What the engine says. Deployed with the raw BPMN namespace, since this
        // is about the engine's own parser rather than our expansion.
        var (deployed, deploymentId, engineMessage) = await TryDeployAsync(key, xml);

        try
        {
            _output.WriteLine(
                $"{definitionName,-12} x {container,-20} autonateRefuses={refusedByAutoNate,-5} " +
                $"engineDeploys={deployed,-5} {engineMessage}");

            // The property: we refuse exactly what the engine refuses, EXCEPT where
            // we have written down why we are deliberately stricter.
            //
            // The direction that ships broken workflows is `!refusedByAutoNate &&
            // !deployed` -- publish says yes and the engine refuses the whole
            // deployment. The other direction is a false refusal, which blocks an
            // author from something that works, and is equally a defect unless it
            // is declared below.
            if (StricterThanTheEngine.TryGetValue((definitionName, container), out var departure))
            {
                var reason = departure.Reason;
                var refusedByTheDeclaredRule = errors.Any(
                    e => e.Contains(departure.RefusalPhrase, StringComparison.Ordinal));
                // #321. BOTH sides. This asserted only that Auton8 refuses, so a row
                // added for any refused cell switched that cell's engine-agreement
                // check off permanently and nothing ever re-validated the other half.
                // A departure is a claim about a DISAGREEMENT; if the engine starts
                // refusing too, the departure is obsolete and should be deleted.
                Assert.True(refusedByTheDeclaredRule,
                    $"A declared departure says Auton8 refuses {definitionName} x {container} " +
                    $"({reason}), and no error matched its stated phrase " +
                    $"'{departure.RefusalPhrase}'. Either the rule was lost, or its wording " +
                    "changed and this row is now checking nothing.");

                Assert.True(deployed,
                    $"A declared departure says Auton8 is STRICTER than the engine for " +
                    $"{definitionName} x {container}, but the engine refuses it too " +
                    $"({engineMessage}). The departure is obsolete -- delete the row and let " +
                    "the ordinary agreement check cover this cell.");
                return;
            }

            // The placement rule must be the thing that refused, where it is the
            // thing that should have. This is the assertion that goes red when the
            // rule is switched off, and it is scoped so an unrelated refusal cannot
            // stand in for it.
            if (!deployed && !withdrawnByManifest)
            {
                Assert.True(refusedByPlacement,
                    $"{definitionName} x {container} is refused by the engine ({engineMessage}) " +
                    "and by publish, but not by the placement rule -- no error contains " +
                    $"'{PlacementRefusalPhrase}'. Some other rule is carrying this cell, so the " +
                    "placement rule could be deleted without this test noticing. That is exactly " +
                    "what #321 found.");
            }

            Assert.True(
                refusedByAutoNate != deployed,
                refusedByAutoNate
                    ? $"FALSE REFUSAL: a {definitionName} start event in a {container} deploys to " +
                      "Flowable, and publish refuses it. An author is being stopped from doing " +
                      "something that works."
                    : $"MISSED REFUSAL: a {definitionName} start event in a {container} publishes " +
                      $"with no error and Flowable refuses it -- {engineMessage}. One misplaced " +
                      "start event fails the author's WHOLE deployment, behind a green studio.");
        }
        finally
        {
            if (deploymentId is not null) await DeleteAsync(deploymentId);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>The errors publish would report for this diagram.</summary>
    private static async Task<IReadOnlyList<string>> PrepareErrorsAsync(
        IAPIRequestContext api, string key, string xml)
    {
        var response = await api.PostAsync("/api/workflows/prepare", new APIRequestContextOptions
        {
            DataObject = new
            {
                model = new { id = Guid.NewGuid(), name = TestNames.Prefixed(key), processKey = key, bpmnXml = xml },
                elementSnapshots = Array.Empty<object>()
            }
        });

        Assert.True(response.Ok, $"prepare failed: {response.Status} {await response.TextAsync()}");

        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();
    }


    private static (string Roots, string Definition) Split(string xml)
    {
        if (xml.Length == 0) return ("", "");
        var parts = xml.Split('|');
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// The signature of <c>BuildStartEventPlacementErrors</c>'s message (#321).
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than "start event". The #115 descope message contains
    /// that phrase and comes from a completely different rule, so matching on it
    /// made four cells pass on an unrelated refusal while the rule under test was
    /// switched off. Mirrored in a comment beside the message that produces it,
    /// because this project cannot reference the web assembly.
    /// </remarks>
    private const string PlacementRefusalPhrase = "cannot start ";

    /// <summary>
    /// Start-event definitions the manifest withdraws, read from the manifest (#321).
    /// </summary>
    /// <remarks>
    /// Derived, not listed. An element the manifest withdraws is refused by
    /// <c>BuildUnsupportedElementErrors</c> before placement is ever considered, and
    /// that refusal is correct — so the attribution assertion must not demand the
    /// placement rule for it. Reading the shared manifest is what keeps this honest:
    /// promote a row and this set shrinks by itself, and the cell starts demanding
    /// the placement rule, which is the behaviour #321 wanted.
    /// </remarks>
    private static readonly HashSet<string> WithdrawnStartEventDefinitions = LoadWithdrawnStartDefinitions();

    // A LIMIT worth stating: while a definition is withdrawn, a wrong row in
    // StartEventDefinitionsAllowed is masked -- the product refuses the element for
    // a different, correct reason, so there is no defect to catch and this test
    // rightly stays green. The row is still wrong, and it becomes a live missed
    // refusal the moment the manifest promotes it (that scenario IS red). Closing
    // the gap properly means deriving the table from the manifest rather than
    // hand-writing it, which is #324 in M4b.

    private static HashSet<string> LoadWithdrawnStartDefinitions()
    {
        // Anchored on AutoNate.sln via the shared helper, NOT on a ".git"
        // directory: in a worktree ".git" is a file, so the old walk ran off the
        // top of the filesystem and threw from this static initialiser (#332).
        var path = Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-support.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("elements").EnumerateArray()
            .Where(e => e.GetProperty("localName").GetString() == "startEvent")
            .Where(e => e.GetProperty("studio").GetString() != "supported")
            .Select(e => e.GetProperty("eventDefinition").GetString())
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>An EL expression, kept out of the interpolated literal below.</summary>
    private const string Done = "${done}";

    private static string Diagram(string key, string roots, string definitionXml, string container)
    {
        var start = $"""
                <startEvent id="s2" name="Trigger">
                  {definitionXml}
                </startEvent>
            """;

        var body = container switch
        {
            "process" => $"""
                {start}
                <sequenceFlow id="f9" sourceRef="s2" targetRef="t" />
                <userTask id="t" name="Work" />
                """,
            "eventSubProcess" => $"""
                <startEvent id="s" />
                <sequenceFlow id="f0" sourceRef="s" targetRef="t" />
                <userTask id="t" name="Work" />
                <subProcess id="sub" name="Handler" triggeredByEvent="true">
                {start}
                  <sequenceFlow id="f1" sourceRef="s2" targetRef="t2" />
                  <userTask id="t2" name="Handled" />
                </subProcess>
                """,
            "adHocSubProcess" => $"""
                <startEvent id="s" />
                <sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <adHocSubProcess id="sub" name="Case work">
                {start}
                  <sequenceFlow id="f1" sourceRef="s2" targetRef="t2" />
                  <userTask id="t2" name="Inner" />
                  <completionCondition>{Done}</completionCondition>
                </adHocSubProcess>
                <sequenceFlow id="f2" sourceRef="sub" targetRef="e" />
                <endEvent id="e" />
                """,
            _ => $"""
                <startEvent id="s" />
                <sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <subProcess id="sub" name="Inner work">
                {start}
                  <sequenceFlow id="f1" sourceRef="s2" targetRef="t2" />
                  <userTask id="t2" name="Inner" />
                </subProcess>
                <sequenceFlow id="f2" sourceRef="sub" targetRef="e" />
                <endEvent id="e" />
                """,
        };

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:flowable="http://flowable.org/bpmn"
                         targetNamespace="http://autonate.dev/workflows">
              {roots}
              <process id="{key}" name="Placement probe" isExecutable="true">
            {body}
              </process>
            </definitions>
            """;
    }

    private static async Task<(bool Deployed, string? Id, string Message)> TryDeployAsync(string key, string xml)
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        using var content = new MultipartFormDataContent();
        var file = new StringContent(xml, Encoding.UTF8);
        // Named with the suite prefix so the sweep can recognise it as ours, and
        // deleted by id below regardless.
        content.Add(file, "file", $"{FlowableDeploymentSweep.SuiteDeploymentPrefix}{key}.bpmn20.xml");

        using var response = await client.PostAsync("service/repository/deployments", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var problem = System.Text.RegularExpressions.Regex.Match(body, @"Problem: '([^']+)'");
            return (false, null, problem.Success ? problem.Groups[1].Value : $"HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(body);
        return (true, document.RootElement.GetProperty("id").GetString(), "");
    }

    private static async Task DeleteAsync(string deploymentId)
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // By id, never by prefix -- the rule every verifier in this milestone was
        // given and two fixtures had to be taught (#248, #297).
        await client.DeleteAsync($"service/repository/deployments/{Uri.EscapeDataString(deploymentId)}?cascade=true");
    }

    /// <summary>
    /// A plain subprocess with two start events is refused (#321).
    /// </summary>
    /// <remarks>
    /// The round-7 PR said "Flowable's multiple-start-event refusal is covered
    /// too". It was implemented and had no cell: deleting the rule left 541/541
    /// green. This is the cell, and it is a differential check like the rest --
    /// what Auton8 says is compared against what the engine says, not against what
    /// I expect the engine to say.
    /// </remarks>
    [Fact]
    public async Task A_plain_subprocess_with_two_start_events_agrees_with_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ms{Guid.NewGuid():N}"[..18];
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         targetNamespace="http://autonate.dev/workflows">
              <process id="{key}" name="Two starts" isExecutable="true">
                <startEvent id="s" />
                <sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <subProcess id="sub" name="Inner work">
                  <startEvent id="s1" name="First" />
                  <startEvent id="s2" name="Second" />
                  <sequenceFlow id="f1" sourceRef="s1" targetRef="t" />
                  <userTask id="t" name="Inner" />
                </subProcess>
                <sequenceFlow id="f2" sourceRef="sub" targetRef="e" />
                <endEvent id="e" />
              </process>
            </definitions>
            """;

        var errors = await PrepareErrorsAsync(api, key, xml);
        var refused = errors.Any(e => e.Contains("start events", StringComparison.Ordinal));

        var (deployed, deploymentId, engineMessage) = await TryDeployAsync(key, xml);
        try
        {
            Assert.True(refused != deployed,
                refused
                    ? $"FALSE REFUSAL: a subprocess with two start events deploys to Flowable " +
                      "and publish refuses it."
                    : $"MISSED REFUSAL: a subprocess with two start events publishes with no " +
                      $"error and Flowable refuses it -- {engineMessage}. One misplaced start " +
                      "event fails the author's WHOLE deployment, behind a green studio.");
        }
        finally
        {
            if (deploymentId is not null) await DeleteAsync(deploymentId);
        }
    }

}
