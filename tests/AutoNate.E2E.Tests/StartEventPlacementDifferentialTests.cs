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
    private static readonly Dictionary<(string Definition, string Container), string> StricterThanTheEngine =
        new()
        {
            [("none", "eventSubProcess")] =
                "an event subprocess is triggered BY its start event, so a bare one can never " +
                "trigger at all -- Flowable deploys it and it silently never runs, which looks " +
                "exactly like its event never happening (BuildEventSubProcessErrors)",
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
        var refusedByAutoNate = (await PrepareErrorsAsync(api, key, xml))
            .Any(e => e.Contains("start event", StringComparison.OrdinalIgnoreCase));

        // What the engine says. Deployed with the raw BPMN namespace, since this
        // is about the engine's own parser rather than our expansion.
        var (deployed, deploymentId, engineMessage) = await TryDeployAsync(key, xml);
        var checkedDeparture = false;

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
            if (StricterThanTheEngine.TryGetValue((definitionName, container), out var reason))
            {
                Assert.True(refusedByAutoNate,
                    $"A declared departure says Auton8 refuses {definitionName} x {container} " +
                    $"({reason}), and it did not. Either the rule was lost, or the departure " +
                    "should be deleted -- a declaration nothing enforces is worse than none.");
                checkedDeparture = true;
                return;
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

        // A departure that stopped applying would otherwise pass silently by
        // falling through to the agreement assertion.
        _ = checkedDeparture;
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
}
