using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Decisions;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// Publishing, versioning and the try-it panel (#110).
/// </summary>
[Trait("Category", "Integration")]
public sealed class DecisionTablePublishTests
{
    private static DecisionTableModel Sample(string key, string route = "escalate") => new()
    {
        DecisionKey = key,
        Name = "Routing",
        HitPolicy = DecisionHitPolicies.First,
        Inputs = [new DecisionColumn("in_amount", "Amount", "amount", DecisionTypeRefs.Number)],
        Outputs = [new DecisionColumn("out_route", "Route", "route", DecisionTypeRefs.String)],
        Rules = [new DecisionRule("r1", ["> 10"], [$"\"{route}\""])]
    };

    private static string Key() => "t_" + Guid.NewGuid().ToString("n")[..10];

    private static async Task<DecisionTableModel> CreateAsync(HttpClient client, DecisionTableModel table)
    {
        var response = await client.PostAsJsonAsync("/api/decision-tables/", table);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DecisionTableModel>()
            ?? throw new InvalidOperationException("Create returned no body.");
    }

    private static readonly XNamespace Dmn = "https://www.omg.org/spec/DMN/20191111/MODEL/";

    /// <summary>Every output cell's text, as a parser sees it.</summary>
    private static List<string> OutputTexts(string dmn) =>
        XDocument.Parse(dmn).Descendants(Dmn + "outputEntry").Select(e => e.Value).ToList();

    [Fact]
    public async Task A_bad_cell_is_refused_at_save_with_every_error_not_the_first()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var broken = Sample(Key()) with
        {
            Rules =
            [
                new DecisionRule("r1", ["banana"], ["\"ok\""]),
                new DecisionRule("r2", ["> 1"], [""])
            ]
        };

        var response = await client.PostAsJsonAsync("/api/decision-tables/", broken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToList();

        // BOTH, not the first. An author fixing a forty-rule table one message at
        // a time is the experience this story exists to avoid, and a validator
        // that returned early would pass a test asserting only "it was refused".
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("Rule 1", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Rule 2", StringComparison.Ordinal));

        // And nothing was stored. A 400 with a row written is the worst outcome:
        // the author is told it failed and the table is there anyway.
        var listed = await client.GetFromJsonAsync<DecisionTableModel[]>("/api/decision-tables/");
        Assert.DoesNotContain(listed!, t => t.DecisionKey == broken.DecisionKey);
    }

    [Fact]
    public async Task Publishing_deploys_the_generated_dmn_and_records_a_version()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var key = Key();
        var created = await CreateAsync(client, Sample(key));

        var response = await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null);
        response.EnsureSuccessStatusCode();
        var published = await response.Content.ReadFromJsonAsync<DecisionTableModel>();

        Assert.Equal(1, published!.PublishedVersionNumber);
        Assert.False(published.IsDraft);
        Assert.NotNull(published.LastDeployment);

        // What reached the engine is the GENERATED DMN, not the stored rules. The
        // stub records it, so this asserts the publish path actually generates
        // rather than sending something else.
        var sent = Assert.Single(factory.DecisionStub.DeployedXml);

        // PARSED, not substring-matched. `"` is XML-escaped on the way out, so
        // grepping for the literal fails against correct output -- and a test that
        // greps is asserting an encoding rather than a value.
        Assert.Equal(["\"escalate\""], OutputTexts(sent));
        Assert.Contains($"Deploy:{key}", factory.DecisionStub.Calls);

        var versions = await client.GetFromJsonAsync<DecisionTableVersionSummary[]>(
            $"/api/decision-tables/{created.Id}/versions");
        var version = Assert.Single(versions!);
        Assert.Equal(1, version.VersionNumber);
    }

    [Fact]
    public async Task Republishing_leaves_version_1_exactly_as_it_was()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var key = Key();
        var created = await CreateAsync(client, Sample(key, route: "escalate"));
        (await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null))
            .EnsureSuccessStatusCode();

        // Change what it decides, and publish again.
        var edited = Sample(key, route: "auto-approve") with { Id = created.Id };
        (await client.PutAsJsonAsync($"/api/decision-tables/{created.Id}", edited))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null))
            .EnsureSuccessStatusCode();

        var versions = (await client.GetFromJsonAsync<DecisionTableVersionSummary[]>(
            $"/api/decision-tables/{created.Id}/versions"))!;
        Assert.Equal(2, versions.Length);

        // THE ASSERTION THAT MATTERS, and it is on version 1's own DMN rather than
        // on the version count. A process bound to version 1 keeps version 1's
        // behaviour -- so the stored XML for v1 must still say "escalate" even
        // though the draft now says "auto-approve". Asserting only that a second
        // version appeared would pass against an implementation that overwrote
        // the first.
        var stub = factory.DecisionStub;
        Assert.Equal(2, stub.DeployedXml.Count);
        Assert.Equal(["\"escalate\""], OutputTexts(stub.DeployedXml[0]));
        Assert.Equal(["\"auto-approve\""], OutputTexts(stub.DeployedXml[1]));

        // And the two versions carry different engine decision ids, which is what
        // #111 binds to.
        Assert.NotEqual(versions[0].DecisionId, versions[1].DecisionId);
    }

    [Fact]
    public async Task A_refused_deployment_leaves_the_draft_unpublished()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var created = await CreateAsync(client, Sample(Key()));
        factory.DecisionStub.DeployThrows =
            StubFlowableDecisionClient.Refusal("cvc-complex-type: something the engine hated");

        var response = await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The failure the AC names: a version that exists in Auton8 and not in the
        // engine. Every later read would then report a published table that cannot
        // be evaluated, and nothing would say why.
        var after = await client.GetFromJsonAsync<DecisionTableModel>(
            $"/api/decision-tables/{created.Id}");
        Assert.Null(after!.PublishedVersionNumber);
        Assert.True(after.IsDraft);

        var versions = await client.GetFromJsonAsync<DecisionTableVersionSummary[]>(
            $"/api/decision-tables/{created.Id}/versions");
        Assert.Empty(versions!);

        // And the engine's own words reach the author, rather than a generic failure.
        Assert.Contains("the engine hated", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_try_it_panel_refuses_an_unpublished_table_rather_than_evaluating_the_draft()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var created = await CreateAsync(client, Sample(Key()));

        var response = await client.PostAsJsonAsync(
            $"/api/decision-tables/{created.Id}/test",
            new { inputs = new Dictionary<string, object?> { ["amount"] = 50 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The key link the AC names: ONE evaluation path, so a table that tests
        // correctly cannot behave differently in a process. Evaluating the draft
        // here would be a second path, and the try-it panel would then be
        // confirming something no process will ever run.
        Assert.DoesNotContain(factory.DecisionStub.Calls,
            c => c.StartsWith("Evaluate:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_try_it_panel_goes_through_the_engine_once_published()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var key = Key();
        var created = await CreateAsync(client, Sample(key));
        (await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null))
            .EnsureSuccessStatusCode();

        factory.DecisionStub.EvaluationOutputs.Add(
            new Dictionary<string, object?> { ["route"] = "escalate" });

        var response = await client.PostAsJsonAsync(
            $"/api/decision-tables/{created.Id}/test",
            new { inputs = new Dictionary<string, object?> { ["amount"] = 50 } });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("matched").GetBoolean());
        Assert.Equal("escalate",
            body.RootElement.GetProperty("outputs")[0].GetProperty("route").GetString());

        Assert.Contains($"Evaluate:{key}", factory.DecisionStub.Calls);
    }

    [Fact]
    public async Task Publishing_and_deleting_are_on_the_record()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var created = await CreateAsync(client, Sample(Key()));
        (await client.PostAsync($"/api/decision-tables/{created.Id}/publish", null))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/decision-tables/{created.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToList();

        // A published table decides business outcomes, and deleting one can stop a
        // deployed process working. Both belong on the record.
        Assert.Contains(WorkflowAdminEventTypes.DecisionTableSaved, types);
        Assert.Contains(WorkflowAdminEventTypes.DecisionTablePublished, types);
        Assert.Contains(WorkflowAdminEventTypes.DecisionTableDeleted, types);
    }
}
