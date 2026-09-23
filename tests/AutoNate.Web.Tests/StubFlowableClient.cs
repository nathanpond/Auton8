using System.Net;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Tests;

/// <summary>
/// Test double for IFlowableClient. Returns canned responses and records
/// invocations so endpoint tests can verify wiring without a real Flowable
/// server. Not for unit testing the FlowableClient itself.
/// </summary>
internal sealed class StubFlowableClient : IFlowableClient
{
    public List<string> Calls { get; } = new();

    // Tests configure these to seed canned responses for the methods that
    // authorization handlers consult.
    public Dictionary<string, FlowableProcessInstanceSummary> InstancesById { get; } = new();
    public Dictionary<string, List<FlowableTaskSummary>> TasksByUser { get; } = new();

    /// <summary>
    /// Set to make the next deploy throw as the real client would (#344).
    /// </summary>
    /// <remarks>
    /// Without this the publish route's engine-refusal branches could not be
    /// driven at all, and they were not: reverting the whole of #334 — both the
    /// status mapping and the sanitisation — left the suite green at 44/44,
    /// because every test of the refusal path called the pure function directly.
    /// </remarks>
    public FlowableRequestException? DeployThrows { get; set; }

    /// <summary>
    /// Every model handed to <see cref="DeployProcessAsync"/>, in order.
    /// </summary>
    /// <remarks>
    /// The DEPLOYED copy, which is not the stored one: publish expands and pins
    /// before deploying, and #111's version binding is only observable here.
    /// </remarks>
    public List<WorkflowModel> DeployedModels { get; } = [];

    public Task<WorkflowDeploymentInfo> DeployProcessAsync(
        WorkflowModel model, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Deploy:{model.ProcessKey}");
        if (DeployThrows is not null) throw DeployThrows;

        DeployedModels.Add(model);

        // #646. A deployment produces a SET: one definition per executable
        // process in the file, like the engine. Recorded per deployment id so
        // the readback returns what this upload produced, and so the endpoint's
        // set branch (pause/resume across every definition) can run under test.
        var deploymentId = $"stub-deployment-{Interlocked.Increment(ref _deployments)}";
        var definitions = ExecutableProcessIds(model.BpmnXml)
            .Select(key => new FlowableProcessDefinitionSummary
            {
                Id = $"{key}:1:{deploymentId}",
                Key = key,
                Name = key,
                Version = 1,
                DeploymentId = deploymentId,
                Suspended = false
            })
            .ToList();
        DeployedSets[deploymentId] = definitions;
        // Like the engine: no definition under the model's key is a failure, not
        // a definition the stub invents (#653 prepares the deployable, so the key
        // is always there for a caller that went through the endpoint).
        var primary = definitions.FirstOrDefault(d => d.Key == model.ProcessKey)
            ?? throw new InvalidOperationException(
                $"Stub deployment of '{model.ProcessKey}' produced no definition under that key; it produced: {string.Join(", ", definitions.Select(d => d.Key))}.");

        return Task.FromResult(new WorkflowDeploymentInfo
        {
            DeploymentId = deploymentId,
            ProcessDefinitionId = primary.Id,
            ProcessDefinitionKey = primary.Key,
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow,
            Definitions = definitions.Select(d => new WorkflowDeployedDefinition
            {
                ProcessDefinitionKey = d.Key, ProcessDefinitionId = d.Id, ProcessDefinitionVersion = d.Version, Name = d.Name
            }).ToList()
        });
    }

    private static int _deployments;

    /// <summary>The set each stub deployment produced, by deployment id (#646).</summary>
    public Dictionary<string, List<FlowableProcessDefinitionSummary>> DeployedSets { get; } = new(StringComparer.Ordinal);

    private static IReadOnlyList<string> ExecutableProcessIds(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];
        try
        {
            System.Xml.Linq.XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
            return System.Xml.Linq.XDocument.Parse(xml).Descendants(bpmn + "process")
                .Where(p => !string.Equals(p.Attribute("isExecutable")?.Value, "false", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    // #169. The set a deployment produced, and the withdrawal of one. The stub
    // records withdrawals so a test can assert the compensation reached the
    // engine boundary; it produces the primary alone as its "set", which is
    // what a single-pool publish reads back.
    public List<string> DeletedDeployments { get; } = [];

    public Task<IReadOnlyList<FlowableProcessDefinitionSummary>> GetProcessDefinitionsByDeploymentAsync(
        string deploymentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FlowableProcessDefinitionSummary>>(
            DeployedSets.TryGetValue(deploymentId, out var set) ? set : []);

    public Task<IReadOnlyList<FlowableProcessInstanceSummary>> GetCounterpartInstancesAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FlowableProcessInstanceSummary>>([]);

    public Task DeleteDeploymentAsync(string deploymentId, bool cascade, CancellationToken cancellationToken = default)
    {
        DeletedDeployments.Add(deploymentId);
        return Task.CompletedTask;
    }

    // Tests can seed this to drive both GetLatestProcessDefinitionAsync and
    // the bulk variant. Keyed by processDefinitionKey.
    public Dictionary<string, FlowableProcessDefinitionSummary> ProcessDefinitionsByKey { get; } = new();

    public Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(
        string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetLatest:{processDefinitionKey}");
        ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var summary);
        return Task.FromResult<FlowableProcessDefinitionSummary?>(summary);
    }

    public Task<IReadOnlyList<FlowableProcessDefinitionSummary>> GetLatestProcessDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ListLatestDefinitions");
        IReadOnlyList<FlowableProcessDefinitionSummary> list = ProcessDefinitionsByKey.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task SuspendProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"SuspendDefinition:{processDefinitionKey}");
        if (ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var existing))
        {
            ProcessDefinitionsByKey[processDefinitionKey] = existing with { Suspended = true };
        }
        return Task.CompletedTask;
    }

    public Task ActivateProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ActivateDefinition:{processDefinitionKey}");
        if (ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var existing))
        {
            ProcessDefinitionsByKey[processDefinitionKey] = existing with { Suspended = false };
        }
        return Task.CompletedTask;
    }

    public List<(string ProcessDefinitionKey, string? Name, IReadOnlyDictionary<string, object?>? Variables)>
        StartedProcesses { get; } = new();

    // Set to make StartProcessInstanceAsync throw — dispatcher tests for the
    // per-process error-isolation path use this without needing a separate
    // IFlowableClient implementation.
    public Exception? StartProcessInstanceThrows { get; set; }

    public Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(
        string processDefinitionKey,
        string? name = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Start:{processDefinitionKey}:{name ?? "(unnamed)"}");
        StartedProcesses.Add((processDefinitionKey, name, variables));
        if (StartProcessInstanceThrows is not null)
        {
            throw StartProcessInstanceThrows;
        }
        return Task.FromResult(StartedInstance ?? new FlowableProcessInstanceSummary { Name = name });
    }

    /// <summary>
    /// What <see cref="StartProcessInstanceAsync"/> returns, when a test needs the
    /// started instance to have an id (#609).
    /// </summary>
    /// <remarks>
    /// The default carries only the name, which was enough while nothing did
    /// anything with the instance afterwards. #609 projects it, and a projection
    /// keyed on an empty id proves nothing.
    /// </remarks>
    public FlowableProcessInstanceSummary? StartedInstance { get; set; }

    // Tests can seed this to assert the count-based auto-naming flow.
    public Dictionary<string, int> InstanceCountsByDefinitionKey { get; } = new();

    /// <summary>#658. Instances the engine's HISTORY still holds; a live instance is in history too.</summary>
    public HashSet<string> HistoricInstanceIds { get; } = new(StringComparer.Ordinal);

    /// <summary>#675. Set to simulate a transient failure of the history endpoint.</summary>
    public bool ThrowOnHistoricProcessInstanceExistsAsync { get; set; }

    public Task<bool> HistoricProcessInstanceExistsAsync(string processInstanceId, CancellationToken cancellationToken = default) =>
        ThrowOnHistoricProcessInstanceExistsAsync
            ? throw new FlowableRequestException(System.Net.HttpStatusCode.ServiceUnavailable, "query the historic process instance", "simulated transient failure")
            : Task.FromResult(HistoricInstanceIds.Contains(processInstanceId) || InstancesById.ContainsKey(processInstanceId));

    public Task<int> GetHistoricProcessInstanceCountByDefinitionKeyAsync(
        string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"CountByDefinitionKey:{processDefinitionKey}");
        InstanceCountsByDefinitionKey.TryGetValue(processDefinitionKey, out var count);
        return Task.FromResult(count);
    }

    /// <summary>Finished tasks the completion sweep will page through (#586).</summary>
    /// <remarks>
    /// Ordered newest-completed first by the stub, because the real endpoint is
    /// queried with `sort=endTime&amp;order=desc` and the sweep's stop condition
    /// depends on that order being real rather than incidental.
    /// </remarks>
    public List<FlowableFinishedTask> FinishedTasks { get; } = new();

    /// <summary>Set to make GetFinishedTasksAsync throw mid-sweep (#586).</summary>
    public Exception? GetFinishedTasksThrows { get; set; }

    /// <summary>Page index (0-based) at which GetFinishedTasksAsync throws (#586).</summary>
    public int GetFinishedTasksThrowsOnPage { get; set; }

    public Task<IReadOnlyList<FlowableFinishedTask>> GetFinishedTasksAsync(
        int start, int size, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetFinishedTasks:{start}:{size}");
        if (GetFinishedTasksThrows is not null && start / Math.Max(1, size) == GetFinishedTasksThrowsOnPage)
        {
            throw GetFinishedTasksThrows;
        }

        var ordered = FinishedTasks
            .OrderByDescending(t => t.EndedAtUtc ?? DateTimeOffset.MinValue)
            .Skip(start)
            .Take(size)
            .ToArray();
        return Task.FromResult<IReadOnlyList<FlowableFinishedTask>>(ordered);
    }

    // Set to make GetProcessInstanceAsync throw, standing in for Flowable being
    // unreachable (#579). Distinct from an absent entry on purpose: returning
    // null means "the engine says this instance does not exist", which
    // FlowableReadThrough treats as a deletion and clears the cache row for.
    // Throwing means "we could not ask", which is the degradation path.
    public Exception? GetProcessInstanceThrows { get; set; }

    public Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetInstance:{processInstanceId}");
        if (GetProcessInstanceThrows is not null) throw GetProcessInstanceThrows;
        InstancesById.TryGetValue(processInstanceId, out var summary);
        return Task.FromResult(summary);
    }

    // Tests that exercise the workflow detectors set this list to control
    // what GetWorkflowExecutionsAsync returns. Default is empty (existing
    // tests rely on the no-op shape).
    public List<WorkflowExecutionSummary> Executions { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(
        CancellationToken cancellationToken = default) =>
        GetWorkflowExecutionsAsync(maxPages: 1, cancellationToken);

    /// <summary>Records the page ceiling it was asked for (#588).</summary>
    /// <remarks>
    /// The call is recorded WITH the ceiling, so a test can assert that the poll
    /// and the backfill ask for different amounts of work — which is the whole
    /// reason the bound is a parameter rather than a constant.
    /// </remarks>
    public Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(
        int maxPages, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListExecutions:{maxPages}");
        return Task.FromResult<IReadOnlyList<WorkflowExecutionSummary>>(Executions.ToArray());
    }

    // #294. Settable so a test can supply the id-bearing surfaces; an empty
    // detail by default, which is what every existing test assumes.
    //
    // It used to be hard-coded to `new WorkflowExecutionDiagramDetail()`, so the
    // cancelled, failed, error-map and history mappings could not be exercised at
    // all -- and `ExpansionSourceMap` beside it was never set by any test, in any
    // file. Four of #218's five id surfaces were unasserted as a result.
    public WorkflowExecutionDiagramDetail DiagramDetail { get; set; } = new();

    public Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Diagram:{processInstanceId}");
        return Task.FromResult(DiagramDetail);
    }

    // #218. Settable so a test can supply a mapping; empty by default, which is
    // what every existing test assumes.
    public IReadOnlyDictionary<string, string> ExpansionSourceMap { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    // #163
    public List<AdhocSubProcessState> AdhocSubProcesses { get; } = [];

    // #243. Settable so a test can make a signal instance-scoped; global by
    // default, which is what every existing test assumes.
    public HashSet<string> InstanceScopedSignals { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsSignalGlobalAsync(
        string processDefinitionId,
        string signalName,
        string? activityId = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"SignalScope:{processDefinitionId}:{signalName}:{activityId}");
        return Task.FromResult(!InstanceScopedSignals.Contains(signalName));
    }

    public List<(string ExecutionId, string ProcessDefinitionId, string? ActivityId)> AwaitingSignalExecutions { get; } = [];

    /// <summary>
    /// Executions waiting on a signal, from either fixture.
    /// </summary>
    /// <remarks>
    /// #243 moved the dispatcher onto this overload so it can ask each waiting
    /// execution's definition whether the signal is instance-scoped. Tests that
    /// only care THAT waiting executions are woken still set
    /// <see cref="WaitingExecutionsBySignal"/>, and they are still testing the
    /// same guarantee -- so this reads both rather than making them restate a
    /// fixture in the new shape. Rewriting those assertions to match the new
    /// plumbing is how a guard quietly stops guarding what it was written for.
    /// </remarks>
    public Task<IReadOnlyList<(string ExecutionId, string ProcessDefinitionId, string? ActivityId)>>
        ListExecutionsAwaitingSignalWithDefinitionAsync(
            string signalName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"AwaitingSignal:{signalName}");

        if (AwaitingSignalExecutions.Count > 0)
        {
            return Task.FromResult<IReadOnlyList<(string, string, string?)>>(AwaitingSignalExecutions);
        }

        var byName = WaitingExecutionsBySignal.TryGetValue(signalName, out var ids)
            ? ids.Select(id => (id, "stub-definition:1:1", (string?)null)).ToList()
            : [];

        return Task.FromResult<IReadOnlyList<(string, string, string?)>>(byName);
    }

    public Task<IReadOnlyList<AdhocSubProcessState>> GetAdhocSubProcessesAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"AdhocList:{processInstanceId}");
        return Task.FromResult<IReadOnlyList<AdhocSubProcessState>>(AdhocSubProcesses);
    }

    public Task StartAdhocActivityAsync(
        string executionId, string activityId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"AdhocStart:{executionId}:{activityId}");
        return Task.CompletedTask;
    }

    public Task CompleteAdhocSubProcessAsync(
        string executionId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"AdhocComplete:{executionId}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetExpansionSourceMapByDefinitionAsync(
        string processDefinitionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExpansionSourceMap);

    /// <summary>#327. The map read backwards, as the real client reads it.</summary>
    public Task<string> ResolveEngineActivityIdAsync(
        string processInstanceId, string activityId, CancellationToken cancellationToken = default)
    {
        foreach (var (generated, authored) in ExpansionSourceMap)
        {
            if (string.Equals(authored, activityId, StringComparison.Ordinal)) return Task.FromResult(generated);
        }
        return Task.FromResult(activityId);
    }

    public Task<IReadOnlyDictionary<string, string>> GetExpansionSourceMapAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ExpansionMap:{processInstanceId}");
        return Task.FromResult(ExpansionSourceMap);
    }

    // Tests can seed this to drive the history endpoint response. Defaults to
    // an empty list when not set.
    public Dictionary<string, List<WorkflowExecutionHistoryEvent>> HistoryByInstance { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionHistoryEvent>> GetWorkflowExecutionHistoryAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"History:{processInstanceId}");
        HistoryByInstance.TryGetValue(processInstanceId, out var events);
        IReadOnlyList<WorkflowExecutionHistoryEvent> list =
            events?.AsReadOnly() ?? (IReadOnlyList<WorkflowExecutionHistoryEvent>)Array.Empty<WorkflowExecutionHistoryEvent>();
        return Task.FromResult(list);
    }

    // #173. The engine's own multi-instance counters and per-execution
    // variables. Seeded per process instance; unseeded instances report Empty,
    // which is the shape a finished process returns for real.
    public Dictionary<string, MultiInstanceEngineState> MultiInstanceStateByInstance { get; } = new();

    public Task<MultiInstanceEngineState> GetMultiInstanceEngineStateAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"MultiInstanceState:{processInstanceId}");
        return Task.FromResult(
            MultiInstanceStateByInstance.TryGetValue(processInstanceId, out var state)
                ? state
                : MultiInstanceEngineState.Empty);
    }

    public Dictionary<string, List<WorkflowExecutionLogEntry>> LogByInstance { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionLogEntry>> GetWorkflowExecutionLogAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Log:{processInstanceId}");
        LogByInstance.TryGetValue(processInstanceId, out var entries);
        IReadOnlyList<WorkflowExecutionLogEntry> list =
            entries?.AsReadOnly() ?? (IReadOnlyList<WorkflowExecutionLogEntry>)Array.Empty<WorkflowExecutionLogEntry>();
        return Task.FromResult(list);
    }

    public Task DeleteWorkflowExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"DeleteExecution:{processInstanceId}");
        return Task.CompletedTask;
    }

    // Tests can seed this to control how many instances the bulk-delete
    // endpoint reports back; the stub doesn't enumerate real instances.
    public int DeleteAllWorkflowExecutionsResult { get; set; }

    public Task<int> DeleteAllWorkflowExecutionsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("DeleteAllExecutions");
        return Task.FromResult(DeleteAllWorkflowExecutionsResult);
    }

    public Task CancelWorkflowExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"CancelExecution:{processInstanceId}");
        return Task.CompletedTask;
    }

    // Tests that need GetTasksByProcessInstanceAsync to return tasks
    // populate this dictionary keyed by process instance id. Default empty.
    public Dictionary<string, List<FlowableTaskSummary>> TasksByProcess { get; } = new();

    /// <summary>
    /// Set to make the task fetch throw, as the real client does when the engine
    /// is unreachable (#604).
    /// </summary>
    /// <remarks>
    /// "Flowable is down" used to be modelled by <see cref="GetProcessInstanceThrows"/>
    /// alone, which was enough while `/{id}/tasks` never asked the engine for the
    /// task list. Now it does, so a fixture that leaves this call answering
    /// normally is not modelling an unreachable engine -- it is modelling an
    /// engine that is up and reports no tasks, which is a different thing and the
    /// opposite assertion.
    /// </remarks>
    public Exception? GetTasksByProcessInstanceThrows { get; set; }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksByInstance:{processInstanceId}");
        if (GetTasksByProcessInstanceThrows is not null) throw GetTasksByProcessInstanceThrows;
        TasksByProcess.TryGetValue(processInstanceId, out var tasks);
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(
            (IReadOnlyList<FlowableTaskSummary>?)tasks ?? Array.Empty<FlowableTaskSummary>());
    }

    // Tests that exercise the projection-framework task polling feed populate
    // RuntimeTasks; the projection upserts each entry into workflow_task_cache.
    public List<FlowableTaskSummary> RuntimeTasks { get; } = new();

    public Task<IReadOnlyList<FlowableTaskSummary>> GetRuntimeTasksAsync(
        int start, int size, CancellationToken cancellationToken = default)
    {
        Calls.Add($"RuntimeTasks:start={start},size={size}");
        if (start >= RuntimeTasks.Count) return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(Array.Empty<FlowableTaskSummary>());
        var page = RuntimeTasks.Skip(start).Take(size).ToArray();
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(page);
    }

    // Tests for the history projection seed this list; the global page method
    // returns entries newest-first and paged by start/size, with NO time filter
    // -- as the engine does. The description of a `sinceUtc` filter is what the
    // stub used to do, and #590 is the bug that caused (#665).
    public List<FlowableHistoricActivityEvent> HistoricActivityEvents { get; } = new();

    /// <summary>
    /// Historic activity events, newest first and <b>unfiltered</b> (#590).
    /// </summary>
    /// <remarks>
    /// <para><b>This stub used to be more capable than the real server, which is
    /// why no test caught #590.</b> It took a <c>sinceUtc</c> and honoured it, so
    /// every assertion written against it confirmed what the code BELIEVED
    /// Flowable did. The engine ignores <c>startedAfter</c> on this collection
    /// entirely — a value of 2030 returns every row.</para>
    ///
    /// <para>It now behaves as the engine does: descending by start time, no time
    /// filter, and the caller is responsible for stopping. A stub that implements
    /// the contract its caller wishes for cannot fail the way production does.</para>
    /// </remarks>
    public Task<IReadOnlyList<FlowableHistoricActivityEvent>> GetHistoricActivityEventsAsync(
        int start, int size, CancellationToken cancellationToken = default)
    {
        Calls.Add($"HistoricActivities:start={start},size={size}");
        var page = HistoricActivityEvents
            .OrderByDescending(e => e.StartTime ?? DateTimeOffset.MinValue)
            .Skip(start)
            .Take(size)
            .ToArray();
        return Task.FromResult<IReadOnlyList<FlowableHistoricActivityEvent>>(page);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUserAsync(
        string userId, IReadOnlyCollection<string> candidateGroups, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksForUserGroups:{userId}:{string.Join(",", candidateGroups)}");
        return GetTasksAssignedToUserAsync(userId, cancellationToken);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUserAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksForUser:{userId}");
        TasksByUser.TryGetValue(userId, out var tasks);
        IReadOnlyList<FlowableTaskSummary> list = tasks?.AsReadOnly() ?? (IReadOnlyList<FlowableTaskSummary>)Array.Empty<FlowableTaskSummary>();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> candidateGroupsByUserId,
        CancellationToken cancellationToken = default)
    {
        foreach (var (userId, groups) in candidateGroupsByUserId)
        {
            Calls.Add($"TasksForUserGroups:{userId}:{string.Join(",", groups)}");
        }
        return GetTasksAssignedToUsersAsync(candidateGroupsByUserId.Keys.ToArray(), cancellationToken);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksForUsers:{string.Join(",", userIds)}");
        var merged = new Dictionary<string, FlowableTaskSummary>(StringComparer.Ordinal);
        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            if (!TasksByUser.TryGetValue(userId, out var tasks)) continue;
            foreach (var t in tasks)
            {
                merged.TryAdd(t.Id, t);
            }
        }
        IReadOnlyList<FlowableTaskSummary> list = merged.Values.ToArray();
        return Task.FromResult(list);
    }

    /// <summary>
    /// Set to make <see cref="CompleteTaskAsync"/> throw as the real client does
    /// for a task the engine has already finished (#604).
    /// </summary>
    /// <remarks>
    /// Flowable answers `404 Could not find a task with id '…'`, and without a way
    /// to drive that the 409 branch could not be tested at all -- which is how it
    /// reached callers as a 500 in the first place.
    /// </remarks>
    public FlowableRequestException? CompleteThrows { get; set; }

    public Task CompleteTaskAsync(
        string taskId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CompleteTask:{taskId}");
        if (CompleteThrows is not null) throw CompleteThrows;
        return Task.CompletedTask;
    }

    public Dictionary<string, string?> TaskAssigneesByTaskId { get; } = new();

    public Task UpdateTaskAssigneeAsync(
        string taskId,
        string? assignee,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateTaskAssignee:{taskId}:{assignee ?? "(null)"}");
        TaskAssigneesByTaskId[taskId] = assignee;
        return Task.CompletedTask;
    }

    public Dictionary<string, DateTimeOffset?> TaskDueDatesByTaskId { get; } = new();

    public Task UpdateTaskDueDateAsync(
        string taskId,
        DateTimeOffset? dueDate,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateTaskDueDate:{taskId}:{dueDate?.ToString("O") ?? "(null)"}");
        TaskDueDatesByTaskId[taskId] = dueDate;
        return Task.CompletedTask;
    }

    public Dictionary<string, List<ProcessVariableUpdate>> VariableUpdatesByInstance { get; } = new();

    public Dictionary<(string ProcessInstanceId, string ActivityId), List<string>> CompletedAssigneesByActivity { get; } = new();

    public List<(string SignalName, IReadOnlyDictionary<string, object?>? Variables)> BroadcastedSignals { get; } = new();

    // Set to make BroadcastSignalAsync throw — tests for the dispatcher's
    // error-swallowing path use this to simulate Flowable being unreachable
    // without needing a separate IFlowableClient implementation.
    public Exception? BroadcastSignalThrows { get; set; }

    public Task BroadcastSignalAsync(
        string signalName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"BroadcastSignal:{signalName}");
        BroadcastedSignals.Add((signalName, variables));
        if (BroadcastSignalThrows is not null)
        {
            throw BroadcastSignalThrows;
        }
        return Task.CompletedTask;
    }

    public List<(string ExecutionId, IReadOnlyDictionary<string, object?>? Variables)> SignalledExecutions { get; } = new();

    public Task SignalExecutionAsync(
        string executionId,
        string signalName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        // #262. The engine rejects a wake with no signal name, so a stub that
        // accepts one would hide exactly the defect that shipped.
        if (string.IsNullOrWhiteSpace(signalName))
        {
            throw new InvalidOperationException(
                "Flowable answers 400 'Signal name is required' when signalName is absent.");
        }

        Calls.Add($"SignalExecution:{executionId}:{signalName}");
        SignalledExecutions.Add((executionId, variables));
        return Task.CompletedTask;
    }

    // Tests can seed this to control which execution ids the dispatcher sees
    // when it asks Flowable who is parked on a given intermediate signal
    // catch. Keys are matched ordinally; missing keys yield an empty list.
    public Dictionary<string, IReadOnlyList<string>> WaitingExecutionsBySignal { get; } =
        new(StringComparer.Ordinal);

    // #112. Keyed by the addressable name — the message name for a message
    // event, the element id for a receive task, which is how the correlator
    // names them too.
    public Dictionary<string, IReadOnlyList<string>> WaitingExecutionsByMessage { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> ListExecutionsAwaitingMessageAsync(
        string processDefinitionKey,
        string messageName,
        string? correlationKey,
        string? correlationValue,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListExecutionsAwaitingMessage:{processDefinitionKey}:{messageName}:{correlationKey}={correlationValue}");
        return Task.FromResult(WaitingExecutionsByMessage.TryGetValue(messageName, out var ids)
            ? ids
            : (IReadOnlyList<string>)Array.Empty<string>());
    }

    public Task<IReadOnlyList<string>> ListExecutionsAwaitingReceiveTaskAsync(
        string processDefinitionKey,
        string activityId,
        string? correlationKey,
        string? correlationValue,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListExecutionsAwaitingReceiveTask:{processDefinitionKey}:{activityId}:{correlationKey}={correlationValue}");
        return Task.FromResult(WaitingExecutionsByMessage.TryGetValue(activityId, out var ids)
            ? ids
            : (IReadOnlyList<string>)Array.Empty<string>());
    }

    public Task DeliverMessageToExecutionAsync(
        string executionId,
        string messageName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"DeliverMessageToExecution:{executionId}:{messageName}");
        return Task.CompletedTask;
    }

    public Task TriggerExecutionAsync(
        string executionId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"TriggerExecution:{executionId}");
        return Task.CompletedTask;
    }

    public string StartedByMessageInstanceId { get; set; } = "started-by-message";

    public Dictionary<string, IReadOnlyList<FlowableProcessInstanceSummary>> ChildInstances { get; } =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<FlowableProcessInstanceSummary>> GetChildProcessInstancesAsync(
        string parentProcessInstanceId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetChildProcessInstances:{parentProcessInstanceId}");
        return Task.FromResult(ChildInstances.TryGetValue(parentProcessInstanceId, out var children)
            ? children
            : (IReadOnlyList<FlowableProcessInstanceSummary>)Array.Empty<FlowableProcessInstanceSummary>());
    }

    public Task<string> StartProcessInstanceByMessageAsync(
        string messageName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"StartProcessInstanceByMessage:{messageName}");
        return Task.FromResult(StartedByMessageInstanceId);
    }

    public Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
        string signalName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListExecutionsBySignalSubscription:{signalName}");
        return Task.FromResult(WaitingExecutionsBySignal.TryGetValue(signalName, out var ids)
            ? ids
            : (IReadOnlyList<string>)Array.Empty<string>());
    }

    // #158. Recorded rather than ignored: the tests that matter here assert this
    // was called AFTER a variable write, because Flowable does not re-evaluate
    // conditional events on its own and a process parked on an already-true
    // condition is indistinguishable from a broken feature.
    /// <summary>
    /// Set to make the nudge fail (#382).
    /// </summary>
    /// <remarks>
    /// Without this there was no way to express the scenario #376 exists for. The
    /// two <c>/variables</c> routes call the THROWING overload deliberately, so a
    /// nudge failure surfaces as a 500 -- and the audit record of the write must
    /// already have been published by then, because the variables are written
    /// before either. Ordered the other way the write happened, the caller saw a
    /// 500, and nothing recorded it. A stub that can only succeed cannot tell the
    /// two orderings apart, which is why reverting #376 left 30/30 green.
    /// </remarks>
    public Exception? EvaluateConditionalEventsThrows { get; set; }

    public Task EvaluateConditionalEventsAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"EvaluateConditionalEvents:{processInstanceId}");
        return EvaluateConditionalEventsThrows is { } boom
            ? Task.FromException(boom)
            : Task.CompletedTask;
    }

    public Task UpdateProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateVariables:{processInstanceId}");
        if (!VariableUpdatesByInstance.TryGetValue(processInstanceId, out var list))
        {
            list = new List<ProcessVariableUpdate>();
            VariableUpdatesByInstance[processInstanceId] = list;
        }
        list.AddRange(updates);
        return Task.CompletedTask;
    }

    public Dictionary<string, List<ProcessVariableUpdate>> VariableAdditionsByInstance { get; } = new();

    /// <summary>#226. When set, AddProcessVariablesAsync throws it.</summary>
    public Exception? AddVariablesFailure { get; set; }

    public Task AddProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> additions,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"AddVariables:{processInstanceId}");
        // #226. Lets a test stand in for Flowable answering 409 for a variable
        // that already exists, so the endpoint's status mapping is exercised
        // without needing the engine.
        if (AddVariablesFailure is not null) throw AddVariablesFailure;
        if (!VariableAdditionsByInstance.TryGetValue(processInstanceId, out var list))
        {
            list = new List<ProcessVariableUpdate>();
            VariableAdditionsByInstance[processInstanceId] = list;
        }
        list.AddRange(additions);
        return Task.CompletedTask;
    }

    public List<(string ProcessInstanceId, string TargetActivityId)> MoveExecutionStateCalls { get; } = new();

    // Set to make MoveWorkflowExecutionStateAsync throw — tests for the
    // "no active activities" guard case use this to simulate Flowable
    // rejecting an empty cancel list without seeding history rows.
    public Exception? MoveExecutionStateThrows { get; set; }

    public Task MoveWorkflowExecutionStateAsync(
        string processInstanceId,
        string targetActivityId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"MoveExecutionState:{processInstanceId}:{targetActivityId}");
        MoveExecutionStateCalls.Add((processInstanceId, targetActivityId));
        if (MoveExecutionStateThrows is not null)
        {
            throw MoveExecutionStateThrows;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetCompletedAssigneesForActivityAsync(
        string processInstanceId,
        string activityId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CompletedAssignees:{processInstanceId}:{activityId}");
        CompletedAssigneesByActivity.TryGetValue((processInstanceId, activityId), out var list);
        IReadOnlyList<string> result = list?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Dictionary<string, FlowableTaskSummary> TasksById { get; } = new();
    public Dictionary<string, Dictionary<string, JsonElement>> VariablesByProcessInstanceId { get; } = new();

    public Task<FlowableTaskSummary?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetTask:{taskId}");
        TasksById.TryGetValue(taskId, out var task);
        return Task.FromResult<FlowableTaskSummary?>(task);
    }

    public Task<IReadOnlyDictionary<string, JsonElement>> GetProcessInstanceVariablesAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetVariables:{processInstanceId}");
        VariablesByProcessInstanceId.TryGetValue(processInstanceId, out var variables);
        IReadOnlyDictionary<string, JsonElement> result =
            variables ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return Task.FromResult(result);
    }
}
