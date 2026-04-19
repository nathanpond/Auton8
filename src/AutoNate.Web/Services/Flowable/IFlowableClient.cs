using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Flowable;

public interface IFlowableClient
{
    Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowModel model, CancellationToken cancellationToken = default);

    Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(string processDefinitionKey, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(CancellationToken cancellationToken = default);

    Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task DeleteWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(string taskId, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);
}
