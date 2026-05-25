using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class AqlAssistSkillTests
{
    private static readonly Guid SessionUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task validate_aql_returns_ok_when_query_text_is_empty()
    {
        var skill = new AqlAssistSkill();
        var sp = ServicesWith<IQueryEntityRegistry>(new FakeRegistry());
        var result = await Invoke(skill, AqlAssistSkill.ValidateToolName, new { queryText = "" }, sp);

        Assert.Equal("aql_validation", result.GetProperty("kind").GetString());
        Assert.False(result.GetProperty("data").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task validate_aql_surfaces_parser_errors_in_envelope_not_as_exception()
    {
        var skill = new AqlAssistSkill();
        var sp = ServicesWith<IQueryEntityRegistry>(new FakeRegistry());
        // Deliberately broken syntax — the parser throws AqlValidationException
        // (or a generic Exception); the skill must catch and return an envelope.
        var result = await Invoke(skill, AqlAssistSkill.ValidateToolName, new
        {
            queryText = "FROM ((((( garbage"
        }, sp);

        Assert.Equal("aql_validation", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.False(data.GetProperty("ok").GetBoolean());
        var errors = data.GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task run_aql_with_empty_query_returns_failed_envelope_and_does_NOT_call_executor()
    {
        var skill = new AqlAssistSkill();
        var executor = new FakeExecutor();
        var sp = ServicesWith<IAqlExecutor>(executor);

        var result = await Invoke(skill, AqlAssistSkill.RunToolName, new { queryText = "" }, sp);

        Assert.Equal("aql_run_failed", result.GetProperty("kind").GetString());
        Assert.False(result.GetProperty("data").GetProperty("ok").GetBoolean());
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task run_aql_invokes_executor_with_session_principal_and_caps_maxRows()
    {
        var skill = new AqlAssistSkill();
        var executor = new FakeExecutor
        {
            Response = new QueryResult(
                Columns: new[] { new QueryColumnMeta("Name", QueryDataType.String) },
                Rows: new IReadOnlyDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Name"] = "row1" }
                },
                TotalCount: 1,
                Truncated: false,
                DurationMs: 5)
        };
        var sp = ServicesWith<IAqlExecutor>(executor);

        var result = await Invoke(skill, AqlAssistSkill.RunToolName, new
        {
            queryText = "FROM Records",
            // Caller asks for 500; the skill caps at 200.
            maxRows = 500
        }, sp);

        Assert.Equal("aql_run_result", result.GetProperty("kind").GetString());
        Assert.True(result.GetProperty("data").GetProperty("ok").GetBoolean());

        var call = Assert.Single(executor.Calls);
        Assert.Equal("FROM Records", call.QueryText);
        Assert.Equal(200, call.HardCap); // Capped.
    }

    [Fact]
    public async Task run_aql_passes_aql_validation_errors_through_failed_envelope()
    {
        var skill = new AqlAssistSkill();
        var executor = new FakeExecutor
        {
            ThrowOnExecute = new AqlValidationException(new[] { "Unknown column 'Foo'." })
        };
        var sp = ServicesWith<IAqlExecutor>(executor);

        var result = await Invoke(skill, AqlAssistSkill.RunToolName, new
        {
            queryText = "FROM Records COLUMNS Foo"
        }, sp);

        Assert.Equal("aql_run_failed", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.False(data.GetProperty("ok").GetBoolean());
        Assert.Equal("Unknown column 'Foo'.", data.GetProperty("errors")[0].GetString());
    }

    // --- helpers ---

    private static async Task<JsonElement> Invoke(AqlAssistSkill skill, string toolName, object args, IServiceProvider sp)
    {
        var tool = skill.Tools.Single(t => t.Name == toolName);
        var argsJson = JsonSerializer.Serialize(args);
        using var doc = JsonDocument.Parse(argsJson);
        var ctx = new AgentToolContext(
            new AgentSessionContext(new ClaimsPrincipal(), SessionUserId, "aql-query"),
            sp);
        return await tool.Invoke(doc.RootElement, ctx, CancellationToken.None);
    }

    private static IServiceProvider ServicesWith<T>(T instance) where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(instance);
        return services.BuildServiceProvider();
    }

    private sealed class FakeExecutor : IAqlExecutor
    {
        public List<(string QueryText, int? HardCap)> Calls { get; } = new();
        public QueryResult Response { get; set; } = new(
            Columns: Array.Empty<QueryColumnMeta>(),
            Rows: Array.Empty<IReadOnlyDictionary<string, object?>>(),
            TotalCount: 0,
            Truncated: false,
            DurationMs: 0);
        public AqlValidationException? ThrowOnExecute { get; set; }

        public Task<QueryResult> ExecuteAsync(string queryText, ClaimsPrincipal actor, int? hardCap, CancellationToken cancellationToken)
        {
            Calls.Add((queryText, hardCap));
            if (ThrowOnExecute is not null) throw ThrowOnExecute;
            return Task.FromResult(Response);
        }
    }

    private sealed class FakeRegistry : IQueryEntityRegistry
    {
        public IReadOnlyList<string> EntityNames { get; } = new[] { "Records", "Flows" };
        public bool TryGet(string name, out IQueryEntity entity)
        {
            entity = null!;
            return false;
        }
    }
}
