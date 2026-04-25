package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.lang.reflect.Proxy;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.Date;

import org.flowable.common.engine.api.FlowableIllegalArgumentException;
import org.flowable.engine.delegate.DelegateExecution;
import org.junit.jupiter.api.Test;

class DueDateHelperTests {

    private static final Instant FIXED_START = Instant.parse("2026-04-25T12:00:00Z");

    @Test
    void fromProcessStartAddsLiteralDays() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        Date result = helper.fromProcessStart(execution("p1"), 3);
        assertEquals(Date.from(FIXED_START.plus(3, ChronoUnit.DAYS)), result);
    }

    @Test
    void fromProcessStartAcceptsZeroDays() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        Date result = helper.fromProcessStart(execution("p1"), 0);
        assertEquals(Date.from(FIXED_START), result);
    }

    @Test
    void fromProcessStartAcceptsNumericString() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        Date result = helper.fromProcessStart(execution("p1"), "10");
        assertEquals(Date.from(FIXED_START.plus(10, ChronoUnit.DAYS)), result);
    }

    @Test
    void fromProcessStartRejectsNegativeDays() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        assertThrows(FlowableIllegalArgumentException.class, () -> helper.fromProcessStart(execution("p1"), -1));
    }

    @Test
    void fromProcessStartRejectsNonNumericValue() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        assertThrows(FlowableIllegalArgumentException.class, () -> helper.fromProcessStart(execution("p1"), "soon"));
    }

    @Test
    void fromProcessStartRejectsNullExecution() {
        DueDateHelper helper = helperReturning(Date.from(FIXED_START));
        assertThrows(FlowableIllegalArgumentException.class, () -> helper.fromProcessStart(null, 3));
    }

    private static DueDateHelper helperReturning(Date startTime) {
        return new DueDateHelper(null) {
            @Override
            protected Date resolveProcessStartTime(DelegateExecution execution) {
                return startTime;
            }
        };
    }

    private static DelegateExecution execution(String processInstanceId) {
        return (DelegateExecution) Proxy.newProxyInstance(
            DelegateExecution.class.getClassLoader(),
            new Class<?>[] { DelegateExecution.class },
            (proxy, method, args) -> {
                if ("getProcessInstanceId".equals(method.getName())) {
                    return processInstanceId;
                }
                Class<?> returnType = method.getReturnType();
                if (returnType == boolean.class) return false;
                if (returnType == int.class) return 0;
                if (returnType == long.class) return 0L;
                if (returnType.isPrimitive()) return 0;
                return null;
            });
    }
}
