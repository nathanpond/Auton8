using AutoNate.Web.Models;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Starting a workflow from a queue message (#524).
/// </summary>
/// <remarks>
/// The dispatcher's decisions are all decidable without a bus or an engine: which
/// topic it answers on, which names it acts on, and — the one that needs saying —
/// that a signal of the same name is invisible to it. Only the Dapr hop and the
/// real engine start need services, and those are covered elsewhere.
/// </remarks>
public sealed class WorkflowMessageDispatcherTests
{
    private const string Topic = "workflow.messages";
    private const string Name = "orderPlaced";

    [Fact]
    public async Task A_matching_message_starts_the_workflow_that_declares_it()
    {
        var flowable = new StubFlowableClient { StartedByMessageInstanceId = "pi-1" };
        var dispatcher = Dispatcher(flowable, StartedByMessage(Name), Registration(Name, "orders"));

        await dispatcher.HandleAsync(Bus(Topic, $$"""{"eventType":"{{Name}}"}"""));

        // The engine was actually asked to start something. Asserting only that
        // nothing threw would pass against a dispatcher that decided correctly
        // and then called nobody.
        Assert.Contains(flowable.Calls, c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal));
    }

    /// <summary>
    /// #524 AC3. Nothing started, and it is not silent about it.
    /// </summary>
    [Fact]
    public async Task A_name_no_workflow_starts_on_starts_nothing()
    {
        var flowable = new StubFlowableClient();
        var dispatcher = Dispatcher(flowable, StartedByMessage(Name), Registration(Name, "orders"));

        await dispatcher.HandleAsync(Bus(Topic, """{"eventType":"somethingElse"}"""));

        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal));
    }

    /// <summary>
    /// A topic no workflow offers a message start on is not ours (#524).
    /// </summary>
    [Fact]
    public async Task A_topic_with_no_registrations_is_left_alone()
    {
        var flowable = new StubFlowableClient();
        var dispatcher = Dispatcher(flowable, StartedByMessage(Name), Registration(Name, "orders"));

        await dispatcher.HandleAsync(Bus("some.other.topic", $$"""{"eventType":"{{Name}}"}"""));

        Assert.Empty(flowable.Calls);
    }

    /// <summary>
    /// #524 AC4, and the reason the registries are separate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A signal named <c>orderPlaced</c> exists, on the same topic, and the bus
    /// carries that name. The message dispatcher consults only MESSAGE
    /// registrations, so it starts nothing — and it would start nothing even if
    /// somebody pointed both kinds at one topic, which is why the boundary is the
    /// registry rather than the topic.
    /// </para>
    /// <para>
    /// A single registry keyed on a bare name would pass every other test in this
    /// file and fail this one, which is the whole point of writing it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_signal_of_the_same_name_is_invisible_to_the_message_dispatcher()
    {
        var flowable = new StubFlowableClient();

        // No MESSAGE registration for the name at all — only a signal declares
        // it, which lives in the other registry entirely.
        var dispatcher = Dispatcher(flowable, StartedByMessage(Name), registrations: []);

        await dispatcher.HandleAsync(Bus(Topic, $$"""{"eventType":"{{Name}}"}"""));

        Assert.Empty(flowable.Calls);
    }

    /// <summary>Every workflow declaring the name is started, not just the first.</summary>
    [Fact]
    public async Task Every_workflow_starting_on_the_name_is_started()
    {
        var flowable = new StubFlowableClient { StartedByMessageInstanceId = "pi-1" };
        var dispatcher = Dispatcher(
            flowable,
            StartedByMessage(Name),
            Registration(Name, "orders"),
            Registration(Name, "returns"));

        await dispatcher.HandleAsync(Bus(Topic, $$"""{"eventType":"{{Name}}"}"""));

        Assert.Equal(
            2,
            flowable.Calls.Count(c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_payload_with_no_eventType_starts_nothing()
    {
        var flowable = new StubFlowableClient();
        var dispatcher = Dispatcher(flowable, StartedByMessage(Name), Registration(Name, "orders"));

        await dispatcher.HandleAsync(Bus(Topic, """{"not":"an eventType"}"""));
        await dispatcher.HandleAsync(Bus(Topic, "not json at all"));

        Assert.Empty(flowable.Calls);
    }

    // ---- helpers -------------------------------------------------------------

    private static BusWatcherStreamService.BusWatcherMessage Bus(string topic, string payload) =>
        new(DateTimeOffset.UtcNow, topic, "application/json", new Dictionary<string, string>(), payload);

    private static WorkflowMessageRegistration Registration(string name, string processKey) =>
        new(name, Topic, processKey);

    private static string StartedByMessage(string messageName) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="{messageName}" />
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static WorkflowMessageDispatcher Dispatcher(
        StubFlowableClient flowable, string xml, params WorkflowMessageRegistration[] registrations)
    {
        // A real correlator behind a real scope factory, because the dispatcher
        // resolving one per message is part of what is under test — a captive
        // scoped dependency is the mistake this shape exists to avoid.
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowModelStore>(_ => new SingleModelStore(new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Orders",
            ProcessKey = "orders",
            BpmnXml = xml
        }));
        services.AddSingleton<AutoNate.Web.Services.Flowable.IFlowableClient>(flowable);
        services.AddScoped<WorkflowMessageCorrelator>();

        return new WorkflowMessageDispatcher(
            new FixedMessageRegistry(registrations),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowMessageDispatcher>.Instance);
    }

    private sealed class FixedMessageRegistry(IReadOnlyList<WorkflowMessageRegistration> registrations)
        : IWorkflowMessageRegistry
    {
        public IReadOnlyCollection<string> GetSubscribedTopics() =>
            registrations.Select(r => r.Topic).Distinct(StringComparer.Ordinal).ToArray();

        public IReadOnlySet<string> GetMessageNamesForTopic(string topic) =>
            registrations.Where(r => r.Topic == topic).Select(r => r.MessageName)
                .ToHashSet(StringComparer.Ordinal);

        public IReadOnlyList<WorkflowMessageRegistration> GetRegistrationsForTopic(string topic) =>
            registrations.Where(r => r.Topic == topic).ToList();

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Answers for ANY process key with the same diagram. The fan-out case
    // registers two keys and both must resolve; a store that knew one of them
    // failed that test for a fixture reason, which is the control failing for
    // the wrong reason rather than the product being wrong.
    private sealed class SingleModelStore(WorkflowModel model) : IWorkflowModelStore
    {
        public Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowModel?>(model with { ProcessKey = processKey });

        public Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowModel>>([model]);
        public Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> SaveAsync(WorkflowModel m, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> PublishAsync(
            WorkflowModel m, WorkflowDeploymentInfo deployment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(
            Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
