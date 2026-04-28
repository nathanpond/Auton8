package com.autonate.flowableevents;

import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Set;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEventType;
import org.flowable.common.engine.impl.cfg.TransactionState;
import org.flowable.engine.RepositoryService;
import org.flowable.engine.RuntimeService;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.delegate.event.AbstractFlowableEngineEventListener;
import org.flowable.engine.delegate.event.FlowableActivityEvent;
import org.flowable.engine.delegate.event.FlowableCancelledEvent;
import org.flowable.engine.delegate.event.FlowableProcessStartedEvent;
import org.flowable.engine.runtime.ProcessInstance;
import org.springframework.beans.factory.ObjectProvider;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

final class WorkflowExecutionEventListener extends AbstractFlowableEngineEventListener {

    private static final Logger Logger = LoggerFactory.getLogger(WorkflowExecutionEventListener.class);
    private static final DateTimeFormatter AutoStartedNameTimestampFormatter =
        DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm z");

    private final WorkflowExecutionEventMapper eventMapper;
    private final DaprWorkflowEventPublisher publisher;
    private final ObjectProvider<RepositoryService> repositoryServiceProvider;
    private final ObjectProvider<RuntimeService> runtimeServiceProvider;
    private final WorkflowDefinitionMetadataResolver definitionMetadataResolver;

    WorkflowExecutionEventListener(
        WorkflowExecutionEventMapper eventMapper,
        DaprWorkflowEventPublisher publisher,
        ObjectProvider<RepositoryService> repositoryServiceProvider,
        ObjectProvider<RuntimeService> runtimeServiceProvider,
        WorkflowDefinitionMetadataResolver definitionMetadataResolver
    ) {
        super(Set.of(
            FlowableEngineEventType.PROCESS_STARTED,
            FlowableEngineEventType.ACTIVITY_STARTED,
            FlowableEngineEventType.ACTIVITY_COMPLETED,
            FlowableEngineEventType.TASK_CREATED,
            FlowableEngineEventType.TASK_ASSIGNED,
            FlowableEngineEventType.TASK_COMPLETED,
            FlowableEngineEventType.PROCESS_COMPLETED,
            FlowableEngineEventType.PROCESS_CANCELLED,
            FlowableEngineEventType.PROCESS_COMPLETED_WITH_ERROR_END_EVENT,
            FlowableEngineEventType.JOB_EXECUTION_FAILURE
        ));
        this.eventMapper = eventMapper;
        this.publisher = publisher;
        this.repositoryServiceProvider = repositoryServiceProvider;
        this.runtimeServiceProvider = runtimeServiceProvider;
        this.definitionMetadataResolver = definitionMetadataResolver;
        setOnTransaction(TransactionState.COMMITTED.name());
    }

    @Override
    public boolean isFailOnException() {
        return false;
    }

    @Override
    protected void processStarted(FlowableProcessStartedEvent event) {
        try {
            if (event.getEntity() instanceof ProcessInstance processInstance) {
                applyAutoStartedName(processInstance);
                publisher.publish(eventMapper.mapProcessStarted("process.started", event, processInstance, repositoryServiceProvider.getIfAvailable()));
            } else {
                Logger.warn("Process started event entity was not a ProcessInstance: {}", event.getEntity());
            }
        } catch (RuntimeException exception) {
            Logger.warn("Failed to publish Flowable workflow event '{}'.", "process.started", exception);
        }
    }

    // Engine-triggered starts (timer / signal / message start events) leave the
    // process instance name blank, which renders as the raw UUID in the
    // executions list. Generate a "<Definition> — <start time UTC>" name so
    // operators can scan the list at a glance. REST-driven starts already
    // pass an explicit name and skip this branch.
    private void applyAutoStartedName(ProcessInstance processInstance) {
        var existingName = processInstance.getName();
        if (existingName != null && !existingName.isBlank()) {
            return;
        }

        var runtimeService = runtimeServiceProvider.getIfAvailable();
        if (runtimeService == null) {
            return;
        }

        var startInstant = processInstance.getStartTime() != null
            ? processInstance.getStartTime().toInstant()
            : Instant.now();
        var generatedName = formatAutoStartedName(
            resolveDefinitionName(processInstance),
            startInstant,
            ZoneId.systemDefault());

        // The PROCESS_STARTED entity is the root execution — its `getId()` is
        // the execution id, which Flowable rejects here. Use the explicit
        // process-instance id instead.
        var processInstanceId = processInstance.getProcessInstanceId();
        try {
            runtimeService.setProcessInstanceName(processInstanceId, generatedName);
        } catch (RuntimeException exception) {
            Logger.warn(
                "Failed to assign default name to auto-started process instance '{}'.",
                processInstanceId,
                exception);
        }
    }

    private String resolveDefinitionName(ProcessInstance processInstance) {
        var directName = processInstance.getProcessDefinitionName();
        if (directName != null && !directName.isBlank()) {
            return directName;
        }
        var resolved = definitionMetadataResolver.resolve(
            repositoryServiceProvider.getIfAvailable(),
            processInstance.getProcessDefinitionId());
        return resolved.processDefinitionName();
    }

    static String formatAutoStartedName(String processDefinitionName, Instant startTime, ZoneId zoneId) {
        var label = processDefinitionName != null && !processDefinitionName.isBlank()
            ? processDefinitionName
            : "Workflow";
        var formatted = AutoStartedNameTimestampFormatter.format(startTime.atZone(zoneId));
        return label + " - " + formatted;
    }

    @Override
    protected void activityStarted(FlowableActivityEvent event) {
        publish("activity.started", event, getExecution(event));
    }

    @Override
    protected void activityCompleted(FlowableActivityEvent event) {
        publish("activity.completed", event, getExecution(event));
    }

    @Override
    protected void taskCreated(FlowableEngineEntityEvent event) {
        publish("task.created", event, getExecution(event));
    }

    @Override
    protected void taskAssigned(FlowableEngineEntityEvent event) {
        publish("task.assigned", event, getExecution(event));
    }

    @Override
    protected void taskCompleted(FlowableEngineEntityEvent event) {
        publish("task.completed", event, getExecution(event));
    }

    @Override
    protected void processCompleted(FlowableEngineEntityEvent event) {
        publish("process.completed", event, getExecution(event));
    }

    @Override
    protected void processCancelled(FlowableCancelledEvent event) {
        publish("process.cancelled", event, getExecution(event));
    }

    @Override
    protected void processCompletedWithErrorEnd(FlowableEngineEntityEvent event) {
        publish("process.completed.error", event, getExecution(event));
    }

    @Override
    protected void jobExecutionFailure(FlowableEngineEntityEvent event) {
        publish("job.execution.failed", event, getExecution(event));
    }

    private void publish(String eventType, FlowableEngineEvent event, DelegateExecution execution) {
        try {
            var envelope = eventMapper.map(eventType, event, execution, repositoryServiceProvider.getIfAvailable());
            publisher.publish(envelope);
        } catch (RuntimeException exception) {
            Logger.warn("Failed to publish Flowable workflow event '{}'.", eventType, exception);
        }
    }
}
