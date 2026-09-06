using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Pipelines.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// #152: an author runs a script against sample variables from the editor.
//
// The panel is only worth having if what it shows is what production does, and
// if a sandbox refusal is legible as a refusal rather than as a bug. Both are
// endpoint behaviour, so both are pinned here.
[Trait("Category", "Integration")]
public sealed class WorkflowScriptTestRunEndpointTests
{
    private sealed class FakeRunner(Func<ScriptTaskResult> behaviour) : IScriptTaskRunner
    {
        public string? LastProcessInstanceId { get; private set; }
        public string? LastLanguage { get; private set; }

        public Task<ScriptTaskResult> RunScriptTaskAsync(
            string processInstanceId, string nodeId, string code, string language,
            IReadOnlyDictionary<string, object?> variables, CancellationToken cancellationToken)
        {
            LastProcessInstanceId = processInstanceId;
            LastLanguage = language;
            return Task.FromResult(behaviour());
        }
    }

    private static async Task<(AutoNateWebApplicationFactory Factory, HttpClient Client, FakeRunner Runner)>
        HostAsync(Func<ScriptTaskResult> behaviour)
    {
        var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var runner = new FakeRunner(behaviour);
        var client = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddScoped<IScriptTaskRunner>(_ => runner))).CreateClient();
        // Primes the dev auto-login cookie; later POSTs carry the admin identity.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return (factory, client, runner);
    }

    private static Task<HttpResponseMessage> RunAsync(
        HttpClient client, string code, object? variables = null) =>
        client.PostAsJsonAsync("/api/workflow-script-tasks/test-run", new
        {
            code,
            variables = variables ?? new Dictionary<string, object?>(),
        });

    [Fact]
    public async Task ItReportsOnlyTheVariablesTheScriptChanged()
    {
        // The author should see the effect, not be left to diff a full dump
        // against what they typed in. `untouched` comes back with the value it
        // went in with and must not be listed.
        var mutations = new Dictionary<string, object?>
        {
            ["untouched"] = 1,
            ["total"] = 42,
        };
        var (factory, client, _) = await HostAsync(() => new ScriptTaskResult(null, mutations));
        await using var _f = factory;

        var response = await RunAsync(client, "variables.set('total', 42);",
            new Dictionary<string, object?> { ["untouched"] = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var changed = doc.RootElement.GetProperty("changed").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Equal(new List<string?> { "total" }, changed);
    }

    [Fact]
    public async Task ASandboxRefusalIsReportedAsARefusalRatherThanAScriptError()
    {
        // The sandbox does not announce refusals — it withholds the binding, so
        // this arrives as a bare ReferenceError indistinguishable from a typo.
        // Reporting it as a plain script error would send an author hunting for
        // a bug that is not there. This is the assertion the story turns on.
        var (factory, client, _) = await HostAsync(
            () => throw new ScriptExecutionException("ReferenceError: Java is not defined"));
        await using var _f = factory;

        var response = await RunAsync(client, "Java.type('java.lang.System');");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("sandbox_refusal", doc.RootElement.GetProperty("errorKind").GetString());
        // And it explains the boundary rather than repeating the bare error.
        Assert.Contains("sandbox", doc.RootElement.GetProperty("errorMessage").GetString()!);
    }

    [Fact]
    public async Task AnOrdinaryScriptErrorIsNotMisreportedAsARefusal()
    {
        // The counterpart. If everything became a "refusal" the distinction
        // would be worthless.
        var (factory, client, _) = await HostAsync(
            () => throw new ScriptExecutionException("ReferenceError: totl is not defined"));
        await using var _f = factory;

        var response = await RunAsync(client, "return totl;");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("script_error", doc.RootElement.GetProperty("errorKind").GetString());
        Assert.Contains("totl", doc.RootElement.GetProperty("errorMessage").GetString()!);
    }

    [Fact]
    public async Task AnUnreachableSandboxSaysTheScriptDidNotRun()
    {
        // Distinct from both above: the author learns nothing about their
        // script from this, and the message has to say so rather than looking
        // like a failure of their code.
        var (factory, client, _) = await HostAsync(
            () => throw new InvalidOperationException("did not reply within 30s"));
        await using var _f = factory;

        var response = await RunAsync(client, "variables.set('x', 1);");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("executor_unavailable", doc.RootElement.GetProperty("errorKind").GetString());
        Assert.Contains("not run", doc.RootElement.GetProperty("errorMessage").GetString()!);
    }

    [Fact]
    public async Task ATestRunTouchesNoRealProcess()
    {
        var (factory, client, runner) = await HostAsync(
            () => new ScriptTaskResult(null, new Dictionary<string, object?>()));
        await using var _f = factory;

        await RunAsync(client, "variables.set('x', 1);");

        // A synthetic scope, not a started execution — nothing to write back to.
        Assert.Equal("test-run", runner.LastProcessInstanceId);
    }

    [Theory]
    [InlineData(null, "js")]
    [InlineData("javascript", "js")]
    [InlineData("python", "python")]
    public async Task TheDeclaredScriptFormatSelectsTheRunner(string? scriptFormat, string expected)
    {
        // #154. BPMN says "javascript"; the executor's wire format says "js".
        // Asserted rather than assumed, because getting it wrong would silently
        // run every Python script task through the JavaScript runner — where it
        // would fail as a syntax error that says nothing about the cause.
        //
        // A null format is the older Flowable extension, which only ever sent
        // JavaScript; it must keep working rather than failing a valid request.
        var (factory, client, runner) = await HostAsync(
            () => new ScriptTaskResult(null, new Dictionary<string, object?>()));
        await using var _f = factory;

        await client.PostAsJsonAsync("/api/workflow-script-tasks/test-run", new
        {
            code = "variables.set('x', 1)",
            variables = new Dictionary<string, object?>(),
            scriptFormat,
        });

        Assert.Equal(expected, runner.LastLanguage);
    }

    [Fact]
    public async Task TheTestRunIsGatedByTheSamePermissionAsEditingTheWorkflow()
    {
        // Running a test executes author-supplied code, so it must not be a
        // lesser operation than authoring it. Asserted on the route's metadata
        // rather than by driving a second user through a grant setup, which
        // would test the authorization stack rather than this route's choice.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/workflow-script-tasks/test-run");

        var gate = endpoint.Metadata.GetMetadata<RequirePermissionMetadata>();
        Assert.NotNull(gate);
        Assert.Equal(EntityKinds.WorkflowModel, gate!.Kind);
        Assert.Equal(Actions.Edit, gate.Action);
    }
}
