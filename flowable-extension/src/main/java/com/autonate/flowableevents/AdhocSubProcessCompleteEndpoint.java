package com.autonate.flowableevents;

import java.util.Map;
import org.springframework.boot.actuate.endpoint.annotation.Endpoint;
import org.springframework.boot.actuate.endpoint.annotation.Selector;
import org.springframework.boot.actuate.endpoint.annotation.WriteOperation;
import org.springframework.boot.actuate.endpoint.web.WebEndpointResponse;

/**
 * #252. Completing a running ad-hoc subprocess.
 *
 * <p>Its own endpoint rather than a reserved {@code activityId} on
 * {@link AdhocSubProcessActionEndpoint}, because an actuator endpoint's
 * operations share one selector shape and completing takes one selector where
 * starting takes two.
 *
 * <p>The arrangement it replaces treated the literal activity id
 * {@code "complete"} as "finish the whole subprocess". An ad-hoc subprocess
 * containing {@code <userTask id="complete"/>} therefore answered 204 to a
 * request to start that task and completed the entire subprocess instead — a
 * silent wrong action, with nothing validating the id anywhere in the stack.
 *
 * <p>Failures are classified rather than thrown, for the reason set out on
 * {@link AdhocSubProcessActionEndpoint.AdhocFailures}: an operator who asks to
 * finish a section that still has an open activity gets 409 and the engine's own
 * sentence, not a 500 and "Could not complete 'adhoc'."
 */
@Endpoint(id = "adhocComplete")
final class AdhocSubProcessCompleteEndpoint {

    private final org.flowable.engine.RuntimeService runtimeService;

    AdhocSubProcessCompleteEndpoint(org.flowable.engine.RuntimeService runtimeService) {
        this.runtimeService = runtimeService;
    }

    @WriteOperation
    WebEndpointResponse<Map<String, String>> complete(@Selector String executionId) {
        return AdhocSubProcessActionEndpoint.AdhocFailures.classifying(
            () -> runtimeService.completeAdhocSubProcess(executionId));
    }
}
