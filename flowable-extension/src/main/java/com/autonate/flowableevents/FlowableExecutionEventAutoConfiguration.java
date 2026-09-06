package com.autonate.flowableevents;

import java.util.ArrayList;
import org.flowable.common.engine.api.delegate.event.FlowableEventListener;
import org.flowable.engine.HistoryService;
import org.flowable.spring.SpringProcessEngineConfiguration;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.net.http.HttpClient;
import java.time.Duration;
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
    FlowableScriptTaskSupportService flowableScriptTaskSupportService(
        FlowableExecutionEventProperties properties
    ) {
        return new FlowableScriptTaskSupportService(properties);
    }

    @Bean(name = "dueDateHelper")
    DueDateHelper dueDateHelper(HistoryService historyService) {
        return new DueDateHelper(historyService);
    }

    // Bean name MUST match the BPMN delegateExpression
    // ${autonateBehaviorDelegate} that WorkflowBpmnXml writes onto every
    // service task. Renaming it would orphan every published workflow.
    @Bean(name = "autonateBehaviorDelegate")
    AutoNateBehaviorDelegate autonateBehaviorDelegate(FlowableExecutionEventProperties properties) {
        return new AutoNateBehaviorDelegate(properties);
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

    // The seam that decides where BPMN script-task code runs (#147).
    //
    // Installing the factory on the engine configuration is what makes the
    // sandbox mandatory rather than opt-in: the parser asks this factory for
    // every script task's behaviour, so there is no route by which a script
    // reaches the JVM's own script engine.
    @Bean
    AutoNateActivityBehaviorFactory autoNateActivityBehaviorFactory(
        FlowableExecutionEventProperties properties
    ) {
        return new AutoNateActivityBehaviorFactory(
            HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(Math.max(1, properties.getBehaviorTimeoutSeconds())))
                .build(),
            new ObjectMapper().registerModule(new JavaTimeModule()),
            properties);
    }

    @Bean
    EngineConfigurationConfigurer<SpringProcessEngineConfiguration> autoNateScriptTaskConfigurer(
        AutoNateActivityBehaviorFactory activityBehaviorFactory
    ) {
        return engineConfiguration -> {
            engineConfiguration.setActivityBehaviorFactory(activityBehaviorFactory);
            logger.info(
                "BPMN script tasks routed to the AutoNate executor sandbox; the JVM script engine is not used.");
        };
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
