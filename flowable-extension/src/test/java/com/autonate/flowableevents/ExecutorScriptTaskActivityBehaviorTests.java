package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.lang.reflect.Proxy;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import org.flowable.bpmn.model.ExtensionAttribute;
import org.flowable.bpmn.model.ScriptTask;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.engine.delegate.DelegateExecution;
import org.junit.jupiter.api.Test;

/**
 * #147 / GHSA-82rh-gjhw-rg9r: script tasks must leave the JVM.
 *
 * <p>The load-bearing assertion in this class is
 * {@code scriptIsNeverEvaluatedInTheJvm}: it runs a script whose body would
 * reach {@code java.lang.System} if any JSR-223 engine evaluated it, and
 * asserts that what actually happens is an HTTP call carrying that text
 * verbatim. If the behaviour ever falls back to the engine's own script path,
 * that test fails rather than quietly re-opening the hole.
 */
class ExecutorScriptTaskActivityBehaviorTests {

    private static final String SECRET = "test-secret";
    private static final ObjectMapper Mapper = new ObjectMapper();

    /** The script from the advisory's proof of concept. */
    private static final String JvmEscapeScript =
        "var System = Java.type('java.lang.System'); System.exit(1);";

    @Test
    void scriptIsNeverEvaluatedInTheJvm() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":null,\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), JvmEscapeScript, null);
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

            // If the JVM evaluated this, the test JVM would exit rather than
            // reach the next line.
            behavior.runInSandbox(execution);

            var body = Mapper.readTree(captured.get());
            assertEquals(JvmEscapeScript, body.get("code").asText(),
                "the script must be forwarded verbatim, not evaluated here");
        }
    }

    @Test
    void variablesAreSentAndMutationsAreAppliedToTheExecution() throws Exception {
        var captured = new AtomicReference<String>();
        var response = "{\"result\":null,\"mutations\":{\"approved\":true,\"score\":7,\"note\":\"ok\"}}";
        try (var fixture = HttpFixture.start(captured, 200, response)) {
            var behavior = newBehavior(fixture.baseUrl(), "variables.set('approved', true);", null);
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of("total", 42L));

            behavior.runInSandbox(execution);

            var sent = Mapper.readTree(captured.get());
            assertEquals(42, sent.get("variables").get("total").asInt());
            assertEquals(SECRET, HttpFixture.lastSecret.get());

            assertEquals(Boolean.TRUE, execution.getVariable("approved"));
            assertEquals(7, execution.getVariable("score"));
            assertEquals("ok", execution.getVariable("note"));
        }
    }

    @Test
    void resultVariableIsSetFromTheReply() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":\"computed\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'computed';", "outcome");
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

            behavior.runInSandbox(execution);

            assertEquals("computed", execution.getVariable("outcome"));
        }
    }

    @Test
    void aNonJsonSafeVariableIsDroppedRatherThanMangled() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":null,\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 1;", null);
            var variables = new LinkedHashMap<String, Object>();
            variables.put("ok", "fine");
            variables.put("stream", new java.io.ByteArrayInputStream(new byte[] { 1, 2 }));
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", variables);

            behavior.runInSandbox(execution);

            var sent = Mapper.readTree(captured.get()).get("variables");
            assertTrue(sent.has("ok"));
            assertFalse(sent.has("stream"), "a non-JSON-safe variable must not be sent");
        }
    }

    @Test
    void aScriptErrorFailsTheActivityAndIsNotRetriedInTheJvm() throws Exception {
        var captured = new AtomicReference<String>();
        var body = "{\"error\":\"script_error\",\"message\":\"ReferenceError: nope\"}";
        try (var fixture = HttpFixture.start(captured, 422, body)) {
            var behavior = newBehavior(fixture.baseUrl(), "nope();", null);
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

            var thrown = assertThrows(FlowableException.class, () -> behavior.runInSandbox(execution));

            assertTrue(thrown.getMessage().contains("ReferenceError: nope"), thrown.getMessage());
            assertEquals(1, HttpFixture.calls.get(), "the script must be attempted exactly once");
        }
    }

    @Test
    void anUnreachableExecutorFailsClosedRatherThanRunningTheScriptLocally() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 503, "{\"error\":\"executor_unavailable\"}")) {
            var behavior = newBehavior(fixture.baseUrl(), JvmEscapeScript, null);
            var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

            // Fails closed. The alternative — running the script here because
            // the sandbox is down — would reinstate the vulnerability exactly
            // when the system is degraded.
            assertThrows(FlowableException.class, () -> behavior.runInSandbox(execution));
        }
    }

    @Test
    void anUnconfiguredCallbackFailsRatherThanFallingBackToTheJvm() {
        var properties = new FlowableExecutionEventProperties();
        var behavior = new ExecutorScriptTaskActivityBehavior(
            "ScriptTask_1", JvmEscapeScript, "javascript", null, null, false,
            HttpClient.newHttpClient(), Mapper, properties);
        var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

        var thrown = assertThrows(FlowableException.class, () -> behavior.runInSandbox(execution));
        assertTrue(thrown.getMessage().contains("callback base URL"), thrown.getMessage());
    }

    @Test
    void anEmptyScriptIsRefused() {
        var properties = propertiesFor(URI.create("http://localhost:1/"));
        var behavior = new ExecutorScriptTaskActivityBehavior(
            "ScriptTask_1", "   ", "javascript", null, null, false,
            HttpClient.newHttpClient(), Mapper, properties);
        var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

        assertThrows(FlowableException.class, () -> behavior.runInSandbox(execution));
    }

    @Test
    void theFactoryReplacesTheEnginesScriptTaskBehaviour() {
        var factory = new AutoNateActivityBehaviorFactory(
            HttpClient.newHttpClient(), Mapper, propertiesFor(URI.create("http://localhost:1/")));
        var task = new ScriptTask();
        task.setId("ScriptTask_1");
        task.setScript(JvmEscapeScript);
        task.setScriptFormat("javascript");

        var behavior = factory.createScriptTaskActivityBehavior(task);

        // The class identity IS the assertion: the parser asks the factory once
        // per script task, so anything other than our subclass here means the
        // engine's own JSR-223 evaluation is what would run.
        assertNotNull(behavior);
        assertEquals(ExecutorScriptTaskActivityBehavior.class, behavior.getClass());
    }

    @Test
    void groovyIsRefusedRatherThanForwardedToAJavaScriptSandbox() {
        // The base image still ships groovy and
        // flowable-groovy-script-static-engine. Neither can serve a script task
        // any more — this behaviour replaced the engine's script path — and a
        // Groovy body must not be shipped to a JS isolate, where it would fail
        // with a syntax error that explains nothing.
        var behavior = new ExecutorScriptTaskActivityBehavior(
            "ScriptTask_1", "System.exit(1)", "groovy", null, null, false,
            HttpClient.newHttpClient(), Mapper, propertiesFor(URI.create("http://localhost:1/")));
        var execution = newExecution("p-1", "e-1", "ScriptTask_1", Map.of());

        var thrown = assertThrows(FlowableException.class, () -> behavior.runInSandbox(execution));
        assertTrue(thrown.getMessage().contains("groovy"), thrown.getMessage());
        assertTrue(thrown.getMessage().contains("sandbox"), thrown.getMessage());
    }

    @Test
    void aPythonScriptTaskIsForwardedWithItsDeclaredFormat() throws Exception {
        // #154: the language is a front-end choice, so it travels with the
        // script rather than being assumed. Without this the host would route
        // every script task to the JavaScript runner.
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":null,\"mutations\":{}}")) {
            var behavior = new ExecutorScriptTaskActivityBehavior(
                "ScriptTask_1", "variables.set('x', 1)", "python", null, null, false,
                HttpClient.newHttpClient(), Mapper, propertiesFor(fixture.baseUrl()));
            behavior.runInSandbox(newExecution("p-1", "e-1", "ScriptTask_1", Map.of()));

            assertEquals("python", Mapper.readTree(captured.get()).get("scriptFormat").asText());
        }
    }

    @Test
    void supportIsReportedFromTheSandboxConfigurationNotTheJvmsScriptEngines() {
        // Before #147 this asked "is a JSR-223 JavaScript engine installed?",
        // which is now inverted: script tasks work because they do not use one.
        var unconfigured = new FlowableScriptTaskSupportService(new FlowableExecutionEventProperties());
        assertFalse(unconfigured.describeSupport().javaScriptSupported(),
            "an unconfigured sandbox must not report script tasks as supported, " +
            "however many JVM script engines happen to be on the classpath");

        var configured = new FlowableScriptTaskSupportService(
            propertiesFor(URI.create("http://localhost:1/")));
        assertTrue(configured.describeSupport().javaScriptSupported());
    }

    // --- helpers ---------------------------------------------------------

    private static FlowableExecutionEventProperties propertiesFor(URI baseUrl) {
        var properties = new FlowableExecutionEventProperties();
        properties.setCallbackBaseUrl(baseUrl);
        properties.setCallbackSharedSecret(SECRET);
        return properties;
    }

    // ── #218: the complex gateway's route contract ───────────────────────────

    @Test
    void aRoutingScriptIsToldWhichRoutesItMayReturn() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":\"fa\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return autonateRoutes[0];", "__autonateRoute_cg");
            behavior.runInSandbox(routedExecution("fa,fb"));

            var routes = Mapper.readTree(captured.get()).get("variables").get("autonateRoutes");
            assertNotNull(routes, "the routes must reach the sandbox as a variable");
            assertEquals(2, routes.size());
            assertEquals("fa", routes.get(0).asText());
            assertEquals("fb", routes.get(1).asText());
        }
    }

    @Test
    void aRouteOutsideTheAllowedSetFailsTheActivity() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":\"nowhere\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'nowhere';", "__autonateRoute_cg");

            var thrown = assertThrows(FlowableException.class,
                () -> behavior.runInSandbox(routedExecution("fa,fb")));

            // The message has to name BOTH halves. "Invalid route" sends an
            // author looking at the gateway; naming what came back and what was
            // allowed usually shows them the typo directly.
            assertTrue(thrown.getMessage().contains("nowhere"), thrown.getMessage());
            assertTrue(thrown.getMessage().contains("fa"), thrown.getMessage());
            assertTrue(thrown.getMessage().contains("fb"), thrown.getMessage());
        }
    }

    @Test
    void aRoutingScriptReturningNullFailsRatherThanTakingTheDefault() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":null,\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "// forgot to return", "__autonateRoute_cg");

            // A script with no return is the likeliest mistake of all, and it is
            // exactly the one that would otherwise fall through to the default
            // flow and look like a deliberate choice.
            var thrown = assertThrows(FlowableException.class,
                () -> behavior.runInSandbox(routedExecution("fa,fb")));
            assertTrue(thrown.getMessage().contains("null"), thrown.getMessage());
        }
    }

    @Test
    void aValidRouteIsStoredAndDoesNotThrow() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":\"fb\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'fb';", "__autonateRoute_cg");
            var execution = routedExecution("fa,fb");

            behavior.runInSandbox(execution);

            // The complement of the two tests above: enforcement that rejected
            // everything would satisfy them both and break every gateway.
            assertEquals("fb", execution.getVariable("__autonateRoute_cg"));
        }
    }

    @Test
    void anOrdinaryScriptTaskIsUnaffectedByTheRouteContract() throws Exception {
        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(captured, 200, "{\"result\":\"anything\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'anything';", "outcome");

            // No route list on the element, so no contract. Without this the
            // check would fail every script task in the product that returns a
            // value, which no other test here would catch.
            behavior.runInSandbox(routedExecution(null));

            var body = Mapper.readTree(captured.get());
            assertTrue(body.get("variables").get("autonateRoutes") == null,
                "an ordinary script task must not be handed a route list");
        }
    }

    /**
     * An execution whose current flow element carries the expansion's route list.
     * Pass null for an ordinary script task with no route list at all.
     */
    private static DelegateExecution routedExecution(String allowedRoutes) {
        var element = new ScriptTask();
        element.setId("cg__autonateRoute");
        if (allowedRoutes != null) {
            var attribute = new ExtensionAttribute("autonateAllowedRoutes");
            attribute.setNamespace("http://flowable.org/bpmn");
            attribute.setValue(allowedRoutes);
            element.addAttribute(attribute);
        }
        return newExecution("p-1", "e-1", "cg__autonateRoute", Map.of(), element);
    }

    private static ExecutorScriptTaskActivityBehavior newBehavior(
        URI baseUrl, String script, String resultVariable
    ) {
        return new ExecutorScriptTaskActivityBehavior(
            "ScriptTask_1", script, "javascript", resultVariable, null, false,
            HttpClient.newHttpClient(), Mapper, propertiesFor(baseUrl));
    }

    private static DelegateExecution newExecution(
        String processInstanceId, String executionId, String activityId, Map<String, Object> initial
    ) {
        return newExecution(processInstanceId, executionId, activityId, initial, null);
    }

    private static DelegateExecution newExecution(
        String processInstanceId, String executionId, String activityId, Map<String, Object> initial,
        Object currentFlowElement
    ) {
        var variables = new LinkedHashMap<>(initial);
        return (DelegateExecution) Proxy.newProxyInstance(
            DelegateExecution.class.getClassLoader(),
            new Class<?>[] { DelegateExecution.class },
            (proxy, method, args) -> switch (method.getName()) {
                case "getProcessInstanceId" -> processInstanceId;
                case "getId" -> executionId;
                case "getCurrentActivityId" -> activityId;
                case "getCurrentFlowElement" -> currentFlowElement;
                case "getVariables" -> new HashMap<>(variables);
                case "getVariable" -> variables.get((String) args[0]);
                case "setVariable" -> {
                    variables.put((String) args[0], args[1]);
                    yield null;
                }
                default -> {
                    Class<?> returnType = method.getReturnType();
                    if (returnType == boolean.class) yield false;
                    if (returnType == int.class) yield 0;
                    if (returnType == long.class) yield 0L;
                    if (returnType.isPrimitive()) yield 0;
                    yield null;
                }
            });
    }

    private record HttpFixture(HttpServer server) implements AutoCloseable {
        static final AtomicReference<String> lastSecret = new AtomicReference<>();
        static final AtomicInteger calls = new AtomicInteger();

        static HttpFixture start(AtomicReference<String> capturedBody, int status, String responseBody)
            throws IOException {
            lastSecret.set(null);
            calls.set(0);
            var server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            server.createContext("/", exchange -> {
                calls.incrementAndGet();
                lastSecret.set(exchange.getRequestHeaders().getFirst("X-AutoNate-Internal-Token"));
                capturedBody.set(new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8));
                var bytes = responseBody.getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().add("Content-Type", "application/json");
                exchange.sendResponseHeaders(status, bytes.length);
                try (var out = exchange.getResponseBody()) {
                    out.write(bytes);
                }
            });
            server.start();
            return new HttpFixture(server);
        }

        URI baseUrl() {
            return URI.create("http://127.0.0.1:" + server.getAddress().getPort() + "/");
        }

        @Override
        public void close() {
            server.stop(0);
        }
    }
}
