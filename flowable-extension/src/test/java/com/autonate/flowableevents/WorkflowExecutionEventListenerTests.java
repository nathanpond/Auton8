package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.time.Instant;
import java.time.ZoneId;
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
}
