using System.Net;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Pipelines.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// #147: BPMN script tasks execute in the executor sandbox, reached through
// this callback. What matters here is the failure mapping, because it is what
// the Flowable side keys on:
//
//   * the author's code failed        -> 422, deterministic, retrying re-fails
//   * the sandbox could not be reached -> 503, retryable
//
// Collapsing those would make a workflow's error surface unable to tell an
// author's mistake from an infrastructure blip. Neither path may run the
// script by any other route: a fallback would reinstate GHSA-82rh-gjhw-rg9r
// exactly when the system is degraded.
[Trait("Category", "Integration")]
public sealed class WorkflowScriptTaskEndpointTests
{
    private const string Secret = "script-task-callback-secret";

    // Records what it was asked to do so a test can assert the endpoint made
    // exactly one attempt — no retry loop, and no second route on failure.
    private sealed class FakeRunner(Func<ScriptTaskResult> behaviour) : IScriptTaskRunner
    {
        public int Calls { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastVariables { get; private set; }

        public Task<ScriptTaskResult> RunScriptTaskAsync(
            string processInstanceId,
            string nodeId,
            string code,
            string language,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastVariables = variables;
            return Task.FromResult(behaviour());
        }
    }

    private static async Task<(AutoNateWebApplicationFactory Factory, HttpClient Client, FakeRunner Runner)>
        HostAsync(Func<ScriptTaskResult> behaviour)
    {
        var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["WorkflowBehaviors:CallbackSharedSecret"] = Secret,
        });
        var runner = new FakeRunner(behaviour);
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IScriptTaskRunner>(_ => runner))).CreateClient();
        return (factory, client, runner);
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? secret, string code = "variables.set('x', 1);",
        object? variables = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            processInstanceId = "pi-1",
            executionId = "ex-1",
            nodeId = "ScriptTask_1",
            code,
            variables = variables ?? new Dictionary<string, object?>(),
            correlationId = "corr-1",
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workflow-script-tasks/execute")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
        {
            request.Headers.Add(SharedSecretEndpointFilter.HeaderName, secret);
        }
        return client.SendAsync(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-secret")]
    public async Task WithMissingOrWrongSecret_IsUnauthorized(string? secret)
    {
        var (factory, client, runner) = await HostAsync(() => new ScriptTaskResult(null, new Dictionary<string, object?>()));
        await using var _ = factory;

        var response = await PostAsync(client, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // The gate has to stop it before the sandbox is asked to run anything.
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task WithCorrectSecret_ReturnsMutationsAndResult()
    {
        var mutations = new Dictionary<string, object?> { ["approved"] = true, ["score"] = 7 };
        var (factory, client, runner) = await HostAsync(() => new ScriptTaskResult("done", mutations));
        await using var _ = factory;

        var response = await PostAsync(client, Secret, variables: new Dictionary<string, object?> { ["total"] = 42 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("done", doc.RootElement.GetProperty("result").GetString());
        Assert.True(doc.RootElement.GetProperty("mutations").GetProperty("approved").GetBoolean());
        Assert.Equal(7, doc.RootElement.GetProperty("mutations").GetProperty("score").GetInt32());
        // The variables the engine sent must reach the sandbox, or a script
        // reading them would see an empty world.
        Assert.NotNull(runner.LastVariables);
        Assert.True(runner.LastVariables!.ContainsKey("total"));
    }

    [Fact]
    public async Task WhenTheScriptFails_Returns422AndNamesItAsAScriptError()
    {
        var (factory, client, runner) = await HostAsync(
            () => throw new ScriptExecutionException("ReferenceError: nope is not defined"));
        await using var _ = factory;

        var response = await PostAsync(client, Secret);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("script_error", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("nope is not defined", doc.RootElement.GetProperty("message").GetString());
        // Exactly one attempt: a deterministic failure must not be retried here.
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task WhenTheExecutorIsUnreachable_Returns503AndDoesNotFallBack()
    {
        var (factory, client, runner) = await HostAsync(
            () => throw new InvalidOperationException("Executor sidecar did not reply within 30s."));
        await using var _ = factory;

        var response = await PostAsync(client, Secret);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("executor_unavailable", doc.RootElement.GetProperty("error").GetString());
        // The assertion that matters: one attempt, and no second route. If a
        // fallback is ever added this fails, which is the point.
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task ScriptErrorAndExecutorFailure_AreDistinguishable()
    {
        // Stated as its own test because it is the contract the Flowable side
        // keys on, and it would still be satisfiable by accident if the two
        // above were checked independently against the same status.
        var (f1, c1, _) = await HostAsync(() => throw new ScriptExecutionException("boom"));
        await using (f1)
        {
            var (f2, c2, _) = await HostAsync(
                () => throw new InvalidOperationException("unreachable"));
            await using (f2)
            {
                var scriptError = await PostAsync(c1, Secret);
                var unreachable = await PostAsync(c2, Secret);
                Assert.NotEqual(scriptError.StatusCode, unreachable.StatusCode);
            }
        }
    }

    [Fact]
    public async Task AnEmptyScriptIsRejectedRatherThanSucceedingAsANoOp()
    {
        var (factory, client, runner) = await HostAsync(
            () => new ScriptTaskResult(null, new Dictionary<string, object?>()));
        await using var _ = factory;

        var response = await PostAsync(client, Secret, code: "   ");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, runner.Calls);
    }
}
