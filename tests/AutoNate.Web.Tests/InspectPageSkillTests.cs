using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.PageQuery;
using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class InspectPageSkillTests
{
    [Fact]
    public async Task Inspect_returns_no_snapshot_when_context_lacks_one()
    {
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(null);

        var result = await Invoke(skill, InspectPageSkill.InspectToolName, "{}", ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("no_snapshot", result.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Inspect_returns_overview_when_no_topic()
    {
        var snapshot = MakeSnapshot("workflow", new
        {
            workflow = new { id = "abc", name = "Test" },
            selection = new { ids = Array.Empty<string>() },
            nodes = new object[] { new { id = "Start_1" } }
        }, summary: "Editing draft workflow 'Test'.", version: 7);
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(snapshot);

        var result = await Invoke(skill, InspectPageSkill.InspectToolName, "{}", ctx);

        Assert.Equal("inspect_page_result", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal("workflow", data.GetProperty("pageKey").GetString());
        Assert.Equal("Editing draft workflow 'Test'.", data.GetProperty("summary").GetString());
        Assert.Equal(7, data.GetProperty("version").GetInt64());
        var keys = data.GetProperty("dataKeys").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("workflow", keys);
        Assert.Contains("selection", keys);
        Assert.Contains("nodes", keys);
    }

    [Fact]
    public async Task Inspect_walks_dotted_path_into_nested_objects()
    {
        var snapshot = MakeSnapshot("workflow", new
        {
            selection = new
            {
                ids = new[] { "UserTask_3" },
                elements = new[] { new { id = "UserTask_3", type = "bpmn:UserTask", name = "Manager Approval" } }
            }
        });
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(snapshot);

        var result = await Invoke(skill, InspectPageSkill.InspectToolName,
            JsonSerializer.Serialize(new { topic = "selection.elements" }), ctx);

        Assert.Equal("inspect_page_result", result.GetProperty("kind").GetString());
        var arr = result.GetProperty("data").EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal("Manager Approval", arr[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Inspect_indexes_arrays_via_numeric_segments()
    {
        var snapshot = MakeSnapshot("workflow", new
        {
            nodes = new[]
            {
                new { id = "n1", name = "first" },
                new { id = "n2", name = "second" }
            }
        });
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(snapshot);

        var result = await Invoke(skill, InspectPageSkill.InspectToolName,
            JsonSerializer.Serialize(new { topic = "nodes.1.name" }), ctx);

        Assert.Equal("second", result.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Inspect_returns_topic_not_found_for_missing_path()
    {
        var snapshot = MakeSnapshot("workflow", new { workflow = new { id = "abc" } });
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(snapshot);

        var result = await Invoke(skill, InspectPageSkill.InspectToolName,
            JsonSerializer.Serialize(new { topic = "nonsense.path" }), ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("topic_not_found", result.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Query_round_trips_via_channel_and_returns_success()
    {
        var stub = new StubChannel();
        var freshNode = JsonSerializer.SerializeToElement(new { id = "X", name = "Fresh" });
        stub.NextResult = new PageQueryResult.Success(freshNode);

        var skill = new InspectPageSkill(stub, new StubActionChannel());
        var ctx = MakeContext(MakeSnapshot("workflow", new { workflow = new { id = "abc" } }));

        var result = await Invoke(skill, InspectPageSkill.QueryToolName,
            JsonSerializer.Serialize(new { topic = "node.byId", args = new { id = "X" } }), ctx);

        Assert.Equal("query_page_result", result.GetProperty("kind").GetString());
        Assert.Equal("Fresh", result.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("node.byId", stub.LastTopic);
        Assert.Equal("X", stub.LastArgs!.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Query_surfaces_failure_from_channel()
    {
        var stub = new StubChannel { NextResult = new PageQueryResult.Failure("page_unreachable", "User navigated away.") };
        var skill = new InspectPageSkill(stub, new StubActionChannel());
        var ctx = MakeContext(MakeSnapshot("workflow", new { workflow = new { id = "abc" } }));

        var result = await Invoke(skill, InspectPageSkill.QueryToolName,
            JsonSerializer.Serialize(new { topic = "bpmn.xml" }), ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("page_unreachable", result.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Query_rejects_missing_topic()
    {
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(MakeSnapshot("workflow", new { workflow = new { id = "abc" } }));

        var result = await Invoke(skill, InspectPageSkill.QueryToolName, "{}", ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("bad_request", result.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task System_prompt_fragment_is_silent_without_snapshot()
    {
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var withSnapshot = new AgentSessionContext(new ClaimsPrincipal(), Guid.NewGuid(), "workflow",
            ConversationId: Guid.NewGuid(), PageContext: MakeSnapshot("workflow", new { x = 1 }));
        var withoutSnapshot = new AgentSessionContext(new ClaimsPrincipal(), Guid.NewGuid(), "workflow",
            ConversationId: Guid.NewGuid());

        Assert.Null(skill.SystemPromptFragment(withoutSnapshot));
        Assert.NotNull(skill.SystemPromptFragment(withSnapshot));
    }

    private static PageContextSnapshot MakeSnapshot(string pageKey, object data, string? summary = null, long version = 1)
    {
        var dataElement = JsonSerializer.SerializeToElement(data);
        return new PageContextSnapshot(pageKey, SchemaVersion: 1, Summary: summary, Version: version, Data: dataElement);
    }

    [Fact]
    public async Task Apply_action_with_confirmed_false_returns_proposal_without_round_trip()
    {
        var actionStub = new StubActionChannel();
        var skill = new InspectPageSkill(new StubChannel(), actionStub);
        var ctx = MakeContext(MakeSnapshot("workflow", new { x = 1 }));

        var result = await Invoke(skill, InspectPageSkill.ApplyActionToolName,
            JsonSerializer.Serialize(new { action = "update_node", args = new { id = "X", properties = new { name = "y" } } }),
            ctx);

        Assert.Equal("page_action_proposal", result.GetProperty("kind").GetString());
        Assert.Equal("update_node", result.GetProperty("data").GetProperty("action").GetString());
        Assert.False(result.GetProperty("data").GetProperty("confirmed").GetBoolean());
        Assert.Null(actionStub.LastAction);
    }

    [Fact]
    public async Task Apply_action_with_confirmed_true_round_trips_and_returns_applied_envelope()
    {
        var actionStub = new StubActionChannel
        {
            NextResult = new PageActionResult.Success("Renamed X to Y.", JsonSerializer.SerializeToElement(new { id = "X" }))
        };
        var skill = new InspectPageSkill(new StubChannel(), actionStub);
        var ctx = MakeContext(MakeSnapshot("workflow", new { x = 1 }));

        var result = await Invoke(skill, InspectPageSkill.ApplyActionToolName,
            JsonSerializer.Serialize(new { action = "update_node", args = new { id = "X" }, confirmed = true }),
            ctx);

        Assert.Equal("page_action_applied", result.GetProperty("kind").GetString());
        Assert.Equal("Renamed X to Y.", result.GetProperty("data").GetProperty("summary").GetString());
        Assert.Equal("update_node", actionStub.LastAction);
    }

    [Fact]
    public async Task Apply_action_surfaces_failure_from_channel()
    {
        var actionStub = new StubActionChannel
        {
            NextResult = new PageActionResult.Failure("not_found", "No such node.")
        };
        var skill = new InspectPageSkill(new StubChannel(), actionStub);
        var ctx = MakeContext(MakeSnapshot("workflow", new { x = 1 }));

        var result = await Invoke(skill, InspectPageSkill.ApplyActionToolName,
            JsonSerializer.Serialize(new { action = "update_node", args = new { id = "Missing" }, confirmed = true }),
            ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("not_found", result.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Apply_action_rejects_missing_action_name()
    {
        var skill = new InspectPageSkill(new StubChannel(), new StubActionChannel());
        var ctx = MakeContext(MakeSnapshot("workflow", new { x = 1 }));

        var result = await Invoke(skill, InspectPageSkill.ApplyActionToolName, "{}", ctx);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Equal("bad_request", result.GetProperty("data").GetProperty("code").GetString());
    }

    private static AgentSessionContext MakeContext(PageContextSnapshot? snapshot) =>
        new(new ClaimsPrincipal(), Guid.NewGuid(), "workflow",
            ConversationId: Guid.NewGuid(), PageContext: snapshot);

    private static async Task<JsonElement> Invoke(InspectPageSkill skill, string toolName, string argsJson, AgentSessionContext session)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var tool = skill.Tools.Single(t => t.Name == toolName);
        var ctx = new AgentToolContext(session, new EmptyServiceProvider());
        return await tool.Invoke(args, ctx, CancellationToken.None);
    }

    private sealed class StubChannel : IPageQueryChannel
    {
        public PageQueryResult NextResult { get; set; } = new PageQueryResult.Failure("not_set", "Test did not configure NextResult.");
        public string? LastTopic { get; private set; }
        public JsonElement? LastArgs { get; private set; }

        public Task<PageQueryResult> AskAsync(string topic, JsonElement? args, CancellationToken cancellationToken)
        {
            LastTopic = topic;
            LastArgs = args;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class StubActionChannel : IPageActionChannel
    {
        public PageActionResult NextResult { get; set; } = new PageActionResult.Failure("not_set", "Test did not configure NextResult.");
        public string? LastAction { get; private set; }
        public JsonElement? LastArgs { get; private set; }

        public Task<PageActionResult> ApplyAsync(string action, JsonElement? args, CancellationToken cancellationToken)
        {
            LastAction = action;
            LastArgs = args;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
