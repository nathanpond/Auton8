package com.autonate.flowableevents;

import java.util.Set;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEventType;
import org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent;
import org.flowable.engine.RepositoryService;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.delegate.event.AbstractFlowableEngineEventListener;
import org.springframework.beans.factory.ObjectProvider;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

// JOB_EXECUTION_FAILURE is dispatched inside Flowable's failing work transaction,
// which then rolls back. The main listener filters on TransactionState.COMMITTED
// so it would never see a failure. This second listener intentionally omits the
// transaction filter so failure events fire immediately.
final class WorkflowFailureEventListener extends AbstractFlowableEngineEventListener {

    private static final Logger Logger = LoggerFactory.getLogger(WorkflowFailureEventListener.class);

    private final WorkflowExecutionEventMapper eventMapper;
    private final DaprWorkflowEventPublisher publisher;
    private final ObjectProvider<RepositoryService> repositoryServiceProvider;

    WorkflowFailureEventListener(
        WorkflowExecutionEventMapper eventMapper,
        DaprWorkflowEventPublisher publisher,
        ObjectProvider<RepositoryService> repositoryServiceProvider
    ) {
        super(Set.of(FlowableEngineEventType.JOB_EXECUTION_FAILURE));
        this.eventMapper = eventMapper;
        this.publisher = publisher;
        this.repositoryServiceProvider = repositoryServiceProvider;
    }

    @Override
    public boolean isFailOnException() {
        return false;
    }

    @Override
    protected void jobExecutionFailure(FlowableEngineEntityEvent event) {
        publish("job.execution.failed", event, getExecution(event), causeFrom(event));
    }

    private void publish(String eventType, FlowableEngineEvent event, DelegateExecution execution, Throwable cause) {
        try {
            var envelope = eventMapper.map(eventType, event, execution, repositoryServiceProvider != null
                ? repositoryServiceProvider.getIfAvailable()
                : null, cause);
            if (envelope != null && publisher != null) {
                publisher.publish(envelope);
            }
        } catch (RuntimeException exception) {
            Logger.warn("Failed to publish Flowable workflow event '{}'.", eventType, exception);
        }
    }

    private static Throwable causeFrom(FlowableEngineEvent event) {
        if (event instanceof FlowableExceptionEvent exceptionEvent) {
            return exceptionEvent.getCause();
        }
        return null;
    }
}
