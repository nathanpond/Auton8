package com.autonate.flowableevents;

import java.util.ArrayList;
import java.util.List;
import org.springframework.boot.actuate.endpoint.annotation.Endpoint;
import org.springframework.boot.actuate.endpoint.annotation.ReadOperation;
import org.springframework.boot.actuate.endpoint.annotation.Selector;

/**
 * #163. Which activities can be started in a running ad-hoc subprocess.
 *
 * <p>Flowable implements the element — {@code AdhocSubProcessActivityBehavior}
 * ships in flowable-engine 8.0.0 and {@code RuntimeService} carries the commands
 * — but {@code flowable-rest} exposes none of it, so Auton8 has nothing to call.
 *
 * <h2>Why an actuator endpoint and not a {@code @RestController}</h2>
 *
 * <p>The story prescribed following {@link FlowableScriptTaskSupportController},
 * a {@code @RestController} under {@code /service/autonate/}. **That half of the
 * precedent does not work.** It registers as a bean and its route is never
 * mapped — Flowable's REST application does not include this package in its
 * handler mapping — so it answers:
 *
 * <pre>No endpoint GET /flowable-rest/service/autonate/script-task-support.</pre>
 *
 * <p>The half that works is the actuator {@code @Endpoint} beside it, which is
 * exactly why {@code FlowableClient} probes {@code actuator/scriptTaskSupport}
 * FIRST and treats the {@code /service/} path as a fallback. Following the
 * written precedent would have produced an endpoint nothing could reach.
 *
 * @see AdhocSubProcessActionEndpoint for starting an activity
 */
@Endpoint(id = "adhocActivities")
final class AdhocSubProcessEndpoint {

    private final org.flowable.engine.RuntimeService runtimeService;

    AdhocSubProcessEndpoint(org.flowable.engine.RuntimeService runtimeService) {
        this.runtimeService = runtimeService;
    }

    @ReadOperation
    List<AdhocSubProcessState> enabledActivities(@Selector String processInstanceId) {
        var states = new ArrayList<AdhocSubProcessState>();

        for (var execution : runtimeService.createExecutionQuery()
            .processInstanceId(processInstanceId)
            .list()) {

            var activityId = execution.getActivityId();
            if (activityId == null) continue;

            List<org.flowable.bpmn.model.FlowNode> enabled;
            try {
                enabled = runtimeService.getEnabledActivitiesFromAdhocSubProcess(execution.getId());
            } catch (Exception exception) {
                // Not an ad-hoc subprocess. The execution query cannot filter on
                // that, so asking and moving on is the only way to tell — and one
                // ordinary execution must not fail the whole listing.
                continue;
            }

            var activities = new ArrayList<AdhocActivity>(enabled.size());
            for (var node : enabled) {
                activities.add(new AdhocActivity(node.getId(), node.getName()));
            }
            states.add(new AdhocSubProcessState(execution.getId(), activityId, activities));
        }

        return states;
    }

    /** One ad-hoc subprocess execution and what can be started in it. */
    record AdhocSubProcessState(String executionId, String activityId, List<AdhocActivity> enabledActivities) { }

    /** One activity a person may start. */
    record AdhocActivity(String id, String name) { }
}
