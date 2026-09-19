using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The two failure classes /prepare can report, told apart (#234).
/// </summary>
/// <remarks>
/// <para>
/// A draft save stores half-built work; publish is the gate. That split only
/// works if the client can tell "your diagram breaks a rule" from "your diagram
/// could not be read", because the first should still save and the second must
/// not -- storing an unreadable diagram puts a model in the database the studio
/// cannot reopen.
/// </para>
/// <para>
/// Both arrive as a non-empty <c>Errors</c> list, and both carry a
/// <c>WorkflowModel</c> that looks fine, so nothing about the response
/// distinguished them before <c>Prepared</c>. That is what these facts pin: not
/// that errors are reported -- other tests cover that -- but that the flag
/// separates the two, in BOTH directions. One direction alone would pass against
/// a constant.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PrepareWorkflowPreparedFlagTests
{
    // Well-formed, and refused: a timer start event with no schedule cannot run.
    // The point is that the XML itself is perfectly readable.
    private const string ValidXmlBrokenRule = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="unscheduled_flow" name="Unscheduled" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:timerEventDefinition id="Timer_1" />
            </bpmn:startEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    // Not XML at all: the tag never closes, so normalization cannot even parse it.
    private const string MalformedXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="truncated" isExecutable="true">
        """;

    private static PrepareWorkflowRequest RequestFor(string xml) => new(
        new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Prepared Flag",
            ProcessKey = string.Empty,
            BpmnXml = xml
        },
        Array.Empty<WorkflowElementSnapshot>());

    private static async Task<PrepareWorkflowResponse> PrepareAsync(string xml)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        // Prime the auth cookie the same way the sibling suite does.
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", RequestFor(xml));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public async Task A_readable_diagram_that_breaks_a_rule_is_still_prepared()
    {
        var result = await PrepareAsync(ValidXmlBrokenRule);

        Assert.NotEmpty(result.Errors);
        Assert.True(
            result.Prepared,
            "A diagram that parses but breaks a publish rule IS prepared -- the " +
            "normalized model exists and a draft save should store it. Errors: " +
            string.Join(" | ", result.Errors));

        // The complement of "prepared": the model came back normalized, not
        // handed straight back. An empty ProcessKey went in; prepare fills it.
        Assert.False(string.IsNullOrWhiteSpace(result.Model.ProcessKey));
    }

    [Fact]
    public async Task A_diagram_that_cannot_be_read_is_not_prepared()
    {
        var result = await PrepareAsync(MalformedXml);

        Assert.NotEmpty(result.Errors);
        Assert.False(
            result.Prepared,
            "Unreadable XML has no normalized model, so nothing should store it. " +
            "Errors: " + string.Join(" | ", result.Errors));

        // And the model is the one we sent -- which is exactly why saving it
        // would be wrong, and why the flag rather than the model is the signal.
        Assert.Equal(string.Empty, result.Model.ProcessKey);
    }
}
