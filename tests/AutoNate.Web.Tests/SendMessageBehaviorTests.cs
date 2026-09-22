using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using AutoNate.Web.Services.Workflow.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A send resolves against the PUBLISHED diagram (#557).
/// </summary>
/// <remarks>
/// <para>
/// `SendMessageBehavior` had no test of any kind. It was moved onto
/// <c>GetPublishedByProcessKeyAsync</c> by #553's fix — correctly, because it
/// runs inside an EXECUTING instance, so the send it resolves must come from the
/// definition Flowable deployed — and nothing pinned that. Reverting it to the
/// draft-returning lookup failed nothing.
/// </para>
/// <para>
/// The discriminator is deliberately two different FAILURE codes rather than
/// success versus failure. Reaching the correlator would need a live engine;
/// both codes here are decided from the diagram alone, before any of that, so
/// the test stays in the slim tier while still distinguishing exactly which xml
/// was read.
/// </para>
/// </remarks>
public sealed class SendMessageBehaviorTests
{
    private const string ProcessKey = "sender";
    private const string ActivityId = "Send_1";

    /// <summary>The published diagram is the one consulted (#557).</summary>
    /// <remarks>
    /// The published xml carries the send with no target workflow, so resolving
    /// against it answers <c>noTargetProcess</c>. The draft carries no send at
    /// that id at all, so resolving against the draft answers <c>notASend</c>.
    /// One run, two distinguishable answers, no engine.
    /// </remarks>
    [Fact]
    public async Task The_send_is_resolved_from_the_published_diagram_not_the_draft()
    {
        var behavior = Behavior(
            publishedXml: SendWithoutTarget(),
            draftXml: NoSendAtAll());

        var result = await behavior.ExecuteAsync(Context(), CancellationToken.None);

        Assert.True(result.Failed);

        // THE ORACLE. Reading the draft gives "notASend"; only the published
        // diagram can produce this code.
        Assert.Equal("noTargetProcess", result.FailureCode);
    }

    /// <summary>
    /// A workflow with no published version sends nothing (#557).
    /// </summary>
    /// <remarks>
    /// The complement. Without it the fact above passes against a lookup that
    /// answers for every model regardless of publication — the first half of
    /// #544, in the send path.
    /// </remarks>
    [Fact]
    public async Task A_workflow_with_no_published_version_reports_no_stored_diagram()
    {
        var behavior = Behavior(publishedXml: null, draftXml: SendWithoutTarget());

        var result = await behavior.ExecuteAsync(Context(), CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Equal("senderNotFound", result.FailureCode);
    }

    private static SendMessageBehavior Behavior(string? publishedXml, string draftXml)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowModelStore>(
            new TwoXmlStore(publishedXml, draftXml));
        services.AddSingleton(NullLogger<SendMessageBehavior>.Instance);

        var provider = services.BuildServiceProvider();
        return new SendMessageBehavior(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SendMessageBehavior>.Instance);
    }

    private static BehaviorContext Context() => new(
        ProcessInstanceId: "pi-1",
        ExecutionId: "ex-1",
        ProcessDefinitionKey: ProcessKey,
        ProcessName: "Sender",
        ActivityId: ActivityId,
        BusinessKey: null,
        CorrelationId: "corr-1",
        Variables: new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    // A send task naming its message but NOT its target workflow.
    private static string SendWithoutTarget() =>
        $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <message id="M" name="order.placed"/>
          <process id="{ProcessKey}">
            <sendTask id="{ActivityId}" flowable:autonateMessageName="order.placed"/>
          </process>
        </definitions>
        """;

    // The same id, but not a send at all.
    private static string NoSendAtAll() =>
        $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <process id="{ProcessKey}">
            <userTask id="{ActivityId}" name="not a send"/>
          </process>
        </definitions>
        """;

    /// <summary>
    /// A store that answers the two lookups DIFFERENTLY (#557).
    /// </summary>
    /// <remarks>
    /// The whole point. Every other double in the suite returns one xml for
    /// both, which is why no test could tell the two lookups apart — the defect
    /// #552 and #557 are both about, expressed as a fixture.
    /// </remarks>
    private sealed class TwoXmlStore(string? publishedXml, string draftXml) : IWorkflowModelStore
    {
        private static WorkflowModel Model(string xml) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Sender",
            ProcessKey = ProcessKey,
            BpmnXml = xml
        };

        // #170. A single-pool stub: a definition key IS the process key.
        public Task<WorkflowModel?> GetPublishedByDefinitionKeyAsync(string processDefinitionKey, CancellationToken cancellationToken = default) =>
            GetPublishedByProcessKeyAsync(processDefinitionKey, cancellationToken);

        public Task<WorkflowModel?> GetPublishedByProcessKeyAsync(
            string processKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(publishedXml is null ? null : Model(publishedXml));

        public Task<WorkflowModel?> GetByProcessKeyAsync(
            string processKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowModel?>(Model(draftXml));

        public Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowModel>> ListPublishedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> PublishAsync(
            WorkflowModel model, WorkflowDeploymentInfo deployment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(
            Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
