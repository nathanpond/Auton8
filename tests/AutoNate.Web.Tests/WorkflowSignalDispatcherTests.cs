using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WorkflowSignalDispatcherTests
{
    [Fact]
    public async Task HandleAsync_BroadcastsSignal_WhenEventTypeMatches()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced", "orderId": 42 }"""));

        var broadcast = Assert.Single(stub.BroadcastedSignals);
        Assert.Equal("OrderPlaced", broadcast.SignalName);
        Assert.NotNull(broadcast.Variables);
        Assert.Equal("eventData", Assert.Single(broadcast.Variables!.Keys));
        var raw = Assert.IsType<string>(broadcast.Variables["eventData"]!);
        Assert.Contains("\"orderId\"", raw);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenEventTypeIsNotConfiguredForTopic()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "ShipmentDelivered" }"""));

        Assert.Empty(stub.BroadcastedSignals);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenTopicHasNoConfiguredSignals()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "unrelated.topic",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Empty(stub.BroadcastedSignals);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenPayloadIsMalformed()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: "not json"));

        Assert.Empty(stub.BroadcastedSignals);
    }

    [Fact]
    public async Task HandleAsync_NoOps_WhenEventTypeFieldIsMissing()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "orderId": 42 }"""));

        Assert.Empty(stub.BroadcastedSignals);
    }

    [Fact]
    public async Task HandleAsync_BroadcastsSeparately_ForMultipleSignalsOnSameTopic()
    {
        // Two signal start events listen on the same topic, but only the matching
        // eventType should be broadcasted — not all configured names.
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced", "OrderCancelled" }));

        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderCancelled" }"""));

        var broadcast = Assert.Single(stub.BroadcastedSignals);
        Assert.Equal("OrderCancelled", broadcast.SignalName);
    }

    [Fact]
    public async Task HandleAsync_SwallowsFlowableException()
    {
        var (dispatcher, stub) = CreateDispatcher(("orders.events", new[] { "OrderPlaced" }));
        stub.BroadcastSignalThrows = new InvalidOperationException("flowable down");

        // Should not propagate — dispatcher logs and continues so a single bad
        // publish can't tear down the bus subscriber loop.
        await dispatcher.HandleAsync(BuildMessage(
            topic: "orders.events",
            payload: """{ "eventType": "OrderPlaced" }"""));

        Assert.Single(stub.BroadcastedSignals);
    }

    private static (WorkflowSignalDispatcher Dispatcher, StubFlowableClient Stub) CreateDispatcher(
        params (string Topic, string[] SignalNames)[] entries)
    {
        // Existing dispatcher tests don't yet care about process keys or
        // record-type filtering — synthesize a single placeholder process key
        // per signal so each (topic, signalName) pair becomes one registration.
        var registrations = entries
            .SelectMany(entry => entry.SignalNames.Select(signalName =>
                new WorkflowSignalRegistration(
                    SignalName: signalName,
                    Topic: entry.Topic,
                    ProcessDefinitionKey: $"Flow_{signalName}",
                    RecordTypeShortCodes: new HashSet<string>(StringComparer.Ordinal))))
            .ToArray();

        var registry = new InMemorySignalRegistry();
        registry.Set(registrations);
        var stub = new StubFlowableClient();
        var dispatcher = new WorkflowSignalDispatcher(
            registry,
            stub,
            NullLogger<WorkflowSignalDispatcher>.Instance);
        return (dispatcher, stub);
    }

    private static BusWatcherStreamService.BusWatcherMessage BuildMessage(string topic, string payload)
    {
        return new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            topic,
            "application/json",
            new Dictionary<string, string>(),
            payload);
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
