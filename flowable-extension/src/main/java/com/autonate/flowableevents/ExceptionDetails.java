package com.autonate.flowableevents;

import java.io.PrintWriter;
import java.io.StringWriter;

// Helpers for extracting human-readable text from a Throwable raised inside
// the Flowable job executor. The values feed the workflow_execution_errors
// table: error_message <- rootCauseMessage; error_stack_trace <- fullStackTrace.
final class ExceptionDetails {

    private ExceptionDetails() {
    }

    static String rootCauseMessage(Throwable t) {
        if (t == null) {
            return null;
        }
        var current = t;
        // Self-referential cause is legal in Java (some libraries set it to
        // break getCause() == null checks). Bail when current.getCause() == current.
        while (current.getCause() != null && current.getCause() != current) {
            current = current.getCause();
        }
        return current.getMessage();
    }

    static String fullStackTrace(Throwable t) {
        if (t == null) {
            return null;
        }
        var sw = new StringWriter();
        t.printStackTrace(new PrintWriter(sw));
        return sw.toString();
    }
}
