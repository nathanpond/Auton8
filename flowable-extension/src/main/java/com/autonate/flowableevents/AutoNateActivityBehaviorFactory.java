package com.autonate.flowableevents;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.net.http.HttpClient;
import org.flowable.bpmn.model.ScriptTask;
import org.flowable.engine.impl.bpmn.behavior.ScriptTaskActivityBehavior;
import org.flowable.engine.impl.bpmn.parser.factory.DefaultActivityBehaviorFactory;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Replaces the behaviour the engine builds for BPMN script tasks (#147).
 *
 * <p>This is the single seam that decides where author code runs. The BPMN
 * parser asks this factory for a behaviour once per script task at deployment
 * time; returning {@link ExecutorScriptTaskActivityBehavior} means the engine's
 * own JSR-223 evaluation is never constructed, so there is no second path a
 * script could take.
 *
 * <p>Everything else is inherited from {@link DefaultActivityBehaviorFactory}
 * unchanged — this deliberately overrides one method, so a Flowable upgrade
 * that adds behaviours does not silently route around it.
 */
public class AutoNateActivityBehaviorFactory extends DefaultActivityBehaviorFactory {

    private static final Logger Logger = LoggerFactory.getLogger(AutoNateActivityBehaviorFactory.class);

    private final HttpClient httpClient;
    private final ObjectMapper objectMapper;
    private final FlowableExecutionEventProperties properties;

    public AutoNateActivityBehaviorFactory(
        HttpClient httpClient,
        ObjectMapper objectMapper,
        FlowableExecutionEventProperties properties
    ) {
        this.httpClient = httpClient;
        this.objectMapper = objectMapper;
        this.properties = properties;
    }

    @Override
    public ScriptTaskActivityBehavior createScriptTaskActivityBehavior(ScriptTask scriptTask) {
        Logger.debug(
            "Routing script task '{}' to the AutoNate executor sandbox rather than the JVM script engine.",
            scriptTask.getId());
        return new ExecutorScriptTaskActivityBehavior(
            scriptTask.getId(),
            scriptTask.getScript(),
            scriptTask.getScriptFormat(),
            scriptTask.getResultVariable(),
            scriptTask.getSkipExpression(),
            scriptTask.isAutoStoreVariables(),
            httpClient,
            objectMapper,
            properties);
    }
}
