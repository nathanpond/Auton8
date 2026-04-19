package com.autonate.flowableevents;

record WorkflowExecutionEventEnvelope(String topic, WorkflowExecutionEventPayload payload) {
}
