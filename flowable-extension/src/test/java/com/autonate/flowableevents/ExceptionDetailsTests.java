package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class ExceptionDetailsTests {

    @Test
    void rootCauseMessage_returnsTopMessage_whenNoCause() {
        var ex = new RuntimeException("boom");
        assertEquals("boom", ExceptionDetails.rootCauseMessage(ex));
    }

    @Test
    void rootCauseMessage_walksCauseChain_returnsDeepest() {
        var deepest = new IllegalStateException("script line 7: x is undefined");
        var middle = new RuntimeException("Problem evaluating script", deepest);
        var top = new RuntimeException("Job execution failed", middle);
        assertEquals("script line 7: x is undefined", ExceptionDetails.rootCauseMessage(top));
    }

    @Test
    void rootCauseMessage_nullThrowable_returnsNull() {
        assertNull(ExceptionDetails.rootCauseMessage(null));
    }

    @Test
    void rootCauseMessage_terminatesOnSelfReferentialCause() {
        // Some libraries set t.cause = t to break naive walkers. The helper
        // must not infinite-loop when getCause() == this.
        var ex = new SelfCausingException("loop");
        assertEquals("loop", ExceptionDetails.rootCauseMessage(ex));
    }

    @Test
    void fullStackTrace_includesClassAndMessage() {
        var ex = new RuntimeException("hello");
        var trace = ExceptionDetails.fullStackTrace(ex);
        assertTrue(trace.contains("RuntimeException"), "trace should contain exception class");
        assertTrue(trace.contains("hello"), "trace should contain message");
        assertTrue(trace.contains("at "), "trace should contain frame markers");
    }

    @Test
    void fullStackTrace_includesCauseChain() {
        var deepest = new IllegalStateException("deep");
        var top = new RuntimeException("top", deepest);
        var trace = ExceptionDetails.fullStackTrace(top);
        assertTrue(trace.contains("Caused by"), "trace should print causes");
        assertTrue(trace.contains("deep"), "trace should include cause message");
    }

    @Test
    void fullStackTrace_nullThrowable_returnsNull() {
        assertNull(ExceptionDetails.fullStackTrace(null));
    }

    private static final class SelfCausingException extends RuntimeException {
        SelfCausingException(String m) {
            super(m);
        }

        @Override
        public synchronized Throwable getCause() {
            return this;
        }
    }
}
