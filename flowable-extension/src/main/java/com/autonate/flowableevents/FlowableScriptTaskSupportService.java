package com.autonate.flowableevents;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import javax.script.ScriptEngineFactory;
import javax.script.ScriptEngineManager;

/**
 * Reports whether this Flowable runtime can serve BPMN script tasks.
 *
 * <p><strong>What "supported" means changed with #147.</strong> This used to
 * answer "is a JSR-223 JavaScript engine installed?", and AutoNate.Web refused
 * to publish a workflow when the answer was no. That question is now not merely
 * irrelevant but inverted: script tasks work precisely <em>because</em> they no
 * longer touch a JVM script engine. Left as it was, the probe would have
 * blocked publishing on a hardened image with Nashorn removed — a deployment
 * where script tasks are safer and work fine — while happily reporting
 * "supported" on a deployment whose sandbox is not configured at all, where
 * every script task fails at runtime.
 *
 * <p>So the verdict now comes from the thing that actually decides it: whether
 * the executor callback is configured. The JSR-223 engine list is still
 * reported, because it remains useful for diagnosing an image, but it no longer
 * determines the answer.
 */
final class FlowableScriptTaskSupportService {

    private final FlowableExecutionEventProperties properties;

    FlowableScriptTaskSupportService(FlowableExecutionEventProperties properties) {
        this.properties = properties;
    }

    FlowableScriptTaskSupportResponse describeSupport() {
        var secret = properties.getCallbackSharedSecret();
        var sandboxConfigured = properties.getCallbackBaseUrl() != null
            && secret != null
            && !secret.isBlank();

        var names = new ArrayList<String>();
        names.add(sandboxConfigured
            ? "autonate-executor-sandbox"
            : "autonate-executor-sandbox (not configured)");
        // Retained for diagnosis only — see the class comment. These engines
        // are not used to run script tasks.
        for (var factory : new ScriptEngineManager().getEngineFactories()) {
            names.addAll(collectNames(factory));
        }

        return new FlowableScriptTaskSupportResponse(sandboxConfigured, List.copyOf(names));
    }

    private List<String> collectNames(ScriptEngineFactory factory) {
        Set<String> names = new LinkedHashSet<>();
        for (var name : factory.getNames()) {
            if (name != null && !name.isBlank()) {
                names.add(name);
            }
        }

        if (factory.getEngineName() != null && !factory.getEngineName().isBlank()) {
            names.add(factory.getEngineName());
        }

        return names.stream()
            .map(name -> name.toLowerCase(Locale.ROOT))
            .toList();
    }
}
