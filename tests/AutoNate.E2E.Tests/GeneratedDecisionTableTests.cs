using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The real generator's output, on the real engine, through the app (#110).
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here goes through Auton8's own API.</b> Creating the table runs
/// the validator; publishing runs <c>DecisionTableDmn.Generate</c> and deploys the
/// result to Flowable; the try-it call evaluates it. So a generator change that
/// produces plausible XML the engine will not run fails <em>here</em>, which no
/// unit test can do.
/// </para>
/// <para>
/// The first version of this file deployed a hand-written copy of what the
/// generator "should" produce. That was a worse test than it looked: its own
/// comment claimed it was kept in step by failing when the two diverged, and it
/// could not — nothing in it called the generator. This one cannot drift, because
/// there is only one path.
/// </para>
/// <para>
/// It also settles a question the unit tests raise but cannot answer: the
/// generator XML-escapes cell text, so <c>"escalate"</c> reaches the engine as
/// <c>&amp;quot;escalate&amp;quot;</c>. Whether FEEL sees the quotes after the XML
/// parser has unescaped them is an engine fact, and the rules below only match if
/// it does.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class GeneratedDecisionTableTests : E2ETestBase
{
    public GeneratedDecisionTableTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_table_authored_through_the_api_deploys_and_decides()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = "e2e_" + Guid.NewGuid().ToString("n")[..12];

        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey = key,
                name = "Routing",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_amount", label = "Amount", name = "amount", typeRef = "number" },
                    new { id = "in_region", label = "Region", name = "region", typeRef = "string" }
                },
                outputs = new[]
                {
                    new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
                },
                rules = new object[]
                {
                    // An EMPTY input cell meaning "any" -- the positional trap. If
                    // the generator dropped it, this rule would silently stop
                    // matching and the XML would still parse.
                    new { id = "r1", inputEntries = new[] { "> 10000", "" }, outputEntries = new[] { "\"escalate\"" } },
                    new { id = "r2", inputEntries = new[] { "", "\"EU\"" }, outputEntries = new[] { "\"eu-desk\"" } },
                    new { id = "r3", inputEntries = new[] { "< 100", "\"US\"" }, outputEntries = new[] { "\"auto-approve\"" } }
                }
            }
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var createdBody = JsonDocument.Parse(await created.TextAsync());
        var id = createdBody.RootElement.GetProperty("id").GetString()!;

        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok,
            $"The engine refused the generated DMN: {published.Status} {await published.TextAsync()}");

        // Each rule, reached independently, through the same evaluation path a
        // process uses.
        Assert.Equal("escalate", await RouteAsync(api, id, 50_000, "US"));
        Assert.Equal("eu-desk", await RouteAsync(api, id, 500, "EU"));
        Assert.Equal("auto-approve", await RouteAsync(api, id, 50, "US"));

        // And the complement: inputs no rule covers. Not an error -- an empty
        // result, which a caller must be able to tell from a failed call.
        Assert.Null(await RouteAsync(api, id, 500, "US"));

        // Cleanup: the engine is shared with the dev environment.
        await api.DeleteAsync($"/api/decision-tables/{id}");
    }

    [Fact]
    public async Task A_table_the_validator_refuses_never_reaches_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = "e2e_" + Guid.NewGuid().ToString("n")[..12];

        // An unquoted word in a string column: it parses, it would deploy, and it
        // would match nothing at run time because the engine reads it as a
        // variable reference. That is the whole reason the validator exists, and
        // it is the case a type check alone would wave through.
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey = key,
                name = "Broken",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_region", label = "Region", name = "region", typeRef = "string" }
                },
                outputs = new[]
                {
                    new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
                },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "EU" }, outputEntries = new[] { "\"eu-desk\"" } }
                }
            }
        });

        Assert.False(created.Ok, "A cell the engine would silently never match should be refused at save.");
        var body = await created.TextAsync();
        Assert.Contains("variable name", body, StringComparison.Ordinal);
        Assert.Contains("\\\"EU\\\"", body, StringComparison.Ordinal);
    }

    private static async Task<string?> RouteAsync(
        IAPIRequestContext api, string id, double amount, string region)
    {
        var response = await api.PostAsync($"/api/decision-tables/{id}/test", new APIRequestContextOptions
        {
            DataObject = new { inputs = new { amount, region } }
        });
        Assert.True(response.Ok, $"Evaluating failed: {response.Status} {await response.TextAsync()}");

        using var document = JsonDocument.Parse(await response.TextAsync());
        if (!document.RootElement.GetProperty("matched").GetBoolean()) return null;

        var first = document.RootElement.GetProperty("outputs")[0];
        return first.TryGetProperty("route", out var route) ? route.GetString() : null;
    }
}
