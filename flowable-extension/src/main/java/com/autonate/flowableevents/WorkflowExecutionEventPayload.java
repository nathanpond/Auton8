package com.autonate.flowableevents;

import java.time.Instant;

// Payload shape published on the workflow telemetry stream. Mirrored on the
// C# side by WorkflowExecutionErrorRecorder + the audit/system-issue
// detectors. Any field added here MUST also be read on the consumer side
// (or it'll be silently ignored — the recorder uses optional ReadString).
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
    String sourceAppId,
    // Populated only on job.execution.failed events. Root-cause message
    // walked from event.getCause(). Capped at 4 KB by the mapper.
    String errorMessage,
    // Populated only on job.execution.failed events. Full chained
    // stack trace from event.getCause(). Capped at 64 KB by the mapper.
    String errorStackTrace
) {
}
