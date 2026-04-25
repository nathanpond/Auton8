package com.autonate.flowableevents;

import java.time.Duration;
import java.util.Date;
import org.flowable.common.engine.api.FlowableIllegalArgumentException;
import org.flowable.common.engine.api.delegate.Expression;
import org.flowable.engine.HistoryService;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.history.HistoricProcessInstance;

public class DueDateHelper {

    private final HistoryService historyService;

    public DueDateHelper(HistoryService historyService) {
        this.historyService = historyService;
    }

    public Date fromProcessStart(DelegateExecution execution, Object days) {
        if (execution == null) {
            throw new FlowableIllegalArgumentException("dueDateHelper.fromProcessStart requires an execution.");
        }

        long resolvedDays = resolveDays(execution, days);
        Date startTime = resolveProcessStartTime(execution);
        return Date.from(startTime.toInstant().plus(Duration.ofDays(resolvedDays)));
    }

    protected Date resolveProcessStartTime(DelegateExecution execution) {
        String processInstanceId = execution.getProcessInstanceId();
        if (processInstanceId == null || processInstanceId.isBlank()) {
            throw new FlowableIllegalArgumentException(
                "dueDateHelper.fromProcessStart requires a process instance id on the execution.");
        }

        HistoricProcessInstance instance = historyService.createHistoricProcessInstanceQuery()
            .processInstanceId(processInstanceId)
            .singleResult();

        if (instance == null || instance.getStartTime() == null) {
            throw new FlowableIllegalArgumentException(
                "dueDateHelper.fromProcessStart could not resolve a start time for process instance '"
                    + processInstanceId + "'.");
        }

        return instance.getStartTime();
    }

    private long resolveDays(DelegateExecution execution, Object days) {
        Object resolved = days;
        if (resolved instanceof Expression expression) {
            resolved = expression.getValue(execution);
        }

        if (resolved == null) {
            throw new FlowableIllegalArgumentException("dueDateHelper.fromProcessStart requires a non-null days value.");
        }

        long value;
        if (resolved instanceof Number number) {
            value = number.longValue();
        } else {
            try {
                value = Long.parseLong(resolved.toString().trim());
            } catch (NumberFormatException ex) {
                throw new FlowableIllegalArgumentException(
                    "dueDateHelper.fromProcessStart days must be numeric, got '" + resolved + "'.");
            }
        }

        if (value < 0) {
            throw new FlowableIllegalArgumentException(
                "dueDateHelper.fromProcessStart days must be >= 0, got " + value + ".");
        }

        return value;
    }
}
