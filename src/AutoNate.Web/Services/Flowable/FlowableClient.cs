using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable;

public sealed class FlowableClient(HttpClient httpClient, IOptions<FlowableOptions> options) : IFlowableClient
{
    private const int WorkflowExecutionQuerySize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly FlowableOptions _options = options.Value;

    public async Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowModel model, CancellationToken cancellationToken = default)
    {
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

        return definition is null
            ? null
            : new FlowableProcessDefinitionSummary
            {
                Id = definition.Id ?? string.Empty,
                Key = definition.Key ?? string.Empty,
                Name = definition.Name ?? string.Empty,
                Version = definition.Version,
                DeploymentId = definition.DeploymentId ?? string.Empty
            };
    }

    public async Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(string processDefinitionKey, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "service/runtime/process-instances",
            new
            {
                processDefinitionKey,
                variables = ToFlowableVariables(variables)
            },
            cancellationToken);

        await EnsureSuccessAsync(response, "start the process instance");

        var payload = await DeserializeAsync<FlowableProcessInstanceResponse>(response, cancellationToken);
        return new FlowableProcessInstanceSummary
        {
            Id = payload.Id ?? string.Empty,
            ProcessDefinitionId = payload.ProcessDefinitionId ?? string.Empty,
            ActivityId = payload.ActivityId,
            Suspended = payload.Suspended
        };
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
            ProcessDefinitionId = payload.ProcessDefinitionId ?? string.Empty,
            ActivityId = payload.ActivityId,
            Suspended = payload.Suspended
        };
    }

    public async Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(CancellationToken cancellationToken = default)
    {
        using var historicResponse = await _httpClient.GetAsync(
            $"service/history/historic-process-instances?sort=startTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(historicResponse, "query historic process instances");

        using var runtimeResponse = await _httpClient.GetAsync(
            $"service/runtime/process-instances?sort=startTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(runtimeResponse, "query runtime process instances");

        using var tasksResponse = await _httpClient.GetAsync(
            $"service/runtime/tasks?sort=createTime&order=desc&size={WorkflowExecutionQuerySize}",
            cancellationToken);
        await EnsureSuccessAsync(tasksResponse, "query runtime tasks");

        var historicPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricProcessInstanceResponse>>(historicResponse, cancellationToken);
        var runtimePayload = await DeserializeAsync<FlowableListResponse<FlowableProcessInstanceResponse>>(runtimeResponse, cancellationToken);
        var tasksPayload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(tasksResponse, cancellationToken);
        var processDefinitionNames = await GetProcessDefinitionNamesByIdAsync(historicPayload.Data, cancellationToken);

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

                return new WorkflowExecutionSummary
                {
                    Id = instance.Id!,
                    WorkflowModelName = workflowModelName,
                    StartedAtUtc = instance.StartTime,
                    Status = isRunning ? "Running" : "Complete",
                    CurrentStep = currentStep
                };
            })
            .OrderByDescending(execution => execution.StartedAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(execution => execution.Id, StringComparer.Ordinal)
            .ToArray();
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
            $"service/history/historic-activity-instances?processInstanceId={Uri.EscapeDataString(processInstanceId)}",
            cancellationToken);
        await EnsureSuccessAsync(activitiesResponse, "query historic activity instances");

        var bpmnXml = await modelResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureDiagramXmlPresent(bpmnXml, processInstanceId, processDefinition);
        var activitiesPayload = await DeserializeAsync<FlowableListResponse<FlowableHistoricActivityInstanceResponse>>(activitiesResponse, cancellationToken);

        var completedActivityIds = activitiesPayload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId) && activity.EndTime is not null)
            .Select(activity => activity.ActivityId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var currentActivityIds = activitiesPayload.Data
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityId) && activity.EndTime is null)
            .Select(activity => activity.ActivityId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (currentActivityIds.Length == 0)
        {
            var runtimeInstance = await GetProcessInstanceAsync(processInstanceId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(runtimeInstance?.ActivityId))
            {
                currentActivityIds = [runtimeInstance.ActivityId];
            }
        }

        return new WorkflowExecutionDiagramDetail
        {
            ExecutionId = processInstanceId,
            BpmnXml = bpmnXml,
            CompletedActivityIds = completedActivityIds,
            CurrentActivityIds = currentActivityIds
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

    public async Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default)
    {
        var url = $"service/runtime/tasks?processInstanceId={Uri.EscapeDataString(processInstanceId)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "query runtime tasks");

        var payload = await DeserializeAsync<FlowableListResponse<FlowableTaskResponse>>(response, cancellationToken);
        return payload.Data
            .Select(task => new FlowableTaskSummary
            {
                Id = task.Id ?? string.Empty,
                Name = task.Name ?? string.Empty,
                Assignee = task.Assignee,
                ProcessInstanceId = task.ProcessInstanceId,
                CreatedAtUtc = task.CreateTime
            })
            .ToArray();
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

    private async Task<string> ReadDeploymentIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await DeserializeAsync<FlowableDeploymentResponse>(response, cancellationToken);
        return payload.Id ?? string.Empty;
    }

    private static object[] ToFlowableVariables(IReadOnlyDictionary<string, object?>? variables)
    {
        return variables?.Select(variable => new { name = variable.Key, value = variable.Value }).ToArray()
            ?? [];
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
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
    }

    private sealed class FlowableProcessInstanceResponse
    {
        public string? Id { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public string? ActivityId { get; init; }

        public bool Suspended { get; init; }
    }

    private sealed class FlowableHistoricProcessInstanceResponse
    {
        public string? Id { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public DateTimeOffset? StartTime { get; init; }

        public DateTimeOffset? EndTime { get; init; }
    }

    private sealed class FlowableHistoricActivityInstanceResponse
    {
        public string? ActivityId { get; init; }

        public DateTimeOffset? EndTime { get; init; }
    }

    private sealed class FlowableTaskResponse
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Assignee { get; init; }

        public string? ProcessInstanceId { get; init; }

        public DateTimeOffset? CreateTime { get; init; }
    }
}
