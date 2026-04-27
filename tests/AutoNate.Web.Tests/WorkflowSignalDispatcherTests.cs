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
        var registry = new InMemorySignalRegistry();
        registry.Set(entries);
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
        private static readonly IReadOnlySet<string> EmptySet =
            new HashSet<string>(StringComparer.Ordinal);

        private Dictionary<string, IReadOnlySet<string>> _byTopic =
            new(StringComparer.Ordinal);

        public void Set(params (string Topic, string[] SignalNames)[] entries)
        {
            _byTopic = entries.ToDictionary(
                entry => entry.Topic,
                entry => (IReadOnlySet<string>)new HashSet<string>(entry.SignalNames, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        public IReadOnlyCollection<string> GetSubscribedTopics() => _byTopic.Keys.ToArray();

        public IReadOnlySet<string> GetSignalNamesForTopic(string topic) =>
            _byTopic.TryGetValue(topic, out var names) ? names : EmptySet;

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
