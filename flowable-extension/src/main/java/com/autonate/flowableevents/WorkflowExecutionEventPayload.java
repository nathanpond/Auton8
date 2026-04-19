package com.autonate.flowableevents;

import java.time.Instant;

record WorkflowExecutionEventPayload(
    String eventId,
    String eventType,
    Instant occurredAtUtc,
    String processInstanceId,
    String processDefinitionId,
    String processDefinitionKey,
    String processDefinitionName,
    String activityId,
    String activityName,
    String taskId,
    String taskName,
    String assignee,
    String tenantId,
    String rawFlowableEventType,
    String sourceAppId
) {
}
