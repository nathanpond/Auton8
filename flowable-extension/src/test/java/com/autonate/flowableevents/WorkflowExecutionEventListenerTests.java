package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.time.Instant;
import java.time.ZoneId;
import org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent;
import org.flowable.common.engine.impl.cfg.TransactionState;
import org.junit.jupiter.api.Test;

class WorkflowExecutionEventListenerTests {

    // JOB_EXECUTION_FAILURE is dispatched in the failing job's work transaction,
    // which then rolls back. WorkflowFailureEventListener must NOT filter on
    // a transaction state, otherwise the event is silently dropped.
    @Test
    void failureListenerHasNoTransactionStateFilter() {
        var listener = new WorkflowFailureEventListener(null, null, null);

        assertNull(listener.getOnTransaction(),
            "WorkflowFailureEventListener must not call setOnTransaction; "
                + "JOB_EXECUTION_FAILURE fires in the rolling-back work transaction.");
    }

    // The main listener intentionally filters on COMMITTED so happy-path events
    // (process.started, activity.started, etc.) only fire for transactions that
    // actually persisted. Pinning this in a test so a future refactor doesn't
    // accidentally relax it and create double-publish on rollback.
    @Test
    void mainListenerFiltersOnCommittedTransaction() {
        var listener = new WorkflowExecutionEventListener(null, null, null, null, null);

        assertEquals(TransactionState.COMMITTED.name(), listener.getOnTransaction());
    }

    @Test
    void formatAutoStartedNameUsesGivenZone() {
        var name = WorkflowExecutionEventListener.formatAutoStartedName(
            "Timer Start Test",
            Instant.parse("2026-04-27T19:50:00Z"),
            ZoneId.of("America/New_York"));

        assertEquals("Timer Start Test - 2026-04-27 15:50 EDT", name);
    }

    @Test
    void formatAutoStartedNameRendersUtcZoneAsUtc() {
        var name = WorkflowExecutionEventListener.formatAutoStartedName(
            "Demo Process",
            Instant.parse("2026-04-27T15:30:00Z"),
            ZoneId.of("UTC"));

        assertEquals("Demo Process - 2026-04-27 15:30 UTC", name);
    }

    @Test
    void formatAutoStartedNameFallsBackToWorkflowWhenDefinitionNameMissing() {
        var name = WorkflowExecutionEventListener.formatAutoStartedName(
            null,
            Instant.parse("2026-01-02T03:04:00Z"),
            ZoneId.of("UTC"));

        assertEquals("Workflow - 2026-01-02 03:04 UTC", name);
    }

    @Test
    void formatAutoStartedNameFallsBackToWorkflowWhenDefinitionNameBlank() {
        var name = WorkflowExecutionEventListener.formatAutoStartedName(
            "   ",
            Instant.parse("2026-12-31T23:59:00Z"),
            ZoneId.of("UTC"));

        assertEquals("Workflow - 2026-12-31 23:59 UTC", name);
    }

    @Test
    void failureListenerExtractsCauseFromExceptionEvent() {
        // The failure listener must cast the event to FlowableExceptionEvent and
        // pass the cause Throwable into the mapper, so the payload carries the
        // root-cause message and stack trace into workflow_execution_errors.
        // This test pins the contract: event.getCause() result MUST end up as the
        // mapper's `cause` argument.
        var capturedCause = new java.util.concurrent.atomic.AtomicReference<Throwable>();
        var fakeMapper = new WorkflowExecutionEventMapper(
            new FlowableExecutionEventProperties(), new WorkflowDefinitionMetadataResolver()) {
            // Override the 5-arg overload to capture the cause without needing
            // a real Flowable event. We return null so the listener short-
            // circuits before calling publisher.publish (which is also null).
            @Override
            WorkflowExecutionEventEnvelope map(
                String eventType,
                org.flowable.common.engine.api.delegate.event.FlowableEngineEvent flowableEvent,
                org.flowable.engine.delegate.DelegateExecution execution,
                org.flowable.engine.RepositoryService repositoryService,
                Throwable cause) {
                capturedCause.set(cause);
                return null;
            }
        };
        // Compiles because Task 3 dropped `final` from
        // WorkflowExecutionEventMapper and the class is package-private (test
        // class is in the same package).

        var rootCause = new IllegalStateException("script line 7: x is undefined");
        var listener = new WorkflowFailureEventListener(fakeMapper, /*publisher*/ null, /*repoProvider*/ null);

        // Drive the protected hook directly so we don't need a live Flowable engine.
        var event = new TestExceptionEntityEvent(rootCause);
        listener.jobExecutionFailure(event);

        org.junit.jupiter.api.Assertions.assertSame(rootCause, capturedCause.get(),
            "WorkflowFailureEventListener must thread event.getCause() into the mapper");
    }

    // Minimal stand-in for FlowableEngineEntityEvent + FlowableExceptionEvent.
    // Production code calls only getCause(); the rest of the interface methods
    // throw — they would fail the test loudly if the listener started touching
    // them, which is what we want.
    private static final class TestExceptionEntityEvent
        implements org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent,
                   org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent {

        private final Throwable cause;

        TestExceptionEntityEvent(Throwable cause) {
            this.cause = cause;
        }

        @Override public Throwable getCause() { return cause; }
        @Override public Object getEntity() { return null; }
        @Override public org.flowable.common.engine.api.delegate.event.FlowableEngineEventType getType() {
            return org.flowable.common.engine.api.delegate.event.FlowableEngineEventType.JOB_EXECUTION_FAILURE;
        }
        @Override public String getProcessDefinitionId() { return null; }
        @Override public String getProcessInstanceId() { return null; }
        @Override public String getExecutionId() { return null; }
        @Override public String getScopeId() { return null; }
        @Override public String getScopeType() { return null; }
        @Override public String getScopeDefinitionId() { return null; }
        @Override public String getSubScopeId() { return null; }
    }
}
