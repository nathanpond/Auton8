package com.autonate.flowableevents;

import java.util.Map;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.common.engine.api.FlowableIllegalArgumentException;
import org.flowable.common.engine.api.FlowableObjectNotFoundException;
import org.springframework.boot.actuate.endpoint.annotation.Endpoint;
import org.springframework.boot.actuate.endpoint.annotation.Selector;
import org.springframework.boot.actuate.endpoint.annotation.WriteOperation;
import org.springframework.boot.actuate.endpoint.web.WebEndpointResponse;

/**
 * #163. Starting one activity in a running ad-hoc subprocess.
 *
 * <p>Separate from {@link AdhocSubProcessEndpoint} because an actuator endpoint's
 * operations share one selector shape, and these take two selectors where the
 * read takes one. Completing the subprocess is a third endpoint,
 * {@link AdhocSubProcessCompleteEndpoint}, for the reason below.
 *
 * <p><b>No authorization decision is made here.</b> This runs inside the engine
 * container behind the same boundary as the rest of the Flowable API; the
 * permission check and the audit event live in Auton8's own endpoint, which is
 * the split every other engine call already uses.
 *
 * <h2>Why completion moved out (#252)</h2>
 *
 * <p>This operation used to accept the literal {@code activityId} of
 * {@code "complete"} as "finish the whole subprocess", so that starting and
 * completing could share one selector shape. That made {@code complete} a
 * reserved activity id with nothing enforcing it: an ad-hoc subprocess
 * containing {@code <userTask id="complete"/>} answered 204 to a request to
 * START that task and silently completed the entire subprocess instead, and the
 * parent advanced. Nothing in the studio, the publish validation or this class
 * refused the id.
 *
 * <p>A second endpoint costs one class and removes the collision outright, which
 * is worth more than a guard that has to be remembered. {@code complete} is now
 * an ordinary activity id.
 */
@Endpoint(id = "adhocExecute")
final class AdhocSubProcessActionEndpoint {

    private final org.flowable.engine.RuntimeService runtimeService;

    AdhocSubProcessActionEndpoint(org.flowable.engine.RuntimeService runtimeService) {
        this.runtimeService = runtimeService;
    }

    /**
     * Starts one enabled activity.
     *
     * <p>Deliberately does not remove the activity from anything: an ad-hoc
     * activity may be started again, and that repeatability is the property that
     * distinguishes this element from a parallel subprocess.
     */
    @WriteOperation
    WebEndpointResponse<Map<String, String>> execute(
        @Selector String executionId, @Selector String activityId) {

        return AdhocFailures.classifying(
            () -> runtimeService.executeActivityInAdhocSubProcess(executionId, activityId));
    }

    /**
     * Turns the engine's own exceptions into the status it already decided (#252).
     *
     * <p>Everything an ad-hoc caller can get wrong used to arrive at Auton8 as a
     * <b>500</b>: an unknown activity id, a non-existent execution, an activity
     * that is not enabled. So {@code FlowableRequestException.IsCallerError} was
     * never true on this route, every {@code catch ... when (IsCallerError)} on
     * the .NET side was dead code, and the engine's genuinely useful message —
     * "activity is not enabled" — was discarded in favour of a bare 500 that the
     * studio rendered as "Could not complete 'adhoc'."
     *
     * <p>An actuator write operation returning {@link WebEndpointResponse} sets
     * its own status, which is what makes this possible without a
     * {@code @RestController} (see {@link AdhocSubProcessEndpoint} for why one
     * cannot be reached here).
     */
    static final class AdhocFailures {

        private AdhocFailures() {
        }

        interface EngineCall {
            void run();
        }

        static WebEndpointResponse<Map<String, String>> classifying(EngineCall call) {
            try {
                call.run();
                return new WebEndpointResponse<>(null, WebEndpointResponse.STATUS_NO_CONTENT);
            } catch (FlowableObjectNotFoundException exception) {
                return failure(exception, WebEndpointResponse.STATUS_NOT_FOUND);
            } catch (FlowableIllegalArgumentException exception) {
                return failure(exception, WebEndpointResponse.STATUS_BAD_REQUEST);
            } catch (FlowableException exception) {
                // The engine refused on the state of the instance rather than on
                // the shape of the request: "Ad-hoc sub process has running child
                // executions that need to be completed first", "The activity is
                // not enabled". Retrying the identical request once the instance
                // has moved on succeeds, which is what makes 409 the honest code
                // and 500 the misleading one.
                return failure(exception, 409);
            }
        }

        private static WebEndpointResponse<Map<String, String>> failure(
            RuntimeException exception, int status) {

            var message = exception.getMessage();
            return new WebEndpointResponse<>(
                Map.of("message", message == null ? exception.toString() : message),
                status);
        }
    }
}
