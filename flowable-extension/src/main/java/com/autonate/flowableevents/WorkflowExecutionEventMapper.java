package com.autonate.flowableevents;

import java.time.Instant;
import java.util.UUID;
import org.flowable.common.engine.api.delegate.event.FlowableEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEvent;
import org.flowable.engine.RepositoryService;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.delegate.event.FlowableActivityEvent;
import org.flowable.engine.runtime.ProcessInstance;
import org.flowable.task.api.TaskInfo;

class WorkflowExecutionEventMapper {

    // Caps applied at the boundary so a runaway throwable can't blow up the
    // event payload or the workflow_execution_errors row. Truncated values get
    // a trailing marker so it's obvious in the UI.
    private static final int ERROR_MESSAGE_CAP = 4 * 1024;
    private static final int ERROR_STACK_TRACE_CAP = 64 * 1024;
    private static final String TRUNCATION_MARKER = "\n… [truncated]";

    private final FlowableExecutionEventProperties properties;
    private final WorkflowDefinitionMetadataResolver definitionMetadataResolver;

    WorkflowExecutionEventMapper(
        FlowableExecutionEventProperties properties,
        WorkflowDefinitionMetadataResolver definitionMetadataResolver
    ) {
        this.properties = properties;
        this.definitionMetadataResolver = definitionMetadataResolver;
    }

    WorkflowExecutionEventEnvelope map(
        String eventType,
        FlowableEngineEvent flowableEvent,
        DelegateExecution execution,
        RepositoryService repositoryService
    ) {
        return map(eventType, flowableEvent, execution, repositoryService, /*cause*/ null);
    }

    WorkflowExecutionEventEnvelope map(
        String eventType,
        FlowableEngineEvent flowableEvent,
        DelegateExecution execution,
        RepositoryService repositoryService,
        Throwable cause
    ) {
        var processDefinitionId = firstNonBlank(flowableEvent.getProcessDefinitionId(), execution != null ? execution.getProcessDefinitionId() : null);
        var definition = definitionMetadataResolver.resolve(repositoryService, processDefinitionId);

        var activity = activityDetails(flowableEvent, execution);
        var task = taskDetails(flowableEvent);
        var processInstanceId = firstNonBlank(flowableEvent.getProcessInstanceId(), execution != null ? execution.getProcessInstanceId() : null);
        var tenantId = firstNonBlank(task.tenantId(), execution != null ? execution.getTenantId() : null);

        var payload = new WorkflowExecutionEventPayload(
            UUID.randomUUID().toString(),
            eventType,
            Instant.now(),
            processInstanceId,
            definition.processDefinitionId(),
            definition.processDefinitionKey(),
            definition.processDefinitionName(),
            activity.activityId(),
            activity.activityName(),
            task.taskId(),
            task.taskName(),
            task.assignee(),
            tenantId,
            flowableEvent.getType().name(),
            properties.getSourceAppId(),
            truncate(ExceptionDetails.rootCauseMessage(cause), ERROR_MESSAGE_CAP),
            truncate(ExceptionDetails.fullStackTrace(cause), ERROR_STACK_TRACE_CAP)
        );

        return new WorkflowExecutionEventEnvelope(topicFor(payload), payload);
    }

    WorkflowExecutionEventEnvelope mapProcessStarted(
        String eventType,
        FlowableEvent flowableEvent,
        ProcessInstance processInstance,
        RepositoryService repositoryService
    ) {
        var definition = definitionMetadataResolver.resolve(repositoryService, processInstance.getProcessDefinitionId());
        var processDefinitionKey = firstNonBlank(processInstance.getProcessDefinitionKey(), definition.processDefinitionKey());
        var processDefinitionName = firstNonBlank(processInstance.getProcessDefinitionName(), definition.processDefinitionName());

        var payload = new WorkflowExecutionEventPayload(
            UUID.randomUUID().toString(),
            eventType,
            Instant.now(),
            processInstance.getProcessInstanceId(),
            processInstance.getProcessDefinitionId(),
            processDefinitionKey,
            processDefinitionName,
            processInstance.getActivityId(),
            null,
            null,
            null,
            null,
            processInstance.getTenantId(),
            flowableEvent.getType().name(),
            properties.getSourceAppId(),
            null,   // errorMessage (process-started never has a cause)
            null    // errorStackTrace
        );

        return new WorkflowExecutionEventEnvelope(topicFor(payload), payload);
    }

    private String topicFor(WorkflowExecutionEventPayload payload) {
        return properties.getTopicRoot();
    }

    private static ActivityDetails activityDetails(FlowableEngineEvent flowableEvent, DelegateExecution execution) {
        if (flowableEvent instanceof FlowableActivityEvent activityEvent) {
            return new ActivityDetails(activityEvent.getActivityId(), activityEvent.getActivityName());
        }

        return new ActivityDetails(
            execution != null ? execution.getCurrentActivityId() : null,
            execution != null ? execution.getCurrentActivityName() : null
        );
    }

    private static TaskDetails taskDetails(FlowableEngineEvent flowableEvent) {
        if (flowableEvent instanceof FlowableEngineEntityEvent entityEvent && entityEvent.getEntity() instanceof TaskInfo task) {
            return new TaskDetails(task.getId(), task.getName(), task.getAssignee(), task.getTenantId());
        }

        return TaskDetails.empty();
    }

    static String sanitizeTopicSegment(String value, String fallback) {
        if (value == null || value.isBlank()) {
            return fallback;
        }

        return value.replaceAll("[^A-Za-z0-9_-]", "_");
    }

    private static String firstNonBlank(String primary, String secondary) {
        return primary != null && !primary.isBlank() ? primary : secondary;
    }

    private static String truncate(String value, int max) {
        if (value == null || value.length() <= max) {
            return value;
        }
        return value.substring(0, max - TRUNCATION_MARKER.length()) + TRUNCATION_MARKER;
    }

    private record ActivityDetails(String activityId, String activityName) {
    }

    private record TaskDetails(String taskId, String taskName, String assignee, String tenantId) {

        static TaskDetails empty() {
            return new TaskDetails(null, null, null, null);
        }
    }
}
