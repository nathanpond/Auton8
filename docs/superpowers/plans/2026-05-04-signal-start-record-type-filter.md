# Signal Start Event — Record Type Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional record-type filter to BPMN signal start events so a workflow only starts when the inbound payload's `recordTypeId` matches one of the configured record types. Empty filter preserves existing "fire on every payload" behavior.

**Architecture:** The Flowable signal **broadcast** path is replaced with per-process-key dispatch driven by the registry. The registry now records `(SignalName, Topic, ProcessDefinitionKey, RecordTypeShortCodes)` per workflow. The dispatcher resolves the payload's `recordTypeId` to a shortcode via a small cache, then iterates matching registrations and calls `StartProcessInstanceAsync` for each whose filter passes. Intermediate signal catches get a parallel "signal each waiting execution" path. The studio modal grows a multi-select hidden behind a `carriesRecordType` flag on the EventCatalog.

**Tech Stack:** .NET 8 (AutoNate.Web), EF Core (Postgres), xUnit, React 18 + TypeScript SPA, bpmn-js with bpmn-moddle, Flowable REST.

**Spec:** `docs/superpowers/specs/2026-05-04-signal-start-record-type-filter-design.md`

---

## File Inventory

| File | Disposition |
| --- | --- |
| `src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs` | Modify — add `RecordTypeShortCodes` to snapshot; expand `WorkflowSignalRegistration` record. |
| `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs` | Modify — read/write `flowable:recordTypeShortCodes`; add validation rules; populate process key + filter in extractor. |
| `src/AutoNate.Web/Services/Workflow/IWorkflowSignalRegistry.cs` | Modify — add `GetRegistrationsForTopic`. |
| `src/AutoNate.Web/Services/Workflow/EfCoreWorkflowSignalRegistry.cs` | Modify — implement new method; cache full registration objects. |
| `src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs` | Modify — replace broadcast with per-key dispatch; add filter evaluation. |
| `src/AutoNate.Web/Services/Flowable/IFlowableClient.cs` | Modify — add `SignalExecutionAsync` for the intermediate-catch path. |
| `src/AutoNate.Web/Services/Flowable/FlowableClient.cs` | Modify — implement `SignalExecutionAsync`. |
| `src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs` | Create — Guid → ShortCode lookup with audit-event invalidation. |
| `src/AutoNate.Web/Services/Events/EventCatalog.cs` | Modify — add `CarriesRecordType` to entry record; set `true` on six `record.events` entries. |
| `src/AutoNate.Web/Endpoints/EventCatalogEndpoints.cs` | Modify — surface the new flag in the API response (find file via grep at task time). |
| `src/AutoNate.Web/Program.cs` (or DI module) | Modify — register `RecordTypeShortCodeCache`. |
| `src/AutoNate.Spa/src/lib/bpmn/workflow.js` | Modify — `describeSignalStartEvent`/`updateSignalStartEventProperties` handle `recordTypeShortCodes`. |
| `src/AutoNate.Spa/src/api/workflows.ts` | Modify — mirror new `recordTypeShortCodes` field on `WorkflowElementSnapshot`. |
| `src/AutoNate.Spa/src/hooks/useEventCatalog.ts` | Modify — surface `carriesRecordType` from server response (find via grep at task time). |
| `src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx` | Modify — `SignalStartEventEditor` type; modal field; conditional visibility; mid-edit warning + strip-on-apply. |
| `tests/AutoNate.Web.Tests/StubFlowableClient.cs` | Modify — capture `SignalExecutionAsync` and per-key starts. |
| `tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs` | Modify — replace broadcast assertions with per-key assertions; add filter cases. |
| `tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs` | Modify — round-trip the new attribute; validation cases. |
| `tests/AutoNate.Web.Tests/EfCoreWorkflowSignalRegistryTests.cs` | Create or modify — exercise `GetRegistrationsForTopic` (search to confirm filename). |
| `tests/AutoNate.Web.Tests/RecordTypeShortCodeCacheTests.cs` | Create. |

---

## Phase 1 — Per-key dispatch (behavior-preserving refactor)

### Task 1: Expand `WorkflowSignalRegistration`

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs`

- [ ] **Step 1: Update the registration record**

Replace lines 24-27 with:

```csharp
// Pair extracted from a published workflow's BPMN XML: a signal start event's
// signal name (matched against the inbound message's `eventType`) and the Dapr
// pub/sub topic the bus subscriber should listen on for that signal. The
// process definition key identifies the workflow to start; the
// RecordTypeShortCodes set is empty for unfiltered registrations and contains
// shortcodes the payload's `recordTypeId` must resolve to for filtered ones.
public sealed record class WorkflowSignalRegistration(
    string SignalName,
    string Topic,
    string ProcessDefinitionKey,
    IReadOnlySet<string> RecordTypeShortCodes);
```

- [ ] **Step 2: Build (compile errors expected — they cascade through extractor and registry; we will fix in tasks 2-3)**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj
```

Expected: build fails in `WorkflowBpmnXml.cs` and `EfCoreWorkflowSignalRegistry.cs` because the constructor now takes 4 args. **Do not commit yet.**

---

### Task 2: Populate process key + empty filter in extractor

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs:613-672`
- Modify: `tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs`

- [ ] **Step 1: Write a failing test for process-key extraction**

Add to `WorkflowBpmnXmlTests.cs`:

```csharp
[Fact]
public void ExtractSignalRegistrations_PopulatesProcessDefinitionKey()
{
    var xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <signal id="Signal_1" name="record.created" flowable:topic="record.events"/>
          <process id="OrderFlow">
            <startEvent id="StartEvent_1">
              <signalEventDefinition signalRef="Signal_1"/>
            </startEvent>
          </process>
        </definitions>
        """;

    var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

    var registration = Assert.Single(registrations);
    Assert.Equal("record.created", registration.SignalName);
    Assert.Equal("record.events", registration.Topic);
    Assert.Equal("OrderFlow", registration.ProcessDefinitionKey);
    Assert.Empty(registration.RecordTypeShortCodes);
}
```

- [ ] **Step 2: Run the test — expect compile error**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ExtractSignalRegistrations_PopulatesProcessDefinitionKey"
```

Expected: build fails (registration record signature doesn't match the test, and old call sites still pass 2 args).

- [ ] **Step 3: Update the extractor**

Replace `ExtractSignalRegistrations` body (`WorkflowBpmnXml.cs:613-672`) so the `foreach` reads the enclosing `<process id>` and constructs the new record:

```csharp
public static IReadOnlyList<WorkflowSignalRegistration> ExtractSignalRegistrations(string xml)
{
    if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<WorkflowSignalRegistration>();

    XDocument document;
    try { document = XDocument.Parse(xml); }
    catch (Exception) { return Array.Empty<WorkflowSignalRegistration>(); }

    var signalsById = document.Root?
        .Elements(BpmnNamespace + "signal")
        .Where(s => !string.IsNullOrWhiteSpace(s.Attribute("id")?.Value))
        .ToDictionary(s => s.Attribute("id")!.Value, s => s, StringComparer.Ordinal)
        ?? new Dictionary<string, XElement>(StringComparer.Ordinal);

    var registrations = new Dictionary<(string Name, string Topic, string ProcessKey), WorkflowSignalRegistration>();

    foreach (var startEvent in document.Descendants(BpmnNamespace + "startEvent"))
    {
        var signalEventDefinition = startEvent.Element(BpmnNamespace + "signalEventDefinition");
        if (signalEventDefinition is null) continue;

        var signalRef = signalEventDefinition.Attribute("signalRef")?.Value;
        if (string.IsNullOrWhiteSpace(signalRef) || !signalsById.TryGetValue(signalRef, out var signal))
            continue;

        var name = signal.Attribute("name")?.Value?.Trim();
        if (string.IsNullOrEmpty(name)) continue;

        var topic = signal.Attribute(FlowableNamespace + "topic")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(topic)) topic = DefaultSignalTopic;

        // Walk up to the enclosing <process id="..."> — that is the
        // processDefinitionKey Flowable will use when starting an instance.
        var processElement = startEvent.Ancestors(BpmnNamespace + "process").FirstOrDefault();
        var processKey = processElement?.Attribute("id")?.Value?.Trim();
        if (string.IsNullOrEmpty(processKey)) continue;

        var shortCodesAttr = signalEventDefinition.Attribute(FlowableNamespace + "recordTypeShortCodes")?.Value;
        var shortCodes = ParseShortCodeList(shortCodesAttr);

        var key = (name, topic, processKey);
        registrations.TryAdd(key, new WorkflowSignalRegistration(name, topic, processKey, shortCodes));
    }

    return registrations.Values.ToArray();
}

private static IReadOnlySet<string> ParseShortCodeList(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return EmptyShortCodeSet;
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        set.Add(token);
    }
    return set;
}

private static readonly IReadOnlySet<string> EmptyShortCodeSet =
    new HashSet<string>(StringComparer.Ordinal);
```

- [ ] **Step 4: Run the test — expect pass**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ExtractSignalRegistrations_PopulatesProcessDefinitionKey"
```

Expected: PASS. Other extractor tests may still fail because they assert pre-existing behavior with the 2-arg constructor — fix those next.

- [ ] **Step 5: Fix any failing extractor tests by adapting their assertions**

Run the full file:

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~WorkflowBpmnXmlTests"
```

For each failure: assertions previously checking `(SignalName, Topic)` should add `ProcessDefinitionKey` (the test fixtures' `<process id>`) and `Empty(RecordTypeShortCodes)`. Do not change behavior — just align expectations.

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs \
        src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs \
        tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs
git commit -m "Extract process key and (empty) record-type filter on signal registrations"
```

---

### Task 3: Add `GetRegistrationsForTopic` to the registry

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/IWorkflowSignalRegistry.cs`
- Modify: `src/AutoNate.Web/Services/Workflow/EfCoreWorkflowSignalRegistry.cs`

- [ ] **Step 1: Write a failing registry test**

Find or create `tests/AutoNate.Web.Tests/EfCoreWorkflowSignalRegistryTests.cs`. Add:

```csharp
[Fact]
public async Task GetRegistrationsForTopic_ReturnsRegistrationsExtractedFromPublishedWorkflows()
{
    using var fixture = await EfTestFixture.CreateAsync();
    fixture.SeedPublishedWorkflow("OrderFlow", """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <signal id="S" name="record.created" flowable:topic="record.events"/>
          <process id="OrderFlow">
            <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
          </process>
        </definitions>
        """);

    var registry = fixture.Registry;
    await registry.RefreshAsync();

    var registrations = registry.GetRegistrationsForTopic("record.events");
    var registration = Assert.Single(registrations);
    Assert.Equal("OrderFlow", registration.ProcessDefinitionKey);
    Assert.Empty(registration.RecordTypeShortCodes);
}
```

(If `EfTestFixture` doesn't exist, mirror the harness from `EfCoreWorkflowModelStoreTests.cs`.)

- [ ] **Step 2: Run the test — expect compile error**

Method doesn't exist on the interface yet.

- [ ] **Step 3: Add the interface method**

Edit `IWorkflowSignalRegistry.cs`:

```csharp
public interface IWorkflowSignalRegistry
{
    IReadOnlyCollection<string> GetSubscribedTopics();
    IReadOnlySet<string> GetSignalNamesForTopic(string topic);
    IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic);
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement on the EF-Core registry**

Edit `EfCoreWorkflowSignalRegistry.cs`:

```csharp
private static readonly IReadOnlyList<WorkflowSignalRegistration> EmptyRegistrations =
    Array.Empty<WorkflowSignalRegistration>();

private IReadOnlyDictionary<string, IReadOnlyList<WorkflowSignalRegistration>> _registrationsByTopic =
    new Dictionary<string, IReadOnlyList<WorkflowSignalRegistration>>(StringComparer.Ordinal);

public IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic) =>
    _registrationsByTopic.TryGetValue(topic, out var list) ? list : EmptyRegistrations;
```

In `RefreshAsync`, build both indexes from one pass:

```csharp
var byTopicNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
var byTopicRegs = new Dictionary<string, List<WorkflowSignalRegistration>>(StringComparer.Ordinal);

foreach (var xml in publishedXmls)
{
    foreach (var registration in WorkflowBpmnXml.ExtractSignalRegistrations(xml))
    {
        if (!byTopicNames.TryGetValue(registration.Topic, out var names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            byTopicNames[registration.Topic] = names;
        }
        names.Add(registration.SignalName);

        if (!byTopicRegs.TryGetValue(registration.Topic, out var regs))
        {
            regs = new List<WorkflowSignalRegistration>();
            byTopicRegs[registration.Topic] = regs;
        }
        regs.Add(registration);
    }
}

_byTopic = byTopicNames.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value, StringComparer.Ordinal);
_registrationsByTopic = byTopicRegs.ToDictionary(
    p => p.Key,
    p => (IReadOnlyList<WorkflowSignalRegistration>)p.Value.AsReadOnly(),
    StringComparer.Ordinal);
```

- [ ] **Step 5: Update the in-memory registry stub used by dispatcher tests**

`tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs:129-151` — extend `InMemorySignalRegistry` to track and return `WorkflowSignalRegistration` objects:

```csharp
private sealed class InMemorySignalRegistry : IWorkflowSignalRegistry
{
    private static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlyList<WorkflowSignalRegistration> EmptyRegs = Array.Empty<WorkflowSignalRegistration>();

    private Dictionary<string, IReadOnlySet<string>> _names = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<WorkflowSignalRegistration>> _regs = new(StringComparer.Ordinal);

    public void Set(params WorkflowSignalRegistration[] registrations)
    {
        _names = registrations.GroupBy(r => r.Topic, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)new HashSet<string>(g.Select(r => r.SignalName), StringComparer.Ordinal),
                StringComparer.Ordinal);
        _regs = registrations.GroupBy(r => r.Topic, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<WorkflowSignalRegistration>)g.ToList().AsReadOnly(),
                StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> GetSubscribedTopics() => _names.Keys.ToArray();
    public IReadOnlySet<string> GetSignalNamesForTopic(string topic) =>
        _names.TryGetValue(topic, out var v) ? v : EmptyNames;
    public IReadOnlyList<WorkflowSignalRegistration> GetRegistrationsForTopic(string topic) =>
        _regs.TryGetValue(topic, out var v) ? v : EmptyRegs;
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

The existing `Set(params (string Topic, string[] SignalNames)[])` overload is no longer used by the rewritten dispatcher tests — keep it (or delete it after Task 6 lands). Adapt the existing `CreateDispatcher` helper accordingly.

- [ ] **Step 6: Run all tests and confirm green (still pre-dispatcher-rewrite, so dispatcher tests should still pass)**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
```

- [ ] **Step 7: Commit**

```bash
git add src/AutoNate.Web/Services/Workflow/IWorkflowSignalRegistry.cs \
        src/AutoNate.Web/Services/Workflow/EfCoreWorkflowSignalRegistry.cs \
        tests/AutoNate.Web.Tests/EfCoreWorkflowSignalRegistryTests.cs \
        tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs
git commit -m "Index full signal registrations by topic, expose GetRegistrationsForTopic"
```

---

### Task 4: Add `SignalExecutionAsync` to the Flowable client

**Files:**
- Modify: `src/AutoNate.Web/Services/Flowable/IFlowableClient.cs`
- Modify: `src/AutoNate.Web/Services/Flowable/FlowableClient.cs`
- Modify: `tests/AutoNate.Web.Tests/StubFlowableClient.cs`
- Modify: `tests/AutoNate.Web.Tests/FlowableClientTests.cs`

- [ ] **Step 1: Write a failing test**

In `FlowableClientTests.cs`:

```csharp
[Fact]
public async Task SignalExecutionAsync_PutsSignalEventReceivedActionWithVariables()
{
    var (client, handler) = CreateClient();
    handler.Respond(HttpStatusCode.OK, """{"id":"exec-1"}""");

    await client.SignalExecutionAsync(
        executionId: "exec-1",
        variables: new Dictionary<string, object?> { ["eventData"] = "{\"x\":1}" });

    var request = handler.LastRequest!;
    Assert.Equal(HttpMethod.Put, request.Method);
    Assert.EndsWith("/runtime/executions/exec-1", request.RequestUri!.AbsolutePath);
    var body = await request.Content!.ReadAsStringAsync();
    Assert.Contains("\"action\":\"signalEventReceived\"", body);
    Assert.Contains("\"eventData\"", body);
}
```

- [ ] **Step 2: Run — expect compile error (method doesn't exist)**

```bash
dotnet test --filter "FullyQualifiedName~SignalExecutionAsync"
```

- [ ] **Step 3: Add to interface**

`IFlowableClient.cs` (next to `BroadcastSignalAsync`):

```csharp
// Wakes a single waiting execution (intermediate signal catch). Used by the
// dispatcher's per-execution path that replaces broadcast for non-start signal
// subscriptions.
Task SignalExecutionAsync(
    string executionId,
    IReadOnlyDictionary<string, object?>? variables = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in `FlowableClient.cs`**

After `BroadcastSignalAsync` (line ~1067):

```csharp
public async Task SignalExecutionAsync(
    string executionId,
    IReadOnlyDictionary<string, object?>? variables = null,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(executionId))
        throw new ArgumentException("Execution id is required.", nameof(executionId));

    var payload = new Dictionary<string, object?>
    {
        ["action"] = "signalEventReceived",
        ["variables"] = ToFlowableVariables(variables)
    };

    using var response = await _httpClient.PutAsJsonAsync(
        $"service/runtime/executions/{Uri.EscapeDataString(executionId)}",
        payload,
        cancellationToken);

    await EnsureSuccessAsync(response, $"signal execution '{executionId}'");
}
```

- [ ] **Step 5: Stub it for the dispatcher tests**

In `StubFlowableClient.cs`, mirror the broadcast capture pattern. Add:

```csharp
public List<(string ExecutionId, IReadOnlyDictionary<string, object?>? Variables)> SignalledExecutions { get; } = new();

public Task SignalExecutionAsync(
    string executionId,
    IReadOnlyDictionary<string, object?>? variables = null,
    CancellationToken cancellationToken = default)
{
    SignalledExecutions.Add((executionId, variables));
    return Task.CompletedTask;
}
```

Also add a way to query waiting executions. Look up how the stub already handles things like `StartProcessInstanceAsync`; we'll need to stub `ListExecutionsBySignalSubscriptionAsync` too — see Task 5 for the dispatcher's call site.

- [ ] **Step 6: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~SignalExecutionAsync"
```

- [ ] **Step 7: Commit**

```bash
git add src/AutoNate.Web/Services/Flowable/ tests/AutoNate.Web.Tests/StubFlowableClient.cs tests/AutoNate.Web.Tests/FlowableClientTests.cs
git commit -m "Add SignalExecutionAsync for per-execution signal delivery"
```

---

### Task 5: Add `ListExecutionsBySignalSubscriptionAsync` to the Flowable client

**Files:**
- Modify: `src/AutoNate.Web/Services/Flowable/IFlowableClient.cs`
- Modify: `src/AutoNate.Web/Services/Flowable/FlowableClient.cs`
- Modify: `tests/AutoNate.Web.Tests/StubFlowableClient.cs`
- Modify: `tests/AutoNate.Web.Tests/FlowableClientTests.cs`

The dispatcher's intermediate-catch path needs to know which executions are waiting on a given signal. Flowable exposes this via `GET /runtime/executions?signalEventSubscriptionName=<name>`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task ListExecutionsBySignalSubscriptionAsync_ParsesIdsFromResponse()
{
    var (client, handler) = CreateClient();
    handler.Respond(HttpStatusCode.OK, """
        { "data": [{"id":"exec-1"},{"id":"exec-2"}], "total": 2 }
        """);

    var ids = await client.ListExecutionsBySignalSubscriptionAsync("record.created");

    Assert.Equal(new[] { "exec-1", "exec-2" }, ids);
    Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    Assert.Contains("signalEventSubscriptionName=record.created", handler.LastRequest.RequestUri!.Query);
}
```

- [ ] **Step 2: Run — expect compile failure**

```bash
dotnet test --filter "FullyQualifiedName~ListExecutionsBySignalSubscriptionAsync"
```

- [ ] **Step 3: Implement**

`IFlowableClient.cs`:

```csharp
Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
    string signalName,
    CancellationToken cancellationToken = default);
```

`FlowableClient.cs`:

```csharp
public async Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
    string signalName,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(signalName))
        return Array.Empty<string>();

    using var response = await _httpClient.GetAsync(
        $"service/runtime/executions?signalEventSubscriptionName={Uri.EscapeDataString(signalName)}",
        cancellationToken);

    await EnsureSuccessAsync(response, $"list executions waiting on '{signalName}'");

    var page = await DeserializeAsync<FlowableExecutionListResponse>(response, cancellationToken);
    if (page?.Data is null) return Array.Empty<string>();
    return page.Data
        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
        .Select(item => item.Id!)
        .ToArray();
}

private sealed class FlowableExecutionListResponse
{
    public List<FlowableExecutionListItem>? Data { get; set; }
}

private sealed class FlowableExecutionListItem
{
    public string? Id { get; set; }
}
```

(Place response classes near other Flowable DTOs in the file.)

- [ ] **Step 4: Stub**

`StubFlowableClient.cs`:

```csharp
public Dictionary<string, IReadOnlyList<string>> WaitingExecutionsBySignal { get; } = new(StringComparer.Ordinal);

public Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
    string signalName,
    CancellationToken cancellationToken = default)
{
    return Task.FromResult(WaitingExecutionsBySignal.TryGetValue(signalName, out var v)
        ? v
        : (IReadOnlyList<string>)Array.Empty<string>());
}
```

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~ListExecutionsBySignalSubscriptionAsync"
```

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Web/Services/Flowable/ tests/AutoNate.Web.Tests/StubFlowableClient.cs tests/AutoNate.Web.Tests/FlowableClientTests.cs
git commit -m "Add ListExecutionsBySignalSubscriptionAsync for intermediate-catch routing"
```

---

### Task 6: Rewrite `WorkflowSignalDispatcher` to per-key dispatch (no filter yet)

**Files:**
- Modify: `src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs`
- Modify: `tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs`

This is the critical refactor. After this task, `BroadcastSignalAsync` is no longer called by production code — but every published workflow still has an empty `RecordTypeShortCodes` set, so behavior is preserved.

- [ ] **Step 1: Update existing dispatcher tests to assert per-key starts**

Replace each `Assert.Single(stub.BroadcastedSignals)`-style assertion with:

```csharp
var start = Assert.Single(stub.StartedProcesses);
Assert.Equal("OrderFlow", start.ProcessDefinitionKey);
Assert.NotNull(start.Variables);
Assert.Equal("eventData", Assert.Single(start.Variables!.Keys));
```

For the existing `HandleAsync_BroadcastsSeparately_ForMultipleSignalsOnSameTopic` test, switch the assertion to count `StartedProcesses`. Update the `CreateDispatcher` helper to accept `WorkflowSignalRegistration` arrays:

```csharp
private static (WorkflowSignalDispatcher Dispatcher, StubFlowableClient Stub) CreateDispatcher(
    params WorkflowSignalRegistration[] registrations)
{
    var registry = new InMemorySignalRegistry();
    registry.Set(registrations);
    var stub = new StubFlowableClient();
    var dispatcher = new WorkflowSignalDispatcher(
        registry,
        stub,
        NullLogger<WorkflowSignalDispatcher>.Instance);
    return (dispatcher, stub);
}

private static WorkflowSignalRegistration Reg(
    string topic, string signalName, string processKey,
    params string[] shortCodes) =>
    new(signalName, topic, processKey,
        new HashSet<string>(shortCodes, StringComparer.Ordinal));
```

Then call sites become:

```csharp
var (dispatcher, stub) = CreateDispatcher(
    Reg("orders.events", "OrderPlaced", "OrderFlow"));
```

- [ ] **Step 2: Add a new failing test for the intermediate-catch path**

```csharp
[Fact]
public async Task HandleAsync_SignalsWaitingExecutions_WhenEventTypeMatches()
{
    var (dispatcher, stub) = CreateDispatcher(Reg("orders.events", "OrderPlaced", "OrderFlow"));
    stub.WaitingExecutionsBySignal["OrderPlaced"] = new[] { "exec-1", "exec-2" };

    await dispatcher.HandleAsync(BuildMessage(
        topic: "orders.events",
        payload: """{ "eventType": "OrderPlaced" }"""));

    Assert.Equal(new[] { "exec-1", "exec-2" },
        stub.SignalledExecutions.Select(s => s.ExecutionId).OrderBy(s => s));
}
```

- [ ] **Step 3: Run — both rewritten and new tests should fail**

```bash
dotnet test --filter "FullyQualifiedName~WorkflowSignalDispatcherTests"
```

Expected: many failures because the dispatcher still calls `BroadcastSignalAsync`.

- [ ] **Step 4: Rewrite `WorkflowSignalDispatcher.HandleAsync`**

```csharp
public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message)
{
    var registrations = _registry.GetRegistrationsForTopic(message.Topic);
    if (registrations.Count == 0) return;

    var eventType = TryReadEventType(message.Payload);
    if (eventType is null)
    {
        _logger.LogWarning(
            "Discarding bus message on topic {Topic}: payload is missing or malformed `eventType` field.",
            message.Topic);
        return;
    }

    var matching = registrations
        .Where(r => string.Equals(r.SignalName, eventType, StringComparison.Ordinal))
        .ToArray();
    if (matching.Length == 0) return;

    // Start one process per matching registration.
    foreach (var registration in matching)
    {
        try
        {
            await _flowableClient.StartProcessInstanceAsync(
                registration.ProcessDefinitionKey,
                variables: new Dictionary<string, object?> { ["eventData"] = message.Payload });

            _logger.LogInformation(
                "Started workflow {ProcessDefinitionKey} from signal '{SignalName}' on topic {Topic}.",
                registration.ProcessDefinitionKey, eventType, message.Topic);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to start workflow {ProcessDefinitionKey} from signal '{SignalName}' on topic {Topic}.",
                registration.ProcessDefinitionKey, eventType, message.Topic);
        }
    }

    // Wake any waiting intermediate-catch executions on this signal.
    IReadOnlyList<string> waitingExecutionIds;
    try
    {
        waitingExecutionIds = await _flowableClient
            .ListExecutionsBySignalSubscriptionAsync(eventType);
    }
    catch (Exception exception)
    {
        _logger.LogError(
            exception,
            "Failed to list executions waiting on signal '{SignalName}'. Skipping intermediate-catch dispatch.",
            eventType);
        return;
    }

    foreach (var executionId in waitingExecutionIds)
    {
        try
        {
            await _flowableClient.SignalExecutionAsync(
                executionId,
                new Dictionary<string, object?> { ["eventData"] = message.Payload });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to signal execution {ExecutionId} for signal '{SignalName}'.",
                executionId, eventType);
        }
    }
}
```

The `TryReadEventType` private method stays unchanged.

- [ ] **Step 5: Run — expect green**

```bash
dotnet test --filter "FullyQualifiedName~WorkflowSignalDispatcherTests"
```

- [ ] **Step 6: Run the whole test suite (regression sweep)**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
```

Expected: green. Phase 1 complete — `BroadcastSignalAsync` is no longer on the dispatch path; behavior is preserved because every registration has an empty filter set.

- [ ] **Step 7: Commit**

```bash
git add src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs \
        tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs
git commit -m "Replace signal broadcast with per-key dispatch and waiting-execution signalling"
```

---

## Phase 2 — Filter capability (backend)

### Task 7: Round-trip `flowable:recordTypeShortCodes` in BPMN apply

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs`
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs:516-544`
- Modify: `tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs`

The studio submits a `WorkflowElementSnapshot` per element on save. The signal-start branch (`ApplySignalStartEventSnapshot`) currently writes `signalRef` and the `<signal>` root. It must additionally write `flowable:recordTypeShortCodes` on the `<signalEventDefinition>`.

- [ ] **Step 1: Add the field to the snapshot**

Update the snapshot record (line 3-22):

```csharp
public sealed record class WorkflowElementSnapshot(
    string Id,
    string Type,
    string? Name,
    string? ScriptFormat = null,
    string? Script = null,
    string? ResultVariable = null,
    string? ConditionExpression = null,
    string? Assignee = null,
    IReadOnlyList<string>? CandidateUsers = null,
    IReadOnlyList<string>? CandidateGroups = null,
    string? DueDate = null,
    string? SignalName = null,
    string? SignalTopic = null,
    IReadOnlyList<string>? RecordTypeShortCodes = null,  // NEW
    string? TimerCycleCron = null,
    string? TimerEndDate = null,
    string? TimerDuration = null,
    string? TimerDate = null,
    string? ServiceTaskKind = null,
    string? BehaviorKey = null);
```

- [ ] **Step 2: Failing test for round-trip**

In `WorkflowBpmnXmlTests.cs`:

```csharp
[Fact]
public void ApplySignalStartEventSnapshot_WritesAndReadsRecordTypeShortCodes()
{
    var initial = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <signal id="S" name="record.created" flowable:topic="record.events"/>
          <process id="OrderFlow">
            <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
          </process>
        </definitions>
        """;

    var snapshot = new WorkflowElementSnapshot(
        Id: "SE", Type: "bpmn:StartEvent", Name: null,
        SignalName: "record.created",
        SignalTopic: "record.events",
        RecordTypeShortCodes: new[] { "asset", "vehicle" });

    var updated = WorkflowBpmnXml.ApplyElementSnapshots(initial, new[] { snapshot });

    Assert.Contains("flowable:recordTypeShortCodes=\"asset,vehicle\"", updated);

    var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(updated);
    var registration = Assert.Single(registrations);
    Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "asset", "vehicle" },
        registration.RecordTypeShortCodes);
}

[Fact]
public void ApplySignalStartEventSnapshot_OmitsAttribute_WhenFilterEmpty()
{
    var initial = /* same as above but with the attribute pre-set */;

    var snapshot = new WorkflowElementSnapshot(
        Id: "SE", Type: "bpmn:StartEvent", Name: null,
        SignalName: "record.created",
        SignalTopic: "record.events",
        RecordTypeShortCodes: Array.Empty<string>());

    var updated = WorkflowBpmnXml.ApplyElementSnapshots(initial, new[] { snapshot });

    Assert.DoesNotContain("flowable:recordTypeShortCodes", updated);
}
```

- [ ] **Step 3: Run — expect failure**

```bash
dotnet test --filter "FullyQualifiedName~ApplySignalStartEventSnapshot_WritesAndReads OR FullyQualifiedName~ApplySignalStartEventSnapshot_Omits"
```

- [ ] **Step 4: Implement in `ApplySignalStartEventSnapshot`**

Add at the end of `ApplySignalStartEventSnapshot` (after line 543), before the closing `}`:

```csharp
// Per-event record-type filter. Empty/null clears the attribute (preserves
// "match all records" behavior). Non-empty writes a comma-joined list.
var shortCodes = snapshot.RecordTypeShortCodes;
if (shortCodes is null || shortCodes.Count == 0)
{
    signalEventDefinition.SetAttributeValue(
        FlowableNamespace + "recordTypeShortCodes", null);
}
else
{
    var normalized = string.Join(",", shortCodes
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Trim()));
    signalEventDefinition.SetAttributeValue(
        FlowableNamespace + "recordTypeShortCodes",
        string.IsNullOrEmpty(normalized) ? null : normalized);
}
```

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~WorkflowBpmnXmlTests"
```

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs \
        src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs \
        tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs
git commit -m "Round-trip flowable:recordTypeShortCodes on signal start events"
```

---

### Task 8: Validation — hard error when attribute is on non-startEvent

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs` (`Validate`)
- Modify: `tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void Validate_ReturnsError_WhenRecordTypeFilterAppearsOnIntermediateCatch()
{
    var xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <signal id="S" name="record.created" flowable:topic="record.events"/>
          <process id="OrderFlow">
            <startEvent id="Start"/>
            <intermediateCatchEvent id="Catch">
              <signalEventDefinition signalRef="S" flowable:recordTypeShortCodes="asset"/>
            </intermediateCatchEvent>
          </process>
        </definitions>
        """;

    var errors = WorkflowBpmnXml.Validate(xml);

    Assert.Contains(errors,
        e => e.Contains("recordTypeShortCodes", StringComparison.OrdinalIgnoreCase)
          && e.Contains("startEvent", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run — expect failure**

- [ ] **Step 3: Add the validation rule**

In `Validate` (find the existing rule list near line 172), add a new builder:

```csharp
errors.AddRange(BuildRecordTypeFilterMisplacementErrors(document));
```

And implement:

```csharp
private static IReadOnlyList<string> BuildRecordTypeFilterMisplacementErrors(XDocument document)
{
    var errors = new List<string>();
    foreach (var signalEventDef in document.Descendants(BpmnNamespace + "signalEventDefinition"))
    {
        if (signalEventDef.Attribute(FlowableNamespace + "recordTypeShortCodes") is null)
            continue;

        var parent = signalEventDef.Parent;
        if (parent is null) continue;

        if (parent.Name != BpmnNamespace + "startEvent")
        {
            var elementId = parent.Attribute("id")?.Value ?? "(unknown)";
            errors.Add(
                $"Element '{elementId}': flowable:recordTypeShortCodes is only supported on signal startEvent (found on {parent.Name.LocalName}).");
        }
    }
    return errors;
}
```

- [ ] **Step 4: Run — expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs \
        tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs
git commit -m "Reject flowable:recordTypeShortCodes on non-startEvent signal definitions"
```

---

### Task 9: Create `RecordTypeShortCodeCache`

**Files:**
- Create: `src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs`
- Create: `tests/AutoNate.Web.Tests/RecordTypeShortCodeCacheTests.cs`
- Modify: `src/AutoNate.Web/Program.cs` (or DI module — confirm location with grep)

A small cache resolves `Guid → ShortCode`. Load all rows on first access (or on app start), refresh on `record-type.created/updated/archived/restored` audit events.

- [ ] **Step 1: Write the cache contract test**

```csharp
public sealed class RecordTypeShortCodeCacheTests
{
    [Fact]
    public async Task TryGetShortCode_ReturnsShortCode_AfterRefresh()
    {
        using var fixture = await EfTestFixture.CreateAsync();
        var typeId = await fixture.SeedRecordTypeAsync(shortCode: "asset");

        var cache = new RecordTypeShortCodeCache(
            fixture.DbContextFactory,
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        Assert.True(cache.TryGetShortCode(typeId, out var shortCode));
        Assert.Equal("asset", shortCode);
    }

    [Fact]
    public async Task TryGetShortCode_ReflectsRename_AfterRefresh()
    {
        using var fixture = await EfTestFixture.CreateAsync();
        var typeId = await fixture.SeedRecordTypeAsync(shortCode: "asset");

        var cache = new RecordTypeShortCodeCache(
            fixture.DbContextFactory,
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        await fixture.RenameRecordTypeShortCodeAsync(typeId, "vehicle");
        await cache.RefreshAsync();

        Assert.True(cache.TryGetShortCode(typeId, out var shortCode));
        Assert.Equal("vehicle", shortCode);
    }

    [Fact]
    public async Task TryGetShortCode_ReturnsFalse_WhenIdUnknown()
    {
        using var fixture = await EfTestFixture.CreateAsync();
        var cache = new RecordTypeShortCodeCache(
            fixture.DbContextFactory,
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        Assert.False(cache.TryGetShortCode(Guid.NewGuid(), out _));
    }
}
```

- [ ] **Step 2: Run — expect compile failure (class doesn't exist)**

- [ ] **Step 3: Implement the cache**

```csharp
namespace AutoNate.Web.Services.Records;

using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class RecordTypeShortCodeCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<RecordTypeShortCodeCache> logger)
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<RecordTypeShortCodeCache> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyDictionary<Guid, string> _byId =
        new Dictionary<Guid, string>();

    public bool TryGetShortCode(Guid recordTypeId, out string shortCode)
    {
        if (_byId.TryGetValue(recordTypeId, out var value))
        {
            shortCode = value;
            return true;
        }
        shortCode = string.Empty;
        return false;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

            var rows = await dbContext.RecordTypes
                .AsNoTracking()
                .Select(rt => new { rt.Id, rt.ShortCode })
                .ToListAsync(cancellationToken);

            _byId = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ShortCode))
                .ToDictionary(r => r.Id, r => r.ShortCode);

            _logger.LogInformation(
                "Record-type short-code cache refreshed: {Count} entries.",
                _byId.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

- [ ] **Step 4: Wire DI registration**

Run `grep -n "AddSingleton.*EfCoreWorkflowSignalRegistry\|AddSingleton.*Registry" src/AutoNate.Web/Program.cs` to find the registration block. Add:

```csharp
builder.Services.AddSingleton<RecordTypeShortCodeCache>();
```

Add an `IHostedService` or use the existing audit-event subscriber to call `RefreshAsync` on:
- application startup (call from a background `IHostedService` or after migrations apply, mirroring `EfCoreWorkflowSignalRegistry`'s init)
- `record-type.created/updated/archived/restored` audit events — find existing audit-event subscription wiring with `grep -rn "record-type" src/AutoNate.Web/Services` and hook a refresh call there.

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~RecordTypeShortCodeCacheTests"
```

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs \
        src/AutoNate.Web/Program.cs \
        tests/AutoNate.Web.Tests/RecordTypeShortCodeCacheTests.cs
git commit -m "Add RecordTypeShortCodeCache for runtime recordTypeId resolution"
```

---

### Task 10: Apply the filter in the dispatcher

**Files:**
- Modify: `src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs`
- Modify: `tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs`

- [ ] **Step 1: Failing tests for the filter behavior**

```csharp
[Fact]
public async Task HandleAsync_StartsWorkflow_WhenFilterMatchesPayloadShortCode()
{
    var assetTypeId = Guid.NewGuid();
    var (dispatcher, stub) = CreateDispatcher(
        Reg("record.events", "record.created", "AssetFlow", "asset"));
    stub.RecordTypeShortCodesById[assetTypeId] = "asset";

    await dispatcher.HandleAsync(BuildMessage(
        topic: "record.events",
        payload: $$"""{ "eventType":"record.created", "recordTypeId":"{{assetTypeId}}" }"""));

    Assert.Single(stub.StartedProcesses);
}

[Fact]
public async Task HandleAsync_SkipsWorkflow_WhenFilterDoesNotMatchPayloadShortCode()
{
    var vehicleTypeId = Guid.NewGuid();
    var (dispatcher, stub) = CreateDispatcher(
        Reg("record.events", "record.created", "AssetFlow", "asset"));
    stub.RecordTypeShortCodesById[vehicleTypeId] = "vehicle";

    await dispatcher.HandleAsync(BuildMessage(
        topic: "record.events",
        payload: $$"""{ "eventType":"record.created", "recordTypeId":"{{vehicleTypeId}}" }"""));

    Assert.Empty(stub.StartedProcesses);
}

[Fact]
public async Task HandleAsync_SkipsWorkflow_WhenFilterSetButPayloadHasNoRecordTypeId()
{
    var (dispatcher, stub) = CreateDispatcher(
        Reg("record.events", "record.created", "AssetFlow", "asset"));

    await dispatcher.HandleAsync(BuildMessage(
        topic: "record.events",
        payload: """{ "eventType":"record.created" }"""));

    Assert.Empty(stub.StartedProcesses);
}

[Fact]
public async Task HandleAsync_StartsBothFilteredAndUnfilteredWorkflows_OnMatchingPayload()
{
    var assetTypeId = Guid.NewGuid();
    var (dispatcher, stub) = CreateDispatcher(
        Reg("record.events", "record.created", "AssetFlow", "asset"),
        Reg("record.events", "record.created", "AnyRecordFlow"));  // unfiltered
    stub.RecordTypeShortCodesById[assetTypeId] = "asset";

    await dispatcher.HandleAsync(BuildMessage(
        topic: "record.events",
        payload: $$"""{ "eventType":"record.created", "recordTypeId":"{{assetTypeId}}" }"""));

    Assert.Equal(
        new[] { "AnyRecordFlow", "AssetFlow" },
        stub.StartedProcesses.Select(s => s.ProcessDefinitionKey).OrderBy(s => s));
}
```

The stub needs a `RecordTypeShortCodesById` dictionary so tests can set up resolutions without hitting the database. Create a tiny `IRecordTypeShortCodeResolver` interface or pass the cache directly and inject a fake — recommended approach: introduce a minimal interface to keep the dispatcher unit-testable.

```csharp
// src/AutoNate.Web/Services/Records/IRecordTypeShortCodeResolver.cs
public interface IRecordTypeShortCodeResolver
{
    bool TryGetShortCode(Guid recordTypeId, out string shortCode);
}
```

`RecordTypeShortCodeCache` implements this interface (one-line addition). Tests use a small fake that wraps a dictionary.

- [ ] **Step 2: Run — expect failures**

- [ ] **Step 3: Inject the resolver into the dispatcher and apply the filter**

Constructor signature becomes:

```csharp
public sealed class WorkflowSignalDispatcher(
    IWorkflowSignalRegistry registry,
    IFlowableClient flowableClient,
    IRecordTypeShortCodeResolver recordTypeResolver,
    ILogger<WorkflowSignalDispatcher> logger)
```

In `HandleAsync`, between determining `matching` and the foreach-start loop:

```csharp
var payloadRecordTypeId = TryReadGuid(message.Payload, "recordTypeId");
string? resolvedShortCode = null;
if (payloadRecordTypeId is Guid id
    && recordTypeResolver.TryGetShortCode(id, out var sc))
{
    resolvedShortCode = sc;
}

foreach (var registration in matching)
{
    if (registration.RecordTypeShortCodes.Count > 0)
    {
        if (resolvedShortCode is null
            || !registration.RecordTypeShortCodes.Contains(resolvedShortCode))
        {
            _logger.LogInformation(
                "Skipping {ProcessDefinitionKey} for signal '{SignalName}': record-type filter excluded payload (recordTypeId={RecordTypeId}, shortCode={ShortCode}).",
                registration.ProcessDefinitionKey, eventType, payloadRecordTypeId, resolvedShortCode);
            continue;
        }
    }

    try { /* existing StartProcessInstanceAsync block */ }
    catch (Exception exception) { /* existing logging */ }
}
```

Add `TryReadGuid`:

```csharp
private static Guid? TryReadGuid(string payload, string fieldName)
{
    if (string.IsNullOrWhiteSpace(payload)) return null;
    try
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!document.RootElement.TryGetProperty(fieldName, out var element)) return null;
        if (element.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(element.GetString(), out var value) ? value : null;
    }
    catch (JsonException) { return null; }
}
```

- [ ] **Step 4: Add a fake resolver to the test stub**

Either extend `StubFlowableClient` (no — different responsibility) or add a tiny test class:

```csharp
private sealed class FakeRecordTypeResolver : IRecordTypeShortCodeResolver
{
    public Dictionary<Guid, string> ShortCodesById { get; } = new();

    public bool TryGetShortCode(Guid recordTypeId, out string shortCode)
    {
        if (ShortCodesById.TryGetValue(recordTypeId, out var v)) { shortCode = v; return true; }
        shortCode = string.Empty; return false;
    }
}
```

Update `CreateDispatcher` to accept and return this resolver. Tests reference `resolver.ShortCodesById[id] = "asset"` rather than my placeholder `stub.RecordTypeShortCodesById` from Step 1 — adjust the tests when implementing.

- [ ] **Step 5: Wire DI**

`Program.cs`:

```csharp
builder.Services.AddSingleton<IRecordTypeShortCodeResolver>(
    sp => sp.GetRequiredService<RecordTypeShortCodeCache>());
```

- [ ] **Step 6: Run all tests — expect green**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
```

- [ ] **Step 7: Commit**

```bash
git add src/AutoNate.Web/Services/Records/IRecordTypeShortCodeResolver.cs \
        src/AutoNate.Web/Services/Records/RecordTypeShortCodeCache.cs \
        src/AutoNate.Web/Services/Signals/WorkflowSignalDispatcher.cs \
        src/AutoNate.Web/Program.cs \
        tests/AutoNate.Web.Tests/WorkflowSignalDispatcherTests.cs
git commit -m "Apply record-type filter in signal dispatcher"
```

---

### Task 11: Validation — warning when shortcode references unknown record type

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs` (or its publish-time validation caller — confirm location with grep)
- Modify: tests appropriately

- [ ] **Step 1: Locate the publish validation pipeline**

```bash
grep -rn "WorkflowBpmnXml.Validate\|publish.*workflow\|PublishAsync" src/AutoNate.Web/Services/Workflow src/AutoNate.Web/Endpoints
```

Identify where `Validate` is called at publish time (likely inside a workflow store or endpoint handler). The XML-only `Validate` should not depend on `RecordType` data, so the **warning** rule lives one level up — wherever publish-time validation runs with DB access.

- [ ] **Step 2: Failing test for the warning surfaced through the publish path**

(Use the existing publish/validation test harness — search for tests under `WorkflowEndpointsTests.cs` or `EfCoreWorkflowModelStoreTests.cs` that already assert on validation messages, and follow the same shape.)

```csharp
[Fact]
public async Task Publish_ReturnsWarning_WhenSignalFilterReferencesUnknownShortCode()
{
    using var fixture = await EfTestFixture.CreateAsync();
    await fixture.SeedRecordTypeAsync(shortCode: "asset");

    var xml = /* signal start with flowable:recordTypeShortCodes="asset,unknownType" */;

    var result = await fixture.PublishWorkflowAsync(xml);

    Assert.Contains(result.Warnings,
        w => w.Contains("unknownType", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Run — expect failure**

- [ ] **Step 4: Implement the warning rule**

In the publish-time validator (the layer with DB access):

```csharp
async Task<IReadOnlyList<string>> BuildRecordTypeShortCodeWarningsAsync(
    XDocument document, CancellationToken ct)
{
    var referenced = new HashSet<string>(StringComparer.Ordinal);
    foreach (var def in document.Descendants(BpmnNamespace + "signalEventDefinition"))
    {
        var raw = def.Attribute(FlowableNamespace + "recordTypeShortCodes")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) continue;
        foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            referenced.Add(token);
    }
    if (referenced.Count == 0) return Array.Empty<string>();

    var existing = (await dbContext.RecordTypes
            .AsNoTracking()
            .Where(rt => referenced.Contains(rt.ShortCode))
            .Select(rt => rt.ShortCode)
            .ToListAsync(ct))
        .ToHashSet(StringComparer.Ordinal);

    var unknown = referenced.Where(c => !existing.Contains(c)).ToArray();
    if (unknown.Length == 0) return Array.Empty<string>();
    return new[]
    {
        $"Signal start event references record-type shortcode(s) not found in this environment: {string.Join(", ", unknown)}. The filter will never match these until the type is created."
    };
}
```

Surface this through the existing warning channel (see how other warnings are returned today — `Validate` may need a sibling `ValidateAsync` if synchronous is the current contract).

- [ ] **Step 5: Run — expect pass**

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Web/Services/Workflow/ tests/AutoNate.Web.Tests/
git commit -m "Warn at publish when signal filter references unknown record-type shortcode"
```

---

## Phase 3 — Editor UI

### Task 12: Add `CarriesRecordType` to `EventCatalogEntry`

**Files:**
- Modify: `src/AutoNate.Web/Services/Events/EventCatalog.cs`
- Modify: any DTO/endpoint that exposes the catalog (find with `grep -rn "EventCatalogEntry\|EventCatalogResponse" src/AutoNate.Web`)
- Modify: `src/AutoNate.Spa/src/hooks/useEventCatalog.ts` (and the corresponding TS type)
- Modify: tests

- [ ] **Step 1: Read the current `EventCatalogEntry` declaration**

```bash
grep -n "EventCatalogEntry\b" src/AutoNate.Web/Services/Events/EventCatalog.cs
```

- [ ] **Step 2: Add the parameter (defaulted to `false`)**

Modify the record declaration so existing call sites compile unchanged:

```csharp
public sealed record class EventCatalogEntry(
    string Topic,
    string EventType,
    string Summary,
    string Behavior,
    IReadOnlyList<string> Notes,
    bool CarriesRecordType = false);
```

(Adjust to match the actual existing parameter list — fetch with grep first.)

- [ ] **Step 3: Set `CarriesRecordType: true` on the six record-event entries**

Find the entries with:

```bash
grep -n "record\.created\|record\.updated\|record\.status\.changed\|record\.assignees\.changed\|record\.restored\|record\.deleted" src/AutoNate.Web/Services/Events/EventCatalog.cs
```

For each match, append `CarriesRecordType: true` to the constructor call (named argument so we don't have to count positions).

- [ ] **Step 4: Surface the flag in the catalog API response**

Find the response DTO with `grep -rn "EventCatalogResponse\|EventCatalogCategoryResponse\|EventCatalogEndpoints" src/AutoNate.Web`. Add `bool CarriesRecordType` to the entry DTO and map it.

- [ ] **Step 5: Mirror in TS**

`src/AutoNate.Spa/src/hooks/useEventCatalog.ts` — add `carriesRecordType: boolean` to whichever interface describes a single event entry.

- [ ] **Step 6: Add a test asserting the flag is set**

In `tests/AutoNate.Web.Tests/...` (find the existing event-catalog tests via `grep -rn "EventCatalog" tests`):

```csharp
[Fact]
public void EventCatalog_RecordEvents_CarryRecordType()
{
    var recordEntries = EventCatalog.Categories
        .SelectMany(c => c.Events)
        .Where(e => e.Topic == "record.events")
        .ToArray();

    Assert.NotEmpty(recordEntries);
    Assert.All(recordEntries, e => Assert.True(e.CarriesRecordType,
        $"Expected {e.EventType} to set CarriesRecordType=true."));
}
```

- [ ] **Step 7: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~EventCatalog"
```

- [ ] **Step 8: Commit**

```bash
git add src/AutoNate.Web/Services/Events/EventCatalog.cs \
        src/AutoNate.Web/Endpoints/ \
        src/AutoNate.Spa/src/hooks/useEventCatalog.ts \
        tests/AutoNate.Web.Tests/
git commit -m "Flag event-catalog entries that carry recordTypeId"
```

---

### Task 13: SPA — `workflow.js` describe + update `recordTypeShortCodes`

**Files:**
- Modify: `src/AutoNate.Spa/src/lib/bpmn/workflow.js:1064-1070, 1219-1240, ~1410-1472`

- [ ] **Step 1: Extend `describeSignalStartEvent`**

After reading `signalTopic` (around line 1238), add:

```javascript
const rawShortCodes = typeof signalEventDefinition.$attrs?.["flowable:recordTypeShortCodes"] === "string"
  ? signalEventDefinition.$attrs["flowable:recordTypeShortCodes"]
  : null;
const recordTypeShortCodes = rawShortCodes
  ? rawShortCodes
      .split(",")
      .map((s) => s.trim())
      .filter((s) => s.length > 0)
  : [];

return {
  signalName,
  signalTopic,
  recordTypeShortCodes
};
```

Then update `describeBusinessObject` (line 1064-1070) to pass through:

```javascript
if (signal) {
  description.signalName = signal.signalName;
  description.signalTopic = signal.signalTopic;
  description.recordTypeShortCodes = signal.recordTypeShortCodes;
}
```

- [ ] **Step 2: Extend `updateSignalStartEventProperties`**

Right after `writeFlowableAttribute(signal, "topic", topic);` (around line 1465 — note this writes to the `<signal>` root; the new attribute goes on `signalEventDefinition` instead):

```javascript
const shortCodes = Array.isArray(payload.recordTypeShortCodes)
  ? payload.recordTypeShortCodes
      .map((s) => (typeof s === "string" ? s.trim() : ""))
      .filter((s) => s.length > 0)
  : [];

writeFlowableAttribute(
  signalEventDefinition,
  "recordTypeShortCodes",
  shortCodes.length === 0 ? null : shortCodes.join(","));
```

(`writeFlowableAttribute` is already used elsewhere in this file for `flowable:topic`; pass `null` to remove the attribute. Verify the helper handles null correctly by reading its definition.)

- [ ] **Step 3: Manual verification**

```bash
cd src/AutoNate.Spa && npm run build
```

Expected: clean build. (No automated unit tests cover this file directly today, so we lean on the integration round-trip from Task 7 and the SPA test in Task 14.)

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Spa/src/lib/bpmn/workflow.js
git commit -m "Round-trip recordTypeShortCodes in BPMN editor describe/update"
```

---

### Task 14: SPA — snapshot mirror in `api/workflows.ts`

**Files:**
- Modify: `src/AutoNate.Spa/src/api/workflows.ts:60-80` (find the `WorkflowElementSnapshot` type with grep)

- [ ] **Step 1: Add the field**

Around line 67 (next to `signalTopic?: string | null;`):

```ts
recordTypeShortCodes?: string[] | null;
```

- [ ] **Step 2: Build & lint**

```bash
cd src/AutoNate.Spa && npm run build
```

- [ ] **Step 3: Commit**

```bash
git add src/AutoNate.Spa/src/api/workflows.ts
git commit -m "Add recordTypeShortCodes to WorkflowElementSnapshot wire shape"
```

---

### Task 15: SPA — modal field + conditional visibility + mid-edit safety

**Files:**
- Modify: `src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx`

This is the largest UI change. We'll do it in three sub-commits to keep diffs reviewable.

#### 15a: Editor type and selection plumbing

- [ ] **Step 1: Extend `SignalStartEventEditor` (line 82)**

```ts
type SignalStartEventEditor = {
  id: string;
  type: string;
  name: string;
  signalName: string;
  signalTopic: string;
  recordTypeShortCodes: string[];   // NEW
};
```

- [ ] **Step 2: Populate on selection (line 414-420)**

Find the block where `setSignalStartEditor({...})` is called (around line 417) and add:

```ts
recordTypeShortCodes: Array.isArray(selection.recordTypeShortCodes)
  ? [...selection.recordTypeShortCodes]
  : [],
```

- [ ] **Step 3: Pass through on apply (line 764-776)**

`applySignalStart` constructs a payload for `workflow.updateSignalStartEventProperties`. Pass:

```ts
recordTypeShortCodes: signalStartEditor.recordTypeShortCodes,
```

- [ ] **Step 4: Pass through to the snapshot save (search for where the editor is converted to a `WorkflowElementSnapshot`)**

Add `recordTypeShortCodes` to the snapshot mapping.

- [ ] **Step 5: Build & lint**

```bash
cd src/AutoNate.Spa && npm run build
```

- [ ] **Step 6: Commit**

```bash
git add src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx
git commit -m "Plumb recordTypeShortCodes through SignalStartEventEditor"
```

#### 15b: Modal field + visibility

- [ ] **Step 1: Add `useRecordTypes` import (line 47-49)**

```ts
import { useRecordTypes } from "@/hooks/useRecordTypes";
```

- [ ] **Step 2: Add the field to `SignalStartEventModal` (line 2094)**

Near the existing `useEventCatalog` call (line 2107), add:

```ts
const { data: recordTypes } = useRecordTypes(/* includeArchived */ false);

const carriesRecordType = useMemo(() => {
  if (!catalog) return false;
  for (const category of catalog.categories ?? []) {
    for (const evt of category.events) {
      if (evt.topic === editor.signalTopic.trim()
          && evt.eventType === editor.signalName.trim()) {
        return Boolean(evt.carriesRecordType);
      }
    }
  }
  return false;
}, [catalog, editor.signalTopic, editor.signalName]);
```

After the existing "Event Type" field (line 2233), conditionally render:

```tsx
{carriesRecordType && (
  <label className="workflow-field">
    <span>Record types (optional)</span>
    <RecordTypeMultiSelect
      selected={editor.recordTypeShortCodes}
      options={recordTypes ?? []}
      onChange={(next) => onChange({ ...editor, recordTypeShortCodes: next })}
    />
    <p className="workflow-modal-note">
      Empty = all record types match. When set, only payloads whose
      <code> recordTypeId </code> matches one of these will start this workflow.
    </p>
  </label>
)}
```

- [ ] **Step 3: Implement `RecordTypeMultiSelect`**

Inline component near the modal (or extract to `components/workflow/RecordTypeMultiSelect.tsx` if file is getting unwieldy):

```tsx
function RecordTypeMultiSelect({
  selected,
  options,
  onChange
}: {
  selected: string[];
  options: { shortCode: string; name: string; isArchived?: boolean }[];
  onChange: (next: string[]) => void;
}) {
  const toggle = (shortCode: string) => {
    if (selected.includes(shortCode)) {
      onChange(selected.filter((s) => s !== shortCode));
    } else {
      onChange([...selected, shortCode]);
    }
  };

  return (
    <div className="workflow-record-type-multiselect">
      {options.map((opt) => (
        <label key={opt.shortCode} className="workflow-chip">
          <input
            type="checkbox"
            checked={selected.includes(opt.shortCode)}
            onChange={() => toggle(opt.shortCode)}
          />
          <span>{opt.name}{opt.isArchived ? " (archived)" : ""}</span>
        </label>
      ))}
    </div>
  );
}
```

(Confirm the field name — `useRecordTypes` returns objects with `shortCode` per `src/AutoNate.Spa/src/hooks/useRecordTypes.ts`; verify and adjust.)

- [ ] **Step 4: Build & smoke-test in the dev server**

```bash
cd src/AutoNate.Spa && npm run dev
```

In a browser, open a workflow, drop a signal start event, set Topic=`record.events` and Event Type=`record.created`. Confirm the "Record types" multi-select appears. Switch Event Type to `process.started` (different topic — needs an unrelated workflow open or a manual catalog spelunking), confirm the multi-select disappears.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx
git commit -m "Show record-type multi-select in signal modal for record-carrying events"
```

#### 15c: Mid-edit safety — preserve selection but warn + strip on apply

- [ ] **Step 1: Add the warning render**

Just below the Event Type input (line ~2232):

```tsx
{!carriesRecordType && editor.recordTypeShortCodes.length > 0 && (
  <p className="workflow-modal-warning">
    This event type doesn't carry a record type — the configured record-type
    filter will be cleared when you apply.
  </p>
)}
```

- [ ] **Step 2: Strip on apply**

In `applySignalStart` (line 764-776), when invoking `workflow.updateSignalStartEventProperties`, conditionally clear:

```ts
const finalShortCodes = carriesRecordType
  ? signalStartEditor.recordTypeShortCodes
  : [];
await workflow.updateSignalStartEventProperties(handle, {
  ...signalStartEditor,
  recordTypeShortCodes: finalShortCodes
});
```

(`carriesRecordType` needs to be computed at the call site as well — extract to a helper module-level function or recompute inline using the same catalog lookup.)

- [ ] **Step 3: Build, dev-server smoke test**

Drop a signal start event, set Event Type=`record.created`, pick Asset, switch to `process.started`. Confirm the warning appears. Apply → the saved snapshot has empty `recordTypeShortCodes`.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx
git commit -m "Strip record-type filter on apply when event no longer carries record type"
```

---

## Phase 4 — Integration test + cleanup

### Task 16: End-to-end integration test

**Files:**
- Create: `tests/AutoNate.Web.Tests/SignalStartRecordTypeFilterIntegrationTests.cs` (or extend an existing integration suite — find the pattern with `grep -rn "PublishAsync\|StartProcessInstanceAsync" tests`)

- [ ] **Step 1: Author the test**

```csharp
[Fact]
public async Task RecordCreated_StartsOnlyMatchingFilteredWorkflows()
{
    using var fixture = await EfTestFixture.CreateAsync();
    var assetTypeId = await fixture.SeedRecordTypeAsync(shortCode: "asset");
    var vehicleTypeId = await fixture.SeedRecordTypeAsync(shortCode: "vehicle");

    await fixture.PublishWorkflowAsync(
        processKey: "AssetFlow",
        recordTypeFilter: new[] { "asset" });
    await fixture.PublishWorkflowAsync(
        processKey: "AnyRecordFlow",
        recordTypeFilter: Array.Empty<string>());

    await fixture.SignalRegistry.RefreshAsync();
    await fixture.RecordTypeShortCodeCache.RefreshAsync();

    // Asset payload starts both
    await fixture.Dispatcher.HandleAsync(BuildMessage(assetTypeId));
    Assert.Equal(
        new[] { "AnyRecordFlow", "AssetFlow" },
        fixture.FlowableStub.StartedProcesses.Select(s => s.ProcessDefinitionKey).OrderBy(s => s));

    fixture.FlowableStub.StartedProcesses.Clear();

    // Vehicle payload starts only the unfiltered one
    await fixture.Dispatcher.HandleAsync(BuildMessage(vehicleTypeId));
    var only = Assert.Single(fixture.FlowableStub.StartedProcesses);
    Assert.Equal("AnyRecordFlow", only.ProcessDefinitionKey);
}
```

- [ ] **Step 2: Implement any missing fixture helpers**

`PublishWorkflowAsync(processKey, recordTypeFilter)` should generate a minimal BPMN doc with the given process key and `flowable:recordTypeShortCodes` attribute, then call the actual publish path (so the registry refresh extracts the filter).

- [ ] **Step 3: Run — expect pass**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add tests/AutoNate.Web.Tests/SignalStartRecordTypeFilterIntegrationTests.cs
git commit -m "Integration test: filtered and unfiltered signal starts on record events"
```

---

### Task 17: Final regression sweep

- [ ] **Step 1: Run all tests**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
cd src/AutoNate.Spa && npm run build && npm run test --if-present
```

- [ ] **Step 2: Manual smoke**

Start the dev environment (per `README.md`), publish a workflow with a filter via the studio, post a `record.created` event for a matching record type via the existing test harness or `dapr publish` CLI, observe the workflow instance starts. Repeat with a non-matching record type → no instance starts.

- [ ] **Step 3: Tag the spec as implemented**

In `docs/superpowers/specs/2026-05-04-signal-start-record-type-filter-design.md`, change `**Status:** Approved (design)` to `**Status:** Implemented`.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-05-04-signal-start-record-type-filter-design.md
git commit -m "Mark signal start record-type filter spec as implemented"
```

---

## Self-Review Notes

- **Spec coverage:** every major spec section maps to a task — BPMN attribute (Task 7), data model (Tasks 1, 12), editor UI (Tasks 13–15), dispatch architecture (Tasks 4–6, 10), validation (Tasks 8, 11), EventCatalog flag (Task 12), testing (Tasks 1–16), observability (Task 6 logs, Task 10 logs).
- **Out-of-scope items honored:** intermediate-catch filters, generic JSON-path filters, boundary-event filters — none added to the plan.
- **Type consistency:** `WorkflowSignalRegistration` parameters used in tasks 1, 2, 3, 6, 10 are identical (`SignalName, Topic, ProcessDefinitionKey, RecordTypeShortCodes`). The `IRecordTypeShortCodeResolver` interface name in Task 10 is referenced consistently in DI wiring (Task 10 Step 5). `flowable:recordTypeShortCodes` attribute name is identical in tasks 7, 8, 11, 12, 13, 15.
- **Behavior preservation:** Phase 1 alone passes the existing test suite (after assertion updates) because every registration's filter is empty until Phase 2's BPMN-attribute reading goes live.
- **Risk surface:** the dispatcher rewrite (Task 6) is the highest-risk single commit. It is independently revertable — Tasks 7–10 layer on top without changing the dispatch shape.
