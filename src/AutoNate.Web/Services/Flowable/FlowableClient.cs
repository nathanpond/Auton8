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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly FlowableOptions _options = options.Value;

    public async Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowDraft draft, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileName = $"{draft.ProcessKey}.bpmn20.xml";
        content.Add(new StringContent(draft.BpmnXml, Encoding.UTF8, "application/xml"), "file", fileName);

        using var response = await _httpClient.PostAsync("service/repository/deployments", content, cancellationToken);
        await EnsureSuccessAsync(response, "deploy the BPMN workflow");

        var processDefinition = await GetLatestProcessDefinitionAsync(draft.ProcessKey, cancellationToken)
            ?? throw new InvalidOperationException($"Flowable accepted the deployment, but no process definition with key '{draft.ProcessKey}' was found.");

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
    }

    private sealed class FlowableProcessInstanceResponse
    {
        public string? Id { get; init; }

        public string? ProcessDefinitionId { get; init; }

        public string? ActivityId { get; init; }

        public bool Suspended { get; init; }
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
