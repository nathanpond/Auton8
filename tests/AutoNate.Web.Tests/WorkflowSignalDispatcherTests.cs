using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WorkflowSignalDispatcherTests
{
    [Fact]
    public async Task HandleAsync_StartsMatchingProcess_WhenEventTypeMatches()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced", "orderId": 42 }"""));

        var start = Assert.Single(stub.StartedProcesses);
        Assert.Equal("OrderFlow", start.ProcessDefinitionKey);
        Assert.NotNull(start.Variables);
        Assert.Equal("eventData", Assert.Single(start.Variables!.Keys));
        var raw = Assert.IsType<string>(start.Variables["eventData"]!);
        Assert.Contains("\"orderId\"", raw);
        Assert.Empty(stub.BroadcastedSignals);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenEventTypeIsNotConfiguredForTopic()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "ShipmentDelivered" }"""));

        Assert.Empty(stub.StartedProcesses);
        Assert.Empty(stub.SignalledExecutions);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenTopicHasNoConfiguredSignals()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "unrelated.topic",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Empty(stub.StartedProcesses);
        Assert.Empty(stub.SignalledExecutions);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenPayloadIsMalformed()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: "not json"));

        Assert.Empty(stub.StartedProcesses);
        Assert.Empty(stub.SignalledExecutions);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenEventTypeFieldIsMissing()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "orderId": 42 }"""));

        Assert.Empty(stub.StartedProcesses);
        Assert.Empty(stub.SignalledExecutions);
    }

    [Fact]
    public async Task HandleAsync_StartsOnlyMatchingRegistration_ForMultipleSignalsOnSameTopic()
    {
        // Two signal start events listen on the same topic — only the matching
        // eventType's registration should be started.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderPlacedFlow"),
            Reg("orders.events", "OrderCancelled", "OrderCancelledFlow"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderCancelled" }"""));

        var start = Assert.Single(stub.StartedProcesses);
        Assert.Equal("OrderCancelledFlow", start.ProcessDefinitionKey);
    }

    [Fact]
    public async Task HandleAsync_SwallowsFlowableException_OnStartProcess()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.StartProcessInstanceThrows = new InvalidOperationException("flowable down");

        // Should not propagate — dispatcher logs and continues so a single bad
        // start can't tear down the bus subscriber loop.
        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        // The stub records the call before throwing so we know the dispatcher
        // attempted the start and swallowed the exception.
        Assert.Single(stub.StartedProcesses);
    }

    [Fact]
    public async Task HandleAsync_StartsAllMatchingRegistrations_EvenWhenOneFails()
    {
        // Two start-event workflows listen on the same signal name. If the
        // first one throws the dispatcher must still attempt the second.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "FlowA"),
            Reg("orders.events", "OrderPlaced", "FlowB"));
        stub.StartProcessInstanceThrows = new InvalidOperationException("flowable down");

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Equal(
            new[] { "FlowA", "FlowB" },
            stub.StartedProcesses.Select(s => s.ProcessDefinitionKey).OrderBy(k => k));
    }

    // ── #243: the scope filter, which shipped with no test at all ───────────

    [Fact]
    public async Task HandleAsync_DoesNotWakeAnInstanceScopedCatchEvent()
    {
        // The whole point of #243, and it had no test — replacing the check with
        // `if (false)` left the suite at 13/13, which is how a guard that never
        // fired in production shipped green.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.AwaitingSignalExecutions.Add(("exec-scoped", "OrderFlow:1:aaa", "catchEvent"));
        stub.InstanceScopedSignals.Add("OrderPlaced");

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        // An instance-scoped signal exists to be raised from inside its own run.
        // Waking it from the bus wakes it in EVERY running instance, which is the
        // cross-instance leak #156 exists to prevent.
        Assert.DoesNotContain(stub.SignalledExecutions, s => s.ExecutionId == "exec-scoped");
    }

    [Fact]
    public async Task HandleAsync_WakesAGlobalCatchEvent()
    {
        // The complement. Without it, a filter that refused everything would pass
        // the test above while breaking every external signal there is.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.AwaitingSignalExecutions.Add(("exec-global", "OrderFlow:1:aaa", "catchEvent"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Contains(stub.SignalledExecutions, s => s.ExecutionId == "exec-global");
    }

    [Fact]
    public async Task HandleAsync_AsksAboutTheEventTheExecutionIsParkedAt()
    {
        // #243. Scope belongs to the signal an EVENT references, not to the name:
        // after #244 one definition can carry two roots of the same name, one
        // scoped and one not. Asking by name alone always found the unscoped one
        // and judged the scoped catch global, so the two fixes defeated each
        // other. The activity id is what makes the question exact.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.AwaitingSignalExecutions.Add(("exec-1", "OrderFlow:1:aaa", "theCatchEvent"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Contains("SignalScope:OrderFlow:1:aaa:OrderPlaced:theCatchEvent", stub.Calls);
    }

    [Fact]
    public async Task HandleAsync_PassesTheSignalNameToTheEngine()
    {
        // #262. The engine refuses a wake with no signal name, and the refusal was
        // swallowed by the per-execution catch below.
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.AwaitingSignalExecutions.Add(("exec-1", "OrderFlow:1:aaa", "catchEvent"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Contains("SignalExecution:exec-1:OrderPlaced", stub.Calls);
    }

    [Fact]
    public async Task HandleAsync_SignalsWaitingExecutions_WhenEventTypeMatches()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("orders.events", "OrderPlaced", "OrderFlow"));
        stub.WaitingExecutionsBySignal["OrderPlaced"] = new[] { "exec-1", "exec-2" };

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Equal(
            new[] { "exec-1", "exec-2" },
            stub.SignalledExecutions.Select(s => s.ExecutionId).OrderBy(s => s));
    }

    [Fact]
    public async Task HandleAsync_StartsWorkflow_WhenFilterMatchesPayloadShortCode()
    {
        var recordTypeId = Guid.NewGuid();
        var (dispatcher, stub, resolver) = CreateDispatcher(
            Reg("records.events", "RecordCreated", "AssetFlow", "asset"));
        resolver.ShortCodesById[recordTypeId] = "asset";

        await dispatcher.HandleAsync(BuildMessage(
            topic: "records.events",
            payload: $$"""{ "eventType": "RecordCreated", "recordTypeId": "{{recordTypeId}}" }"""));

        var start = Assert.Single(stub.StartedProcesses);
        Assert.Equal("AssetFlow", start.ProcessDefinitionKey);
    }

    [Fact]
    public async Task HandleAsync_SkipsWorkflow_WhenFilterDoesNotMatchPayloadShortCode()
    {
        var recordTypeId = Guid.NewGuid();
        var (dispatcher, stub, resolver) = CreateDispatcher(
            Reg("records.events", "RecordCreated", "AssetFlow", "asset"));
        resolver.ShortCodesById[recordTypeId] = "vehicle";

        await dispatcher.HandleAsync(BuildMessage(
            topic: "records.events",
            payload: $$"""{ "eventType": "RecordCreated", "recordTypeId": "{{recordTypeId}}" }"""));

        Assert.Empty(stub.StartedProcesses);
    }

    [Fact]
    public async Task HandleAsync_SkipsWorkflow_WhenFilterSetButPayloadHasNoRecordTypeId()
    {
        var (dispatcher, stub, _) = CreateDispatcher(
            Reg("records.events", "RecordCreated", "AssetFlow", "asset"));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "records.events",
            payload: """{ "eventType": "RecordCreated" }"""));

        Assert.Empty(stub.StartedProcesses);
    }

    [Fact]
    public async Task HandleAsync_StartsBothFilteredAndUnfilteredWorkflows_OnMatchingPayload()
    {
        var recordTypeId = Guid.NewGuid();
        var (dispatcher, stub, resolver) = CreateDispatcher(
            Reg("records.events", "RecordCreated", "AssetFlow", "asset"),
            Reg("records.events", "RecordCreated", "GenericFlow"));
        resolver.ShortCodesById[recordTypeId] = "asset";

        await dispatcher.HandleAsync(BuildMessage(
            topic: "records.events",
            payload: $$"""{ "eventType": "RecordCreated", "recordTypeId": "{{recordTypeId}}" }"""));

        Assert.Equal(
            new[] { "AssetFlow", "GenericFlow" },
            stub.StartedProcesses.Select(s => s.ProcessDefinitionKey).OrderBy(k => k));
    }

    private static (WorkflowSignalDispatcher Dispatcher, StubFlowableClient Stub, FakeRecordTypeResolver Resolver) CreateDispatcher(
        params WorkflowSignalRegistration[] registrations)
    {
        var registry = new InMemorySignalRegistry();
        registry.Set(registrations);
        var stub = new StubFlowableClient();
        var resolver = new FakeRecordTypeResolver();
        var dispatcher = new WorkflowSignalDispatcher(
            registry,
            stub,
            resolver,
            NullLogger<WorkflowSignalDispatcher>.Instance);
        return (dispatcher, stub, resolver);
    }

    private static WorkflowSignalRegistration Reg(
        string topic, string signalName, string processKey,
        params string[] shortCodes) =>
        new(signalName, topic, processKey,
            new HashSet<string>(shortCodes, StringComparer.Ordinal));

    private static BusWatcherStreamService.BusWatcherMessage BuildMessage(string topic, string payload)
    {
        return new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            topic,
            "application/json",
            new Dictionary<string, string>(),
            payload);
    }

    private sealed class FakeRecordTypeResolver : IRecordTypeShortCodeResolver
    {
        public Dictionary<Guid, string> ShortCodesById { get; } = new();

        public bool TryGetShortCode(Guid recordTypeId, out string shortCode)
        {
            if (ShortCodesById.TryGetValue(recordTypeId, out var v))
            {
                shortCode = v;
                return true;
            }
            shortCode = string.Empty;
            return false;
        }

        // Dispatcher tests only consult TryGetShortCode; the inverse map is
        // here just to satisfy the interface.
        public IReadOnlyDictionary<string, Guid> ShortCodeToId =>
            ShortCodesById.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
    }

    private sealed class InMemorySignalRegistry : IWorkflowSignalRegistry
    {
        private static readonly IReadOnlySet<string> EmptyNames =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly IReadOnlyList<WorkflowSignalRegistration> EmptyRegs =
            Array.Empty<WorkflowSignalRegistration>();

        private Dictionary<string, IReadOnlySet<string>> _names = new(StringComparer.Ordinal);
        private Dictionary<string, IReadOnlyList<WorkflowSignalRegistration>> _regs = new(StringComparer.Ordinal);

        public void Set(params WorkflowSignalRegistration[] registrations)
        {
            _names = registrations
                .GroupBy(r => r.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlySet<string>)new HashSet<string>(
                        group.Select(r => r.SignalName), StringComparer.Ordinal),
                    StringComparer.Ordinal);

            _regs = registrations
                .GroupBy(r => r.Topic, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<WorkflowSignalRegistration>)group.ToList().AsReadOnly(),
                    StringComparer.Ordinal);
        }

        public IReadOnlyCollection<string> GetSubscribedTopics() => _names.Keys.ToArray();

        public IReadOnlySet<string> GetSignalNamesForTopic(string topic) =>
            _names.TryGetValue(topic, out var names) ? names : EmptyNames;

        public IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic) =>
            _regs.TryGetValue(topic, out var registrations) ? registrations : EmptyRegs;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
