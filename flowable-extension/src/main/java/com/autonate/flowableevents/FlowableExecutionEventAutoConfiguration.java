package com.autonate.flowableevents;

import java.util.ArrayList;
import org.flowable.common.engine.api.delegate.event.FlowableEventListener;
import org.flowable.engine.HistoryService;
import org.flowable.spring.SpringProcessEngineConfiguration;
import org.flowable.spring.boot.EngineConfigurationConfigurer;
import org.springframework.boot.autoconfigure.AutoConfiguration;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.context.annotation.Bean;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

@AutoConfiguration
@EnableConfigurationProperties(FlowableExecutionEventProperties.class)
public class FlowableExecutionEventAutoConfiguration {

    private static final Logger logger = LoggerFactory.getLogger(FlowableExecutionEventAutoConfiguration.class);

    @Bean
    WorkflowDefinitionMetadataResolver workflowDefinitionMetadataResolver() {
        return new WorkflowDefinitionMetadataResolver();
    }

    @Bean
    DaprWorkflowEventPublisher daprWorkflowEventPublisher(FlowableExecutionEventProperties properties) {
        return new DaprWorkflowEventPublisher(properties);
    }

    @Bean
    WorkflowExecutionEventMapper workflowExecutionEventMapper(
        FlowableExecutionEventProperties properties,
        WorkflowDefinitionMetadataResolver definitionMetadataResolver
    ) {
        return new WorkflowExecutionEventMapper(properties, definitionMetadataResolver);
    }

    @Bean
    FlowableScriptTaskSupportService flowableScriptTaskSupportService() {
        return new FlowableScriptTaskSupportService();
    }

    @Bean(name = "dueDateHelper")
    DueDateHelper dueDateHelper(HistoryService historyService) {
        return new DueDateHelper(historyService);
    }

    @Bean
    FlowableScriptTaskSupportController flowableScriptTaskSupportController(
        FlowableScriptTaskSupportService scriptTaskSupportService
    ) {
        return new FlowableScriptTaskSupportController(scriptTaskSupportService);
    }

    @Bean
    FlowableScriptTaskSupportEndpoint scriptTaskSupportEndpoint(
        FlowableScriptTaskSupportService scriptTaskSupportService
    ) {
        return new FlowableScriptTaskSupportEndpoint(scriptTaskSupportService);
    }

    @Bean
    WorkflowExecutionEventListener workflowExecutionEventListener(
        WorkflowExecutionEventMapper eventMapper,
        DaprWorkflowEventPublisher publisher,
        ObjectProvider<org.flowable.engine.RepositoryService> repositoryServiceProvider,
        ObjectProvider<org.flowable.engine.RuntimeService> runtimeServiceProvider,
        WorkflowDefinitionMetadataResolver definitionMetadataResolver
    ) {
        return new WorkflowExecutionEventListener(
            eventMapper,
            publisher,
            repositoryServiceProvider,
            runtimeServiceProvider,
            definitionMetadataResolver);
    }

    @Bean
    WorkflowFailureEventListener workflowFailureEventListener(
        WorkflowExecutionEventMapper eventMapper,
        DaprWorkflowEventPublisher publisher,
        ObjectProvider<org.flowable.engine.RepositoryService> repositoryServiceProvider
    ) {
        return new WorkflowFailureEventListener(eventMapper, publisher, repositoryServiceProvider);
    }

    @Bean
    EngineConfigurationConfigurer<SpringProcessEngineConfiguration> workflowExecutionListenerConfigurer(
        WorkflowExecutionEventListener listener,
        WorkflowFailureEventListener failureListener
    ) {
        return engineConfiguration -> {
            var eventListeners = new ArrayList<FlowableEventListener>();
            eventListeners.add(listener);
            eventListeners.add(failureListener);
            engineConfiguration.setEventListeners(eventListeners);
            logger.info("Registered AutoNate workflow execution event listeners with the Flowable process engine.");
        };
    }
}
