package com.autonate.flowableevents;

import org.springframework.boot.actuate.endpoint.annotation.Endpoint;
import org.springframework.boot.actuate.endpoint.annotation.Selector;
import org.springframework.boot.actuate.endpoint.annotation.WriteOperation;

/**
 * #163. Starting one activity in a running ad-hoc subprocess, and completing it.
 *
 * <p>Separate from {@link AdhocSubProcessEndpoint} because an actuator endpoint's
 * operations share one selector shape, and these take two selectors where the
 * read takes one.
 *
 * <p><b>No authorization decision is made here.</b> This runs inside the engine
 * container behind the same boundary as the rest of the Flowable API; the
 * permission check and the audit event live in Auton8's own endpoint, which is
 * the split every other engine call already uses.
 */
@Endpoint(id = "adhocExecute")
final class AdhocSubProcessActionEndpoint {

    private final org.flowable.engine.RuntimeService runtimeService;

    AdhocSubProcessActionEndpoint(org.flowable.engine.RuntimeService runtimeService) {
        this.runtimeService = runtimeService;
    }

    /**
     * Starts one enabled activity, or completes the subprocess when
     * {@code activityId} is the literal {@code complete}.
     *
     * <p>Deliberately does not remove the activity from anything: an ad-hoc
     * activity may be started again, and that repeatability is the property that
     * distinguishes this element from a parallel subprocess.
     */
    @WriteOperation
    void execute(@Selector String executionId, @Selector String activityId) {
        if ("complete".equals(activityId)) {
            runtimeService.completeAdhocSubProcess(executionId);
            return;
        }

        runtimeService.executeActivityInAdhocSubProcess(executionId, activityId);
    }
}
