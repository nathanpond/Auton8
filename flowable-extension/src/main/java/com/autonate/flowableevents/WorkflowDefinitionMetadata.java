package com.autonate.flowableevents;

record WorkflowDefinitionMetadata(
    String processDefinitionId,
    String processDefinitionKey,
    String processDefinitionName
) {

    static WorkflowDefinitionMetadata empty(String processDefinitionId) {
        return new WorkflowDefinitionMetadata(processDefinitionId, null, null);
    }
}
