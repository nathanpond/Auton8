package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class WorkflowExecutionEventMapperTests {

    @Test
    void sanitizeTopicSegmentReplacesUnsupportedCharacters() {
        assertEquals("process_123", WorkflowExecutionEventMapper.sanitizeTopicSegment("process:123", "fallback"));
    }

    @Test
    void sanitizeTopicSegmentFallsBackWhenMissing() {
        assertEquals("fallback", WorkflowExecutionEventMapper.sanitizeTopicSegment(" ", "fallback"));
    }

    @Test
    void eventPayloadAllowsNullOptionalFields() {
        var payload = new WorkflowExecutionEventPayload(
            "event-1",
            "task.completed",
            java.time.Instant.parse("2026-01-01T00:00:00Z"),
            "process-1",
            "definition-1",
            "demo",
            "Demo Process",
            null,
            null,
            null,
            null,
            null,
            null,
            "TASK_COMPLETED",
            "flowable"
        );

        assertEquals("event-1", payload.eventId());
        assertEquals("task.completed", payload.eventType());
        assertNull(payload.activityId());
        assertNull(payload.taskId());
        assertEquals("flowable", payload.sourceAppId());
    }

    @Test
    void envelopeRetainsTopicAndPayload() {
        var payload = new WorkflowExecutionEventPayload(
            "event-2",
            "process.started",
            java.time.Instant.parse("2026-01-01T00:00:00Z"),
            "process-2",
            "definition-2",
            "demo",
            "Demo Process",
            null,
            null,
            null,
            null,
            null,
            null,
            "PROCESS_STARTED",
            "flowable"
        );

        var envelope = new WorkflowExecutionEventEnvelope("workflow.execution.events", payload);

        assertEquals("workflow.execution.events", envelope.topic());
        assertNotNull(envelope.payload());
        assertEquals("process-2", envelope.payload().processInstanceId());
    }
}
