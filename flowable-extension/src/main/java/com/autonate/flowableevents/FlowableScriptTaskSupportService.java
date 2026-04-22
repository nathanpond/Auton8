package com.autonate.flowableevents;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import javax.script.ScriptEngineFactory;
import javax.script.ScriptEngineManager;

final class FlowableScriptTaskSupportService {

    FlowableScriptTaskSupportResponse describeSupport() {
        var manager = new ScriptEngineManager();
        var engineFactories = manager.getEngineFactories();
        var engineNames = engineFactories.stream()
            .flatMap(factory -> collectNames(factory).stream())
            .toList();

        var javaScriptSupported = engineFactories.stream().anyMatch(this::isJavaScriptEngine);
        return new FlowableScriptTaskSupportResponse(javaScriptSupported, engineNames);
    }

    private boolean isJavaScriptEngine(ScriptEngineFactory factory) {
        return collectNames(factory).stream().anyMatch(name ->
            name.equalsIgnoreCase("javascript")
                || name.equalsIgnoreCase("js")
                || name.equalsIgnoreCase("graal.js")
                || name.equalsIgnoreCase("graaljs"));
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
