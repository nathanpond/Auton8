package com.autonate.flowableevents;

import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import org.flowable.engine.RepositoryService;
import org.flowable.engine.repository.ProcessDefinition;

final class WorkflowDefinitionMetadataResolver {

    private final ConcurrentHashMap<String, WorkflowDefinitionMetadata> cache = new ConcurrentHashMap<>();

    WorkflowDefinitionMetadata resolve(RepositoryService repositoryService, String processDefinitionId) {
        if (repositoryService == null || processDefinitionId == null || processDefinitionId.isBlank()) {
            return WorkflowDefinitionMetadata.empty(processDefinitionId);
        }

        return cache.computeIfAbsent(processDefinitionId, key -> {
            ProcessDefinition definition = repositoryService.createProcessDefinitionQuery()
                .processDefinitionId(key)
                .singleResult();

            if (definition == null) {
                return WorkflowDefinitionMetadata.empty(key);
            }

            return new WorkflowDefinitionMetadata(
                definition.getId(),
                definition.getKey(),
                definition.getName()
            );
        });
    }

    void put(WorkflowDefinitionMetadata metadata) {
        if (metadata == null || metadata.processDefinitionId() == null || metadata.processDefinitionId().isBlank()) {
            return;
        }

        cache.put(metadata.processDefinitionId(), metadata);
    }
}
