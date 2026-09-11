using AutoNate.E2E.Tests.Support;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The Flowable sweep removes the suite's deployments and nobody else's (#214).
/// </summary>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class FlowableDeploymentSweepTests : E2ETestBase
{
    public FlowableDeploymentSweepTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task It_removes_suite_deployments_and_leaves_everything_else()
    {
        // The criterion that matters: a prefix sweep against a SHARED engine can
        // delete a developer's real work. The engine held deployments named
        // `autonate`, `default`, `car` and `account` alongside 303 from the suite.
        using var client = FlowableDeploymentSweep.CreateClient(
            "http://localhost:8080/flowable-rest", "rest-admin", "test");

        var probeTag = $"p{Guid.NewGuid():N}"[..9];
        var mine = $"e2e-{probeTag}-mine";
        var theirs = $"{probeTag}-theirs";

        await DeployAsync(client, mine);
        await DeployAsync(client, theirs);

        try
        {
            Assert.True(await ExistsAsync(client, mine), "Precondition: the suite deployment was not created.");
            Assert.True(await ExistsAsync(client, theirs), "Precondition: the other deployment was not created.");

            // #297. Cut-off injected: both deployments were made DURING this run,
            // and the default cut-off spares exactly that. The axis under test here
            // is the NAME, so the clock is moved out of the way -- the cut-off has
            // its own pair of tests below, in both directions.
            // #308. Scoped to this test's own plants. Without `onlyNamed` a cut-off
            // of "now" cascade-deletes every pre-existing e2e-* deployment on the
            // shared engine -- a concurrent run's live instances and jobs included,
            // which is the destruction #297 exists to stop.
            var deleted = await FlowableDeploymentSweep.SweepAsync(
                client, DateTimeOffset.UtcNow, onlyNamed: probeTag);

            Assert.True(deleted > 0, "The sweep reported deleting nothing.");
            Assert.False(await ExistsAsync(client, mine), "A suite deployment survived the sweep.");
            // The half that makes this safe to run on a shared engine.
            Assert.True(await ExistsAsync(client, theirs),
                "The sweep deleted a deployment outside the suite's naming convention.");
        }
        finally
        {
            await DeleteByNameAsync(client, theirs);
            // `mine` is normally swept above, but if an assertion failed first it
            // is still there -- and leaving it would seed the next run's backlog.
            await DeleteByNameAsync(client, mine);
        }
    }

    private static async Task DeployAsync(HttpClient client, string name)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         targetNamespace="http://autonate.dev/workflows">
              <process id="p{Guid.NewGuid():N}" isExecutable="true"><startEvent id="s" /></process>
            </definitions>
            """;

        using var content = new MultipartFormDataContent();
        var file = new StringContent(xml);
        content.Add(file, "file", $"{name}.bpmn20.xml");
        using var response = await client.PostAsync("service/repository/deployments", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<bool> ExistsAsync(HttpClient client, string name) =>
        await FindIdAsync(client, name) is not null;

    private static async Task<string?> FindIdAsync(HttpClient client, string name)
    {
        // Newest first: the shared engine holds more deployments than one page,
        // so an unsorted query silently misses a deployment made seconds ago.
        using var response = await client.GetAsync(
            "service/repository/deployments?size=1000&sort=deployTime&order=desc");
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var element in document.RootElement.GetProperty("data").EnumerateArray())
        {
            if (element.TryGetProperty("name", out var n) && n.GetString() == name)
            {
                return element.GetProperty("id").GetString();
            }
        }
        return null;
    }

    private static async Task DeleteByNameAsync(HttpClient client, string name)
    {
        if (await FindIdAsync(client, name) is { } id)
        {
            using var _ = await client.DeleteAsync($"service/repository/deployments/{id}?cascade=true");
        }
    }

    // #257, second pass. The claim this file most needs to make, and the one it
    // was not making: that the name the APP produces is a name the sweep matches.
    //
    // Both tests above deploy their fixture straight to the engine, choosing the
    // name themselves. That is how the original defect survived its own test for
    // the whole of M4 — the suite published through FlowableClient, which named
    // deployments after the process key (`adh…`, `cgx…`), while the sweep looked
    // for `e2e-`. Two conventions, no test comparing them.
    //
    // This one publishes through the real endpoint and then asks the sweep to
    // find it. It fails if the prefix option is dropped, if the fixture stops
    // setting it, or if the sweep's constant drifts from the fixture's.
    [Fact]
    public async Task The_sweep_matches_what_the_app_actually_deploys()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"swp{Guid.NewGuid():N}"[..20];
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(key);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{key}" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
                <bpmn:userTask id="t" name="Wait" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var created = await api.PostAsync("/api/workflows/", new Microsoft.Playwright.APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new Microsoft.Playwright.APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");

        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // The name the app chose, not one this test chose.
        var deployedAs = FlowableDeploymentSweep.SuiteDeploymentPrefix + key;
        Assert.True(
            await ExistsAsync(client, deployedAs),
            $"The app published as something other than '{deployedAs}'. The sweep " +
            "matches by name, so a mismatch here is the sweep silently removing " +
            "nothing -- which is exactly what #257 was.");

        // #297. The cut-off is injected because this deployment was made DURING
        // the run, and the default cut-off deliberately spares exactly that. What
        // is under test here is the name matching, so the clock is moved out of
        // the way rather than raced -- the cut-off's own behaviour has its own
        // test below.
        // #308. Scoped to this key, so the engine's other deployments are untouched.
        var deleted = await FlowableDeploymentSweep.SweepAsync(
            client, DateTimeOffset.UtcNow, onlyNamed: key);
        Assert.True(deleted > 0, "The sweep reported deleting nothing.");
        Assert.False(await ExistsAsync(client, deployedAs),
            "The sweep did not remove a deployment the app itself published.");
    }

    /// <summary>
    /// A freshly deployed suite deployment survives the DEFAULT sweep (#304).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test #297 should have been. Its version injected a cut-off, so
    /// the overload the fixture actually calls was never exercised — and that
    /// overload was broken two ways: its <c>RunStartedAt</c> was set at first type
    /// access rather than run start, and "older than when I started" spares only
    /// runs that began later, not the ones already going.
    /// </para>
    /// <para>
    /// Deliberately <b>no cut-off argument</b>. A deployment made moments ago is
    /// what a concurrent run's looks like, and the default must spare it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_default_sweep_leaves_a_deployment_made_moments_ago()
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // Named exactly as the suite names its own, so the PREFIX rule matches and
        // only the age rule can save it. A name outside the convention would pass
        // for the wrong reason.
        var key = FlowableDeploymentSweep.SuiteDeploymentPrefix + $"cc{Guid.NewGuid():N}"[..18];
        await DeployAsync(client, key);

        try
        {
            Assert.True(await ExistsAsync(client, key),
                "the fixture did not deploy, so the assertion below would be vacuous");

            // THE DEFAULT OVERLOAD. This is the whole point of the test.
            var deleted = await FlowableDeploymentSweep.SweepAsync(client);

            Assert.True(await ExistsAsync(client, key),
                $"The default sweep removed a deployment made moments ago — it deleted " +
                $"{deleted}. On a shared engine that is a CONCURRENT run's live work, and " +
                "cascade takes its instances and jobs with it.");
        }
        finally
        {
            await DeleteByNameAsync(client, key);
        }
    }

    /// <summary>The complement: an older one with the same name IS swept.</summary>
    /// <remarks>
    /// Without this, the cut-off could be a rule that spares everything — which is
    /// how #257's first fix failed in the other direction, and how the sweep spent
    /// the whole of M4 removing nothing while its test stayed green.
    /// </remarks>
    [Fact]
    public async Task A_deployment_older_than_the_threshold_is_still_swept()
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var key = FlowableDeploymentSweep.SuiteDeploymentPrefix + $"cp{Guid.NewGuid():N}"[..18];
        await DeployAsync(client, key);

        // The shape of a leftover: a cut-off after it, standing in for the two
        // hours the default would wait. The injected overload is used here BECAUSE
        // the alternative is a two-hour test; the default's own behaviour is
        // covered by The_default_sweep_leaves_a_deployment_made_moments_ago above.
        await Task.Delay(1100);
        var runStartedAt = DateTimeOffset.UtcNow;

        var swept = false;
        try
        {
            Assert.True(await ExistsAsync(client, key),
                "the fixture did not deploy, so the assertion below would be vacuous");

            // #308. Scoped: without onlyNamed this cut-off sweeps every e2e-*
            // deployment older than a moment ago, which on a shared engine is a
            // concurrent run's live work.
            await FlowableDeploymentSweep.SweepAsync(client, runStartedAt, onlyNamed: key);

            swept = !await ExistsAsync(client, key);
            Assert.True(swept,
                "The sweep left a deployment that predates the run. The cut-off has " +
                "become a rule that spares everything, which is the sweep doing " +
                "nothing while reading as protection.");
        }
        finally
        {
            if (!swept) await DeleteByNameAsync(client, key);
        }
    }

    [Fact]
    public async Task The_sweep_leaves_a_realistically_named_deployment_alone_at_any_age()
    {
        // The complement of the above, and the property the first #257 fix broke:
        // it swept by age, so a developer's own three-hour-old work went with the
        // orphans while this file's own remarks promised it would not.
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // Named the way FlowableClient names a PRODUCTION model: no prefix.
        var key = $"cgx{Guid.NewGuid():N}"[..20];
        await DeployAsync(client, key);

        try
        {
            Assert.True(await ExistsAsync(client, key),
                "the fixture did not deploy, so the assertion below would be vacuous");

            await FlowableDeploymentSweep.SweepAsync(client);

            Assert.True(await ExistsAsync(client, key),
                "The sweep removed a deployment outside the suite's naming " +
                "convention. On a shared engine that is a developer's real work.");
        }
        finally
        {
            await DeleteByNameAsync(client, key);
        }
    }
}
