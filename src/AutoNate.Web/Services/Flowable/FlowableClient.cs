using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable;

public sealed class FlowableClient(
    HttpClient httpClient,
    IOptions<FlowableOptions> options,
    IMemoryCache cache) : IFlowableClient
{
    private const int WorkflowExecutionQuerySize = 200;
    private const int WorkflowExecutionActivityQuerySize = 2000;
    private const string ProcessDefinitionNameCacheKeyPrefix = "flowable:process-definition-name:";

    // Process definitions are immutable per (key, version) in Flowable — a
    // redeploy produces a new id, so once we've resolved id → name it never
    // changes. Sliding TTL is plenty; the eviction is really about bounding
    // memory if a deployment churns many definitions.
    private static readonly TimeSpan ProcessDefinitionNameCacheTtl = TimeSpan.FromHours(24);

    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly FlowableOptions _options = options.Value;
    private readonly IMemoryCache _cache = cache;

    public async Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowModel model, CancellationToken cancellationToken = default)
    {
        if (ContainsScriptTask(model.BpmnXml))
        {
            await EnsureJavaScriptScriptTaskSupportAsync(cancellationToken);
        }

        using var content = new MultipartFormDataContent();
        var fileName = $"{model.ProcessKey}.bpmn20.xml";
        content.Add(new StringContent(model.BpmnXml, Encoding.UTF8, "application/xml"), "file", fileName);

        using var response = await _httpClient.PostAsync("service/repository/deployments", content, cancellationToken);
        await EnsureSuccessAsync(response, "deploy the BPMN workflow");

        var processDefinition = await GetLatestProcessDefinitionAsync(model.ProcessKey, cancellationToken)
            ?? throw new InvalidOperationException($"Flowable accepted the deployment, but no process definition with key '{model.ProcessKey}' was found.");

        return new WorkflowDeploymentInfo
        {
            DeploymentId = await ReadDeploymentIdAsync(response, cancellationToken),
            ProcessDefinitionId = processDefinition.Id,
            ProcessDefinitionKey = processDefinition.Key,
            ProcessDefinitionVersion = processDefinition.Version,
            DeployedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        var url = $"service/repository/process-definitions?key={Uri.EscapeDataString(processDefinitionKey)}&latest=true";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "query the latest deployed process definition");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableProcessDefinitionResponse>>(response, cancellationToken);
        var definition = payload.Data.FirstOrDefault();

        return definition is null ? null : ToSummary(definition);
    }

    public async Task<IReadOnlyList<FlowableProcessDefinitionSummary>> GetLatestProcessDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        // Page through every latest=true definition. The workflow list page
        // typically has a handful of workflows so a single page (size=200) is
        // almost always sufficient, but we loop for completeness.
        const int pageSize = 200;
        var results = new List<FlowableProcessDefinitionSummary>();
        var start = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"service/repository/process-definitions?latest=true&size={pageSize}&start={start}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, "list the latest deployed process definitions");

            var payload = await DeserializeAsync<FlowableListResponse<FlowableProcessDefinitionResponse>>(response, cancellationToken);
            if (payload.Data.Count == 0)
            {
                return results;
            }

            results.AddRange(payload.Data.Select(ToSummary));

            if (payload.Data.Count < pageSize)
            {
                return results;
            }

            start += payload.Data.Count;
        }
    }

    public Task SuspendProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
        => SetProcessDefinitionStateAsync(processDefinitionKey, "suspend", cancellationToken);

    public Task ActivateProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
        => SetProcessDefinitionStateAsync(processDefinitionKey, "activate", cancellationToken);

    private async Task SetProcessDefinitionStateAsync(string processDefinitionKey, string action, CancellationToken cancellationToken)
    {
        var definition = await GetLatestProcessDefinitionAsync(processDefinitionKey, cancellationToken)
            ?? throw new InvalidOperationException($"No deployed process definition exists for key '{processDefinitionKey}'.");

        // includeProcessInstances=false leaves running executions alone; only
        // the definition itself is flipped, blocking new starts (suspend) or
        // re-allowing them (activate).
        var payload = new
        {
            action,
            includeProcessInstances = false
        };

        using var response = await _httpClient.PutAsJsonAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(definition.Id)}",
            payload,
            cancellationToken);

        await EnsureSuccessAsync(response, $"{action} the process definition '{processDefinitionKey}'");
    }

    private static FlowableProcessDefinitionSummary ToSummary(FlowableProcessDefinitionResponse definition)
        => new()
        {
            Id = definition.Id ?? string.Empty,
            Key = definition.Key ?? string.Empty,
            Name = definition.Name ?? string.Empty,
            Version = definition.Version,
            DeploymentId = definition.DeploymentId ?? string.Empty,
            Suspended = definition.Suspended
        };

    public async Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(string processDefinitionKey, string? name = null, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default)
    {
        // Build the body explicitly so an absent name omits the field rather
        // than sending an explicit null — Flowable treats them differently.
        var payload = new Dictionary<string, object?>
        {
            ["processDefinitionKey"] = processDefinitionKey,
            ["variables"] = ToFlowableVariables(variables)
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            payload["name"] = name;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "service/runtime/process-instances",
            payload,
            cancellationToken);

        await EnsureSuccessAsync(response, "start the process instance");

        var responsePayload = await DeserializeAsync<FlowableProcessInstanceResponse>(response, cancellationToken);
        return new FlowableProcessInstanceSummary
        {
            Id = responsePayload.Id ?? string.Empty,
            Name = string.IsNullOrWhiteSpace(responsePayload.Name) ? null : responsePayload.Name,
            ProcessDefinitionId = responsePayload.ProcessDefinitionId ?? string.Empty,
            ActivityId = responsePayload.ActivityId,
            Suspended = responsePayload.Suspended,
            StartUserId = responsePayload.StartUserId
        };
    }

    public async Task<int> GetHistoricProcessInstanceCountByDefinitionKeyAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        // Smallest possible response — we only need the `total` field, so
        // ask for one row.
        var url = $"service/history/historic-process-instances?processDefinitionKey={Uri.EscapeDataString(processDefinitionKey)}&size=1";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "count historic process instances by definition key");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableHistoricProcessInstanceResponse>>(response, cancellationToken);
        return payload.Total;
    }

    public async Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "query the process instance");
        var payload = await DeserializeAsync<FlowableProcessInstanceResponse>(response, cancellationToken);

        return new FlowableProcessInstanceSummary
        {
            Id = payload.Id ?? string.Empty,
            Name = string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name,
            ProcessDefinitionId = payload.ProcessDefinitionId ?? string.Empty,
            ActivityId = payload.ActivityId,
            Suspended = payload.Suspended,
            StartUserId = payload.StartUserId
        };
    }

    public async Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(CancellationToken cancellationToken = default)
    {
        // Four independent Flowable collections — kick them off concurrently
        // so the wall-clock for the list page is bounded by the slowest, not
        // the sum. Each response is consumed only after the merge below, so
        // overlapping the fetches is safe.
        var historicTask = _httpClient.GetAsync(
            $"service/history/historic-process-instances?sort=startTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        var runtimeTask = _httpClient.GetAsync(
            $"service/runtime/process-instances?sort=startTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        var tasksTask = _httpClient.GetAsync(
            $"service/runtime/tasks?sort=createTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        var activitiesTask = _httpClient.GetAsync(
            $"service/history/historic-activity-instances?sort=startTime&order=desc&size={WorkflowExecutionActivityQuerySize}",
            cancellationToken);

        HttpResponseMessage? historicResponse = null;
        HttpResponseMessage? runtimeResponse = null;
        HttpResponseMessage? tasksResponse = null;
        HttpResponseMessage? activitiesResponse = null;
        try
        {
            try
            {
                await Task.WhenAll(historicTask, runtimeTask, tasksTask, activitiesTask);
            }
            catch
            {
                // WhenAll waits for every task to finish before throwing, so
                // each task is .IsCompleted here. Reclaim responses that
                // succeeded before we let the original exception propagate.
                // await on a completed-successfully task returns synchronously
                // — it's just the way to get the value without tripping the
                // VSTHRD103 analyzer for .Result.
                if (historicTask.IsCompletedSuccessfully) (await historicTask).Dispose();
                if (runtimeTask.IsCompletedSuccessfully) (await runtimeTask).Dispose();
                if (tasksTask.IsCompletedSuccessfully) (await tasksTask).Dispose();
                if (activitiesTask.IsCompletedSuccessfully) (await activitiesTask).Dispose();
                throw;
            }

            historicResponse = await historicTask;
            runtimeResponse = await runtimeTask;
            tasksResponse = await tasksTask;
            activitiesResponse = await activitiesTask;

            await EnsureSuccessAsync(historicResponse, "query historic process instances");
            await EnsureSuccessAsync(runtimeResponse, "query runtime process instances");
            await EnsureSuccessAsync(tasksResponse, "query runtime tasks");
            await EnsureSuccessAsync(activitiesResponse, "query historic activity instances");

            var historicPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricProcessInstanceResponse>>(historicResponse, cancellationToken);
            var runtimePayload = await DeserializeAsync<FlowableListResponse<FlowableProcessInstanceResponse>>(runtimeResponse, cancellationToken);
            var tasksPayload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(tasksResponse, cancellationToken);
            var activitiesPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricActivityInstanceResponse>>(activitiesResponse, cancellationToken);
            var processDefinitionNames = await GetProcessDefinitionNamesByIdAsync(historicPayload.Data, cancellationToken);

            var lastActivityByProcessInstanceId = activitiesPayload.Data
                .Where(activity => !string.IsNullOrWhiteSpace(activity.ProcessInstanceId))
                .GroupBy(activity => activity.ProcessInstanceId!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(activity => MaxTimestamp(activity.EndTime, activity.StartTime))
                        .Where(value => value.HasValue)
                        .DefaultIfEmpty()
                        .Max(),
                    StringComparer.Ordinal);

            var runtimeById = runtimePayload.Data
                .Where(instance => !string.IsNullOrWhiteSpace(instance.Id))
                .ToDictionary(instance => instance.Id!, StringComparer.Ordinal);

            var currentTaskByProcessInstanceId = tasksPayload.Data
                .Where(task => !string.IsNullOrWhiteSpace(task.ProcessInstanceId))
                .GroupBy(task => task.ProcessInstanceId!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(task => task.CreateTime ?? DateTimeOffset.MaxValue)
                        .ThenBy(task => task.Id, StringComparer.Ordinal)
                        .First(),
                    StringComparer.Ordinal);

            return historicPayload.Data
                .Where(instance => !string.IsNullOrWhiteSpace(instance.Id))
                .Select(instance =>
                {
                    runtimeById.TryGetValue(instance.Id!, out var runtimeInstance);
                    currentTaskByProcessInstanceId.TryGetValue(instance.Id!, out var currentTask);
                    processDefinitionNames.TryGetValue(instance.ProcessDefinitionId ?? string.Empty, out var workflowModelName);

                    var isRunning = runtimeInstance is not null;
                    var currentStep = isRunning
                        ? FirstNonEmpty(currentTask?.Name, runtimeInstance?.ActivityId)
                        : null;

                    lastActivityByProcessInstanceId.TryGetValue(instance.Id!, out var lastActivityAtUtc);
                    var lastActivityAt = MaxTimestamp(
                        lastActivityAtUtc,
                        instance.EndTime,
                        currentTask?.CreateTime,
                        instance.StartTime);

                    // Any non-empty DeleteReason on a finished process means it
                    // was torn down (cancelled) rather than reaching an end event.
                    // We don't pin the exact reason string — Delete fully removes
                    // the historic record, so the only reasons that survive here
                    // are cancellations (ours or operator-issued).
                    var status = isRunning
                        ? "Running"
                        : !string.IsNullOrWhiteSpace(instance.DeleteReason)
                            ? "Cancelled"
                            : "Complete";

                    return new WorkflowExecutionSummary
                    {
                        Id = instance.Id!,
                        Name = string.IsNullOrWhiteSpace(instance.Name) ? null : instance.Name,
                        WorkflowModelName = workflowModelName,
                        StartedAtUtc = instance.StartTime,
                        LastActivityAtUtc = lastActivityAt,
                        Status = status,
                        CurrentStep = currentStep,
                        ProcessDefinitionId = string.IsNullOrWhiteSpace(instance.ProcessDefinitionId)
                            ? null
                            : instance.ProcessDefinitionId,
                        StartUserId = string.IsNullOrWhiteSpace(instance.StartUserId)
                            ? null
                            : instance.StartUserId
                    };
                })
                .OrderByDescending(execution => execution.StartedAtUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(execution => execution.Id, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            historicResponse?.Dispose();
            runtimeResponse?.Dispose();
            tasksResponse?.Dispose();
            activitiesResponse?.Dispose();
        }
    }

    private async Task<Dictionary<string, string>> GetProcessDefinitionNamesByIdAsync(
        IReadOnlyList<FlowableHistoricProcessInstanceResponse> historicInstances,
        CancellationToken cancellationToken)
    {
        var processDefinitionIds = historicInstances
            .Select(instance => instance.ProcessDefinitionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var namesById = new Dictionary<string, string>(StringComparer.Ordinal);
        if (processDefinitionIds.Length == 0)
        {
            return namesById;
        }

        // Hit the cache first. Process-definition id → name is stable for the
        // lifetime of a deployment (Flowable produces a new id on redeploy),
        // so warmed entries are reusable across requests.
        var misses = new List<string>();
        foreach (var processDefinitionId in processDefinitionIds)
        {
            var key = ProcessDefinitionNameCacheKeyPrefix + processDefinitionId;
            if (_cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            {
                namesById[processDefinitionId!] = cached!;
            }
            else
            {
                misses.Add(processDefinitionId!);
            }
        }

        if (misses.Count == 0)
        {
            return namesById;
        }

        // Fan misses out in parallel — the previous serial foreach turned a
        // distinct-definitions-per-page count into that many round trips
        // stacked end to end.
        var fetchTasks = misses
            .Select(id => FetchProcessDefinitionNameAsync(id, cancellationToken))
            .ToArray();
        var resolved = await Task.WhenAll(fetchTasks);

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ProcessDefinitionNameCacheTtl
        };
        for (var i = 0; i < misses.Count; i++)
        {
            var name = resolved[i];
            if (string.IsNullOrWhiteSpace(name)) continue;
            namesById[misses[i]] = name!;
            _cache.Set(ProcessDefinitionNameCacheKeyPrefix + misses[i], name, cacheEntryOptions);
        }
        return namesById;
    }

    private async Task<string?> FetchProcessDefinitionNameAsync(
        string processDefinitionId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(processDefinitionId)}",
            cancellationToken);
        await EnsureSuccessAsync(response, $"query process definition '{processDefinitionId}'");
        var processDefinition = await DeserializeAsync<FlowableProcessDefinitionResponse>(response, cancellationToken);
        return FirstNonEmpty(processDefinition.Name, processDefinition.Key, processDefinition.Id);
    }

    public async Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        using var processInstanceResponse = await _httpClient.GetAsync($"service/history/historic-process-instances/{Uri.EscapeDataString(processInstanceId)}", cancellationToken);
        await EnsureSuccessAsync(processInstanceResponse, "query the historic process instance");

        var processInstance = await DeserializeAsync<FlowableHistoricProcessInstanceResponse>(processInstanceResponse, cancellationToken);
        if (string.IsNullOrWhiteSpace(processInstance.ProcessDefinitionId))
        {
            throw new InvalidOperationException($"Execution '{processInstanceId}' does not have a process definition id.");
        }

        using var processDefinitionResponse = await _httpClient.GetAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(processInstance.ProcessDefinitionId)}",
            cancellationToken);
        await EnsureSuccessAsync(processDefinitionResponse, "query the process definition");

        var processDefinition = await DeserializeAsync<FlowableProcessDefinitionResponse>(processDefinitionResponse, cancellationToken);

        if (!processDefinition.GraphicalNotationDefined)
        {
            var processLabel = FirstNonEmpty(processDefinition.Name, processDefinition.Key, processDefinition.Id, processInstance.ProcessDefinitionId)
                ?? processInstance.ProcessDefinitionId;

            throw new InvalidOperationException(
                $"Execution '{processInstanceId}' belongs to process '{processLabel}', which was deployed without BPMN diagram notation. It can run, but Flowable cannot provide a visual diagram for it.");
        }

        using var modelResponse = await _httpClient.GetAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(processInstance.ProcessDefinitionId)}/resourcedata",
            cancellationToken);
        await EnsureSuccessAsync(modelResponse, "load the BPMN model for the execution");

        using var activitiesResponse = await _httpClient.GetAsync(
            $"service/history/historic-activity-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}&size={WorkflowExecutionActivityQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(activitiesResponse, "query historic activity instances");

        using var variablesResponse = await _httpClient.GetAsync(
            $"service/history/historic-variable-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(variablesResponse, "query historic process variables");

        var bpmnXml = await modelResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureDiagramXmlPresent(bpmnXml, processInstanceId, processDefinition);
        var activitiesPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricActivityInstanceResponse>>(activitiesResponse, cancellationToken);
        var variablesPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricVariableInstanceResponse>>(variablesResponse, cancellationToken);

        // A non-empty DeleteReason means the process was torn down rather
        // than completing through an end event. For each in-flight activity
        // Flowable usually propagates that DeleteReason; in versions where
        // the REST history response omits the per-activity field we fall
        // back to "ended within a few seconds of the process EndTime", which
        // is the only window in which a cancellation can land them.
        var isCancelled = !string.IsNullOrWhiteSpace(processInstance.DeleteReason);
        var cancelWindow = TimeSpan.FromSeconds(5);

        var cancelledActivityIds = isCancelled
            ? activitiesPayload.Data
                .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId)
                                && (
                                    !string.IsNullOrWhiteSpace(activity.DeleteReason)
                                    || (processInstance.EndTime is not null
                                        && activity.EndTime is not null
                                        && (processInstance.EndTime.Value - activity.EndTime.Value).Duration() <= cancelWindow)
                                ))
                .Select(activity => activity.ActivityId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        var cancelledSet = new HashSet<string>(cancelledActivityIds, StringComparer.Ordinal);

        var completedActivityIds = activitiesPayload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId)
                            && activity.EndTime is not null
                            && !cancelledSet.Contains(activity.ActivityId!))
            .Select(activity => activity.ActivityId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var currentActivityIds = activitiesPayload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId) && activity.EndTime is null)
            .Select(activity => activity.ActivityId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (currentActivityIds.Length == 0 && !isCancelled)
        {
            var runtimeInstance = await GetProcessInstanceAsync(processInstanceId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(runtimeInstance?.ActivityId))
            {
                currentActivityIds = [runtimeInstance.ActivityId];
            }
        }

        var variables = variablesPayload.Data
            .Where(entry => entry.Variable is not null && !string.IsNullOrWhiteSpace(entry.Variable.Name))
            .GroupBy(entry => entry.Variable!.Name!, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(entry => entry.LastUpdatedTime ?? entry.CreateTime ?? DateTimeOffset.MinValue)
                .First())
            .OrderBy(entry => entry.Variable!.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new FlowableProcessVariable
            {
                Name = entry.Variable!.Name!,
                Type = entry.Variable.Type,
                Value = FormatVariableValue(entry.Variable.Value)
            })
            .ToArray();

        return new WorkflowExecutionDiagramDetail
        {
            ExecutionId = processInstanceId,
            Name = string.IsNullOrWhiteSpace(processInstance.Name) ? null : processInstance.Name,
            BpmnXml = bpmnXml,
            CompletedActivityIds = completedActivityIds,
            CurrentActivityIds = currentActivityIds,
            CancelledActivityIds = cancelledActivityIds,
            Variables = variables
        };
    }

    public async Task<IReadOnlyList<WorkflowExecutionHistoryEvent>> GetWorkflowExecutionHistoryAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"service/history/historic-activity-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}&sort=startTime&order=asc&size={WorkflowExecutionActivityQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(response, "query historic activity instances");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableHistoricActivityInstanceResponse>>(response, cancellationToken);

        return payload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId))
            .Select(activity => new WorkflowExecutionHistoryEvent
            {
                ActivityId = activity.ActivityId!,
                ActivityName = string.IsNullOrWhiteSpace(activity.ActivityName) ? null : activity.ActivityName,
                ActivityType = string.IsNullOrWhiteSpace(activity.ActivityType) ? null : activity.ActivityType,
                StartedAtUtc = activity.StartTime,
                EndedAtUtc = activity.EndTime,
                DurationMs = activity.DurationInMillis,
                Assignee = string.IsNullOrWhiteSpace(activity.Assignee) ? null : activity.Assignee,
                TaskId = string.IsNullOrWhiteSpace(activity.TaskId) ? null : activity.TaskId,
                DeleteReason = string.IsNullOrWhiteSpace(activity.DeleteReason) ? null : activity.DeleteReason
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<WorkflowExecutionLogEntry>> GetWorkflowExecutionLogAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(processInstanceId);

        // Variable updates — Flowable's "selectOnlyVariableUpdates=true" filters
        // out form-property records so we only get state changes.
        using var detailResponse = await _httpClient.GetAsync(
            $"service/history/historic-detail?processInstanceId={encodedId}&selectOnlyVariableUpdates=true&size={WorkflowExecutionActivityQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(detailResponse, "query historic detail (variable updates)");

        // User task lifecycle — start, claim, end with assignee/owner/dueDate.
        using var taskResponse = await _httpClient.GetAsync(
            $"service/history/historic-task-instances?processInstanceId={encodedId}&size={WorkflowExecutionActivityQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(taskResponse, "query historic task instances");

        var detailPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricDetailResponse>>(detailResponse, cancellationToken);
        var taskPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricTaskResponse>>(taskResponse, cancellationToken);

        var entries = new List<WorkflowExecutionLogEntry>(detailPayload.Data.Count + taskPayload.Data.Count * 3);

        foreach (var detail in detailPayload.Data)
        {
            var variable = detail.Variable;
            if (variable is null || string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            entries.Add(new WorkflowExecutionLogEntry
            {
                Kind = "variable-update",
                OccurredAtUtc = detail.Time,
                VariableUpdate = new WorkflowExecutionLogVariableUpdate
                {
                    Name = variable.Name!,
                    Type = string.IsNullOrWhiteSpace(variable.Type) ? null : variable.Type,
                    Value = FormatVariableValue(variable.Value),
                    Revision = detail.Revision,
                    TaskId = string.IsNullOrWhiteSpace(detail.TaskId) ? null : detail.TaskId,
                    ActivityInstanceId = string.IsNullOrWhiteSpace(detail.ActivityInstanceId) ? null : detail.ActivityInstanceId
                }
            });
        }

        foreach (var task in taskPayload.Data)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                continue;
            }

            var taskInfo = new WorkflowExecutionLogTask
            {
                TaskId = task.Id!,
                Name = string.IsNullOrWhiteSpace(task.Name) ? null : task.Name,
                TaskDefinitionKey = string.IsNullOrWhiteSpace(task.TaskDefinitionKey) ? null : task.TaskDefinitionKey,
                Assignee = string.IsNullOrWhiteSpace(task.Assignee) ? null : task.Assignee,
                Owner = string.IsNullOrWhiteSpace(task.Owner) ? null : task.Owner,
                FormKey = string.IsNullOrWhiteSpace(task.FormKey) ? null : task.FormKey,
                Priority = task.Priority,
                DueAtUtc = task.DueDate,
                DeleteReason = string.IsNullOrWhiteSpace(task.DeleteReason) ? null : task.DeleteReason
            };

            if (task.StartTime is not null)
            {
                entries.Add(new WorkflowExecutionLogEntry
                {
                    Kind = "task-created",
                    OccurredAtUtc = task.StartTime,
                    Task = taskInfo
                });
            }

            // Only emit "claimed" if the claim happened distinct from the
            // task's creation. Tasks pre-assigned via BPMN often have
            // ClaimTime equal to (or absent vs.) StartTime.
            if (task.ClaimTime is not null
                && task.StartTime is not null
                && (task.ClaimTime.Value - task.StartTime.Value).Duration() > TimeSpan.FromSeconds(1))
            {
                entries.Add(new WorkflowExecutionLogEntry
                {
                    Kind = "task-claimed",
                    OccurredAtUtc = task.ClaimTime,
                    Task = taskInfo
                });
            }

            if (task.EndTime is not null)
            {
                entries.Add(new WorkflowExecutionLogEntry
                {
                    Kind = string.IsNullOrWhiteSpace(task.DeleteReason) ? "task-completed" : "task-cancelled",
                    OccurredAtUtc = task.EndTime,
                    Task = taskInfo
                });
            }
        }

        return entries
            .OrderBy(e => e.OccurredAtUtc ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static string? FormatVariableValue(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind == JsonValueKind.Undefined || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.Value.GetRawText()
        };
    }

    public async Task DeleteWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        var encodedProcessInstanceId = Uri.EscapeDataString(processInstanceId);
        var runtimeInstance = await GetProcessInstanceAsync(processInstanceId, cancellationToken);

        if (runtimeInstance is not null)
        {
            var deleteReason = Uri.EscapeDataString("Deleted from AutoNate workflow executions page");
            using var runtimeDeleteResponse = await _httpClient.DeleteAsync(
                $"service/runtime/process-instances/{encodedProcessInstanceId}?deleteReason={deleteReason}",
                cancellationToken);

            if (runtimeDeleteResponse.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(runtimeDeleteResponse, "delete the running process instance");
            }
        }

        using var historyDeleteResponse = await _httpClient.DeleteAsync(
            $"service/history/historic-process-instances/{encodedProcessInstanceId}",
            cancellationToken);

        if (historyDeleteResponse.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(historyDeleteResponse, "delete the historic process instance");
        }
    }

    public async Task<int> DeleteAllWorkflowExecutionsAsync(CancellationToken cancellationToken = default)
    {
        // Page through historic instances (covers both running and finished
        // — Flowable mirrors runtime rows into history) and delete each one
        // through the per-instance path so the runtime + history pair is
        // handled identically. Loop until a page returns empty rather than
        // trusting `total`, which can shift while we're deleting.
        const int pageSize = 200;
        var deleted = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pageResponse = await _httpClient.GetAsync(
                $"service/history/historic-process-instances?size={pageSize}",
                cancellationToken);
            await EnsureSuccessAsync(pageResponse, "list historic process instances for bulk delete");

            var page = await DeserializeAsync<FlowableListResponse<FlowableHistoricProcessInstanceResponse>>(
                pageResponse, cancellationToken);

            if (page.Data.Count == 0)
            {
                return deleted;
            }

            foreach (var instance in page.Data)
            {
                if (string.IsNullOrWhiteSpace(instance.Id))
                {
                    continue;
                }

                await DeleteWorkflowExecutionAsync(instance.Id, cancellationToken);
                deleted++;
            }
        }
    }

    // Cancellation flag stored as Flowable's `deleteReason` on the historic
    // record so GetWorkflowExecutionsAsync can render the row as "Cancelled".
    // Keep this string stable — the listing read-back compares against it.
    private const string CancelDeleteReason = "Cancelled from AutoNate workflow executions page";

    public async Task CancelWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        var encodedProcessInstanceId = Uri.EscapeDataString(processInstanceId);
        var runtimeInstance = await GetProcessInstanceAsync(processInstanceId, cancellationToken);

        // Already finished — nothing to cancel.
        if (runtimeInstance is null)
        {
            return;
        }

        var deleteReason = Uri.EscapeDataString(CancelDeleteReason);
        using var runtimeDeleteResponse = await _httpClient.DeleteAsync(
            $"service/runtime/process-instances/{encodedProcessInstanceId}?deleteReason={deleteReason}",
            cancellationToken);

        if (runtimeDeleteResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(runtimeDeleteResponse, "cancel the running process instance");
    }

    public async Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        var url = $"service/runtime/tasks?processInstanceId={Uri.EscapeDataString(processInstanceId)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "query runtime tasks");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(response, cancellationToken);
        var processInstanceNames = await GetProcessInstanceNamesByIdAsync(payload.Data, cancellationToken);
        return payload.Data
            .Select(task =>
            {
                processInstanceNames.TryGetValue(task.ProcessInstanceId ?? string.Empty, out var instanceName);
                return new FlowableTaskSummary
                {
                    Id = task.Id ?? string.Empty,
                    Name = task.Name ?? string.Empty,
                    TaskDefinitionKey = task.TaskDefinitionKey,
                    Assignee = task.Assignee,
                    ProcessInstanceId = task.ProcessInstanceId,
                    ProcessInstanceName = FirstNonEmpty(task.ProcessInstanceName, instanceName),
                    ProcessDefinitionId = task.ProcessDefinitionId,
                    CreatedAtUtc = task.CreateTime,
                    DueDate = task.DueDate
                };
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var encodedUserId = Uri.EscapeDataString(userId);
        var assigneeUrl = $"service/runtime/tasks?assignee={encodedUserId}&sort=createTime&order=desc&size={WorkflowExecutionQuerySize}";
        var candidateUrl = $"service/runtime/tasks?candidateUser={encodedUserId}&sort=createTime&order=desc&size={WorkflowExecutionQuerySize}";

        // Awaiting in declaration order is fine: HttpClient runs both
        // requests concurrently the moment they're started above. Don't
        // touch .Result on completed tasks — leaves a sync-over-async
        // footgun for whoever copies this pattern next.
        var assigneeTask = _httpClient.GetAsync(assigneeUrl, cancellationToken);
        var candidateTask = _httpClient.GetAsync(candidateUrl, cancellationToken);

        using var assigneeResponse = await assigneeTask;
        using var candidateResponse = await candidateTask;

        await EnsureSuccessAsync(assigneeResponse, "query tasks assigned to user");
        await EnsureSuccessAsync(candidateResponse, "query tasks where user is a candidate");

        var assigneePayload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(assigneeResponse, cancellationToken);
        var candidatePayload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(candidateResponse, cancellationToken);

        var mergedById = new Dictionary<string, FlowableTaskResponse>(StringComparer.Ordinal);
        foreach (var task in assigneePayload.Data.Concat(candidatePayload.Data))
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                continue;
            }

            mergedById.TryAdd(task.Id, task);
        }

        var processDefinitionNamesTask = GetProcessDefinitionNamesByIdAsync(mergedById.Values, cancellationToken);
        var processInstanceNamesTask = GetProcessInstanceNamesByIdAsync(mergedById.Values, cancellationToken);

        var processDefinitionNames = await processDefinitionNamesTask;
        var processInstanceNames = await processInstanceNamesTask;

        return mergedById.Values
            .Select(task =>
            {
                processDefinitionNames.TryGetValue(task.ProcessDefinitionId ?? string.Empty, out var definitionName);
                processInstanceNames.TryGetValue(task.ProcessInstanceId ?? string.Empty, out var instanceName);
                return new FlowableTaskSummary
                {
                    Id = task.Id ?? string.Empty,
                    Name = task.Name ?? string.Empty,
                    TaskDefinitionKey = task.TaskDefinitionKey,
                    Assignee = task.Assignee,
                    ProcessInstanceId = task.ProcessInstanceId,
                    ProcessInstanceName = FirstNonEmpty(task.ProcessInstanceName, instanceName),
                    ProcessDefinitionId = task.ProcessDefinitionId,
                    ProcessDefinitionName = definitionName,
                    CreatedAtUtc = task.CreateTime,
                    DueDate = task.DueDate
                };
            })
            .OrderByDescending(task => task.CreatedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();
    }

    // Backfill helper: for any tasks whose runtime response didn't include a
    // processInstanceName, look it up in history. Only fetches the unique
    // ids that need it. Returns an id→name map (entries with no name found
    // or already populated are simply absent).
    private async Task<Dictionary<string, string>> GetProcessInstanceNamesByIdAsync(
        IEnumerable<FlowableTaskResponse> tasks,
        CancellationToken cancellationToken)
    {
        var idsNeedingLookup = tasks
            .Where(task => string.IsNullOrWhiteSpace(task.ProcessInstanceName)
                        && !string.IsNullOrWhiteSpace(task.ProcessInstanceId))
            .Select(task => task.ProcessInstanceId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var namesById = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idsNeedingLookup.Length == 0)
        {
            return namesById;
        }

        var fetched = await Task.WhenAll(idsNeedingLookup.Select(async id =>
        {
            using var response = await _httpClient.GetAsync(
                $"service/history/historic-process-instances/{Uri.EscapeDataString(id)}",
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (id, name: (string?)null);
            }
            await EnsureSuccessAsync(response, $"query historic process instance '{id}'");
            var payload = await DeserializeAsync<FlowableHistoricProcessInstanceResponse>(response, cancellationToken);
            return (id, name: string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name);
        }));

        foreach (var (id, name) in fetched)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                namesById[id] = name!;
            }
        }

        return namesById;
    }

    public async Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return Array.Empty<FlowableTaskSummary>();
        }

        // Fan out per user. Flowable's tasks endpoint is single-assignee, so
        // we issue one request per id in parallel and dedupe by task id.
        var perUser = await Task.WhenAll(userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => GetTasksAssignedToUserAsync(id, cancellationToken)));

        return perUser
            .SelectMany(list => list)
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(t => t.CreatedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<Dictionary<string, string>> GetProcessDefinitionNamesByIdAsync(
        IEnumerable<FlowableTaskResponse> tasks,
        CancellationToken cancellationToken)
    {
        var processDefinitionIds = tasks
            .Select(task => task.ProcessDefinitionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var namesById = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var processDefinitionId in processDefinitionIds)
        {
            using var processDefinitionResponse = await _httpClient.GetAsync(
                $"service/repository/process-definitions/{Uri.EscapeDataString(processDefinitionId!)}",
                cancellationToken);
            await EnsureSuccessAsync(processDefinitionResponse, $"query process definition '{processDefinitionId}'");

            var processDefinition = await DeserializeAsync<FlowableProcessDefinitionResponse>(processDefinitionResponse, cancellationToken);
            var resolvedName = FirstNonEmpty(processDefinition.Name, processDefinition.Key, processDefinition.Id);

            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                namesById[processDefinitionId!] = resolvedName;
            }
        }

        return namesById;
    }

    public async Task CompleteTaskAsync(string taskId, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"service/runtime/tasks/{Uri.EscapeDataString(taskId)}",
            new
            {
                action = "complete",
                variables = ToFlowableVariables(variables)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, "complete the user task");
    }

    public async Task UpdateTaskAssigneeAsync(string taskId, string? assignee, CancellationToken cancellationToken = default)
    {
        // Flowable's PUT /runtime/tasks/{id} updates whatever fields appear in
        // the body, with explicit null clearing the field. We do NOT use the
        // POST .../{id} actions endpoint: Flowable's RestActionRequest only
        // recognizes complete/claim/delegate/resolve, so an "assign" verb
        // either errors or silently no-ops depending on the build.
        var normalized = string.IsNullOrWhiteSpace(assignee) ? null : assignee;

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"service/runtime/tasks/{Uri.EscapeDataString(taskId)}")
        {
            Content = JsonContent.Create(new { assignee = normalized })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "reassign the user task");
    }

    public async Task UpdateTaskDueDateAsync(string taskId, DateTimeOffset? dueDate, CancellationToken cancellationToken = default)
    {
        // PUT /runtime/tasks/{id} replaces the task representation. Flowable
        // serializes a null dueDate as an explicit clear, which matches what
        // the SPA sends when the admin empties the date field.
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"service/runtime/tasks/{Uri.EscapeDataString(taskId)}")
        {
            Content = JsonContent.Create(new { dueDate })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "update the user task due date");
    }

    public async Task UpdateProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var payload = updates.Select(update => update.Type is null
            ? (object)new { name = update.Name, value = update.Value }
            : new { name = update.Name, value = update.Value, type = update.Type }).ToArray();

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/variables")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "update the process variables");
    }

    public async Task AddProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> additions,
        CancellationToken cancellationToken = default)
    {
        if (additions.Count == 0)
        {
            return;
        }

        var payload = additions.Select(addition => addition.Type is null
            ? (object)new { name = addition.Name, value = addition.Value }
            : new { name = addition.Name, value = addition.Value, type = addition.Type }).ToArray();

        using var response = await _httpClient.PostAsJsonAsync(
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/variables",
            payload,
            cancellationToken);

        await EnsureSuccessAsync(response, "create the process variables");
    }

    public async Task MoveWorkflowExecutionStateAsync(
        string processInstanceId,
        string targetActivityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetActivityId))
        {
            throw new ArgumentException("Target activity id must be provided.", nameof(targetActivityId));
        }

        // Cancel everything currently in flight (no end time on the historic
        // activity row) and start a fresh execution token at the target.
        // Flowable's change-state API requires both lists in one call so the
        // move happens atomically — partial moves leak runtime tokens.
        var activitiesUrl =
            $"service/history/historic-activity-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}&finished=false&size={WorkflowExecutionActivityQuerySize}";
        using var activitiesResponse = await _httpClient.GetAsync(activitiesUrl, cancellationToken);
        await EnsureSuccessAsync(activitiesResponse, "list active activities for change-state");

        var activitiesPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricActivityInstanceResponse>>(
            activitiesResponse, cancellationToken);

        var cancelActivityIds = activitiesPayload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId) && activity.EndTime is null)
            .Select(activity => activity.ActivityId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (cancelActivityIds.Length == 0)
        {
            // No tokens to move — Flowable's change-state would 400 on an
            // empty cancel list. Surface a useful message; the SPA only
            // exposes this action while a run is in flight, so reaching here
            // means the runtime state changed under the operator.
            throw new InvalidOperationException(
                $"Execution '{processInstanceId}' has no active activities to move — the run may have already finished.");
        }

        var payload = new
        {
            cancelActivityIds,
            startActivityIds = new[] { targetActivityId }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/change-state",
            payload,
            cancellationToken);
        await EnsureSuccessAsync(response, $"move execution '{processInstanceId}' to activity '{targetActivityId}'");
    }

    public async Task BroadcastSignalAsync(
        string signalName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["signalName"] = signalName,
            ["async"] = false,
            ["variables"] = ToFlowableVariables(variables)
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "service/runtime/signals",
            payload,
            cancellationToken);

        await EnsureSuccessAsync(response, $"broadcast signal '{signalName}'");
    }

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

    public async Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
        string signalName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signalName))
        {
            return Array.Empty<string>();
        }

        using var response = await _httpClient.GetAsync(
            $"service/runtime/executions?signalEventSubscriptionName={Uri.EscapeDataString(signalName)}",
            cancellationToken);

        await EnsureSuccessAsync(response, $"list executions waiting on '{signalName}'");

        var page = await DeserializeAsync<FlowableListResponse<FlowableExecutionResponse>>(response, cancellationToken);
        if (page.Data is null || page.Data.Count == 0)
        {
            return Array.Empty<string>();
        }

        return page.Data
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id!)
            .ToArray();
    }

    public async Task<FlowableTaskSummary?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(
            $"service/runtime/tasks/{Uri.EscapeDataString(taskId)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "fetch runtime task");

        var task = await DeserializeAsync<FlowableTaskResponse>(response, cancellationToken);
        if (task is null || string.IsNullOrWhiteSpace(task.Id))
        {
            return null;
        }

        // Backfill the per-instance display name if Flowable's task DTO
        // didn't include it (older versions return null on the task and
        // expose it only on the process-instance resource).
        var processInstanceName = task.ProcessInstanceName;
        if (string.IsNullOrWhiteSpace(processInstanceName) && !string.IsNullOrWhiteSpace(task.ProcessInstanceId))
        {
            var instance = await GetProcessInstanceAsync(task.ProcessInstanceId, cancellationToken);
            processInstanceName = instance?.Name;
        }

        return new FlowableTaskSummary
        {
            Id = task.Id,
            Name = task.Name ?? string.Empty,
            TaskDefinitionKey = task.TaskDefinitionKey,
            Assignee = task.Assignee,
            ProcessInstanceId = task.ProcessInstanceId,
            ProcessInstanceName = processInstanceName,
            ProcessDefinitionId = task.ProcessDefinitionId,
            CreatedAtUtc = task.CreateTime,
            DueDate = task.DueDate
        };
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetProcessInstanceVariablesAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processInstanceId))
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        using var response = await _httpClient.GetAsync(
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/variables",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        await EnsureSuccessAsync(response, "fetch process instance variables");

        // Flowable returns this endpoint as a bare JSON array (not the
        // standard {data, total} wrapper) so we deserialize it directly.
        var variables = await response.Content.ReadFromJsonAsync<FlowableRuntimeVariableResponse[]>(cancellationToken: cancellationToken)
            ?? Array.Empty<FlowableRuntimeVariableResponse>();

        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name) || variable.Value is null)
            {
                continue;
            }
            result[variable.Name] = variable.Value.Value;
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> GetCompletedAssigneesForActivityAsync(
        string processInstanceId,
        string activityId,
        CancellationToken cancellationToken = default)
    {
        var url = $"service/history/historic-task-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}"
                  + $"&taskDefinitionKey={Uri.EscapeDataString(activityId)}&finished=true&size={WorkflowExecutionQuerySize}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "query historic task instances");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableHistoricTaskResponse>>(response, cancellationToken);
        return payload.Data
            .Select(task => task.Assignee)
            .Where(assignee => !string.IsNullOrWhiteSpace(assignee))
            .Select(assignee => assignee!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> ReadDeploymentIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await DeserializeAsync<FlowableDeploymentResponse>(response, cancellationToken);
        return payload.Id ?? string.Empty;
    }

    private async Task EnsureJavaScriptScriptTaskSupportAsync(CancellationToken cancellationToken)
    {
        var probeResult = await TryReadJavaScriptScriptTaskSupportAsync(cancellationToken);
        if (probeResult is null)
        {
            throw new InvalidOperationException(
                "This Flowable runtime does not expose the AutoNate script task capability probe. " +
                "Run the infrastructure startup path again so the updated Flowable image is rebuilt and restarted.");
        }

        if (probeResult.JavaScriptSupported)
        {
            return;
        }

        var engineList = probeResult.EngineNames.Count == 0
            ? "no installed script engines"
            : string.Join(", ", probeResult.EngineNames);

        throw new InvalidOperationException(
            $"Flowable is missing JavaScript script task support. Available script engines: {engineList}. " +
            "Install a JavaScript JSR-223 engine in the Flowable runtime before publishing BPMN script tasks.");
    }

    private async Task<FlowableScriptTaskSupportResponse?> TryReadJavaScriptScriptTaskSupportAsync(CancellationToken cancellationToken)
    {
        var probeUrls = new[]
        {
            "actuator/scriptTaskSupport",
            "service/autonate/script-task-support"
        };

        foreach (var probeUrl in probeUrls)
        {
            using var response = await _httpClient.GetAsync(probeUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (IsMissingProbeEndpoint(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)))
                {
                    continue;
                }

                await EnsureSuccessAsync(response, "verify JavaScript script task runtime support");
            }

            return await DeserializeAsync<FlowableScriptTaskSupportResponse>(response, cancellationToken);
        }

        return null;
    }

    private static bool IsMissingProbeEndpoint(HttpStatusCode statusCode, string? body)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (statusCode != HttpStatusCode.InternalServerError || string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("No endpoint GET", StringComparison.OrdinalIgnoreCase);
    }

    private static object[] ToFlowableVariables(IReadOnlyDictionary<string, object?>? variables)
    {
        return variables?.Select(variable => new { name = variable.Key, value = variable.Value }).ToArray()
            ?? [];
    }

    private static bool ContainsScriptTask(string bpmnXml)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml))
        {
            return false;
        }

        var document = XDocument.Parse(bpmnXml);
        return document.Descendants(BpmnNamespace + "scriptTask").Any();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static DateTimeOffset? MaxTimestamp(params DateTimeOffset?[] values)
    {
        DateTimeOffset? max = null;
        foreach (var value in values)
        {
            if (value.HasValue && (!max.HasValue || value.Value > max.Value))
            {
                max = value;
            }
        }

        return max;
    }

    private static void EnsureDiagramXmlPresent(string bpmnXml, string processInstanceId, FlowableProcessDefinitionResponse processDefinition)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml))
        {
            throw new InvalidOperationException(
                $"Flowable returned an empty BPMN resource for execution '{processInstanceId}'.");
        }

        if (!bpmnXml.Contains("<bpmndi:BPMNDiagram", StringComparison.OrdinalIgnoreCase)
            && !bpmnXml.Contains(":BPMNDiagram", StringComparison.OrdinalIgnoreCase)
            && !bpmnXml.Contains("<BPMNDiagram", StringComparison.OrdinalIgnoreCase))
        {
            var processLabel = FirstNonEmpty(processDefinition.Name, processDefinition.Key, processDefinition.Id) ?? "unknown";
            throw new InvalidOperationException(
                $"Execution '{processInstanceId}' belongs to process '{processLabel}', but its BPMN XML does not contain renderable diagram notation.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var details = string.IsNullOrWhiteSpace(body) ? "No response body was returned." : body;
        throw new InvalidOperationException($"Flowable could not {operation}. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Flowable returned an empty response payload.");
    }

    public static void ConfigureHttpClient(HttpClient httpClient, FlowableOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("Flowable:BaseUrl must be configured.");
        }

        httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            var credentialBytes = Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
        }
    }

    private sealed class FlowableListResponse<T>
    {
        public List<T> Data { get; init; } = [];

        // Flowable echoes the unpaged total on every list response.
        public int Total { get; init; }
    }

    private sealed class FlowableDeploymentResponse
    {
        public string? Id { get; init; }
    }

    private sealed class FlowableProcessDefinitionResponse
    {
        public string? Id { get; init; }

        public string? Key { get; init; }

        public string? Name { get; init; }

        public int Version { get; init; }

        public string? DeploymentId { get; init; }

        public bool GraphicalNotationDefined { get; init; }

        public bool Suspended { get; init; }
    }

    private sealed class FlowableProcessInstanceResponse
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public string? ActivityId { get; init; }

        public bool Suspended { get; init; }

        public string? StartUserId { get; init; }
    }

    // Trimmed-down execution row for the runtime executions listing — only
    // the id is consumed by ListExecutionsBySignalSubscriptionAsync. Flowable
    // returns the same shape for any /runtime/executions query, so additional
    // fields can be added here without touching the request path.
    private sealed class FlowableExecutionResponse
    {
        public string? Id { get; init; }
    }

    private sealed class FlowableHistoricProcessInstanceResponse
    {
        public string? Id { get; init; }

        // Per-instance display name set when the run was started. Often null.
        public string? Name { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public DateTimeOffset? StartTime { get; init; }

        public DateTimeOffset? EndTime { get; init; }

        // Set by Flowable when the instance was ended via runtime DELETE with
        // a reason — used to distinguish a cancelled run from a normal finish.
        public string? DeleteReason { get; init; }

        // Needed by the list-endpoint visibility filter so a `[startedby=user]`
        // selector can be evaluated without re-fetching the instance.
        public string? StartUserId { get; init; }
    }

    private sealed class FlowableHistoricActivityInstanceResponse
    {
        public string? ActivityId { get; init; }

        public string? ActivityName { get; init; }

        // BPMN element type — userTask, serviceTask, startEvent, endEvent,
        // exclusiveGateway, etc. Used by the Execution Log tab.
        public string? ActivityType { get; init; }

        public string? ProcessInstanceId { get; init; }

        public DateTimeOffset? StartTime { get; init; }

        public DateTimeOffset? EndTime { get; init; }

        public long? DurationInMillis { get; init; }

        // Set on userTask rows.
        public string? Assignee { get; init; }

        public string? TaskId { get; init; }

        // Flowable propagates the process-level delete reason to every
        // activity instance that was in flight at cancellation time. This is
        // the authoritative signal for "this node was halted, not finished."
        public string? DeleteReason { get; init; }
    }

    private sealed class FlowableHistoricVariableInstanceResponse
    {
        public FlowableHistoricVariableResponse? Variable { get; init; }

        public DateTimeOffset? CreateTime { get; init; }

        public DateTimeOffset? LastUpdatedTime { get; init; }
    }

    private sealed class FlowableHistoricVariableResponse
    {
        public string? Name { get; init; }

        public string? Type { get; init; }

        public JsonElement? Value { get; init; }
    }

    // Shape returned by GET /runtime/process-instances/{id}/variables — a
    // flat array of these (no `data` wrapper). Mirrors the historic variant
    // above but without the wrapper indirection.
    private sealed class FlowableRuntimeVariableResponse
    {
        public string? Name { get; init; }

        public string? Type { get; init; }

        public JsonElement? Value { get; init; }
    }

    private sealed class FlowableTaskResponse
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? TaskDefinitionKey { get; init; }

        public string? Assignee { get; init; }

        public string? ProcessInstanceId { get; init; }

        // Some Flowable versions surface the per-instance name on the runtime
        // task response; backfilled from history if missing.
        public string? ProcessInstanceName { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public DateTimeOffset? CreateTime { get; init; }

        public DateTimeOffset? DueDate { get; init; }
    }

    private sealed class FlowableHistoricTaskResponse
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Assignee { get; init; }

        public string? Owner { get; init; }

        public string? TaskDefinitionKey { get; init; }

        public DateTimeOffset? StartTime { get; init; }

        public DateTimeOffset? EndTime { get; init; }

        public DateTimeOffset? ClaimTime { get; init; }

        public DateTimeOffset? DueDate { get; init; }

        public string? FormKey { get; init; }

        public int? Priority { get; init; }

        public string? DeleteReason { get; init; }
    }

    private sealed class FlowableHistoricDetailResponse
    {
        // "variableUpdate" — Flowable nests it as `detailType` on the wire,
        // not `type` (which is the variable's data type, inside `variable`).
        public string? DetailType { get; init; }

        public string? TaskId { get; init; }

        public string? ActivityInstanceId { get; init; }

        public DateTimeOffset? Time { get; init; }

        // Variable name / type / value are nested under `variable` — same
        // shape as historic-variable-instances. Reuse that DTO.
        public FlowableHistoricVariableResponse? Variable { get; init; }

        public int? Revision { get; init; }
    }

    private sealed class FlowableScriptTaskSupportResponse
    {
        public bool JavaScriptSupported { get; init; }

        public List<string> EngineNames { get; init; } = [];
    }
}
