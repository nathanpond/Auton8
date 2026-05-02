package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assertions.fail;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.HttpHandler;
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
import java.util.concurrent.atomic.AtomicReference;
import org.flowable.bpmn.model.ExtensionAttribute;
import org.flowable.bpmn.model.ServiceTask;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.engine.delegate.DelegateExecution;
import org.junit.jupiter.api.Test;

class AutoNateBehaviorDelegateTests {

    private static final String SECRET = "test-secret";
    private static final String FlowableNs = "http://flowable.org/bpmn";
    private static final ObjectMapper Mapper = new ObjectMapper();

    @Test
    void executeSendsSecretHeaderAndAppliesStringVariableUpdate() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        var responseBody = """
            {
              "variableUpdates": {
                "unlockResult": { "type": "string", "value": "unlocked" }
              },
              "failed": false
            }
            """;

        try (var fixture = HttpFixture.start(captured, 200, responseBody)) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.unlock-account");
            var execution = newExecution("p-1", "e-1", "behavior_flow:1:42", task,
                Map.of("userId", 42L));
            var delegate = newDelegate(fixture.baseUrl());

            delegate.execute(execution);

            assertEquals(SECRET, captured.get().headers.get("x-autonate-internal-token"));
            assertTrue(captured.get().path.endsWith("/api/workflow-behaviors/autonate.unlock-account/execute"),
                "unexpected path: " + captured.get().path);
            assertEquals("unlocked", execution.getVariable("unlockResult"));
        }
    }

    @Test
    void executeAppliesNumericVariableUpdates() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        var responseBody = """
            {
              "variableUpdates": {
                "count": { "type": "long", "value": 7 },
                "ratio": { "type": "double", "value": 1.5 },
                "ok": { "type": "bool", "value": true }
              }
            }
            """;
        try (var fixture = HttpFixture.start(captured, 200, responseBody)) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.test");
            var execution = newExecution("p-1", "e-1", "k:1:1", task, Map.of());
            var delegate = newDelegate(fixture.baseUrl());

            delegate.execute(execution);

            assertEquals(7L, execution.getVariable("count"));
            assertEquals(1.5d, execution.getVariable("ratio"));
            assertEquals(Boolean.TRUE, execution.getVariable("ok"));
        }
    }

    @Test
    void executeRemoveTypeRemovesVariable() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        var responseBody = """
            { "variableUpdates": { "transient": { "type": "remove" } } }
            """;
        try (var fixture = HttpFixture.start(captured, 200, responseBody)) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.test");
            var execution = newExecution("p", "e", "k:1:1", task, new LinkedHashMap<>(Map.of(
                "transient", "value-to-clear")));
            var delegate = newDelegate(fixture.baseUrl());

            delegate.execute(execution);

            assertNull(execution.getVariable("transient"));
        }
    }

    @Test
    void executeDoesNotThrow_WhenBehaviorReportsPredictableFailure() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        var responseBody = """
            {
              "variableUpdates": { "unlockResult": { "type": "string", "value": "userNotFound" } },
              "failed": true,
              "failureCode": "userNotFound",
              "failureMessage": "no such user"
            }
            """;
        try (var fixture = HttpFixture.start(captured, 200, responseBody)) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.unlock-account");
            var execution = newExecution("p", "e", "k:1:1", task, Map.of());
            var delegate = newDelegate(fixture.baseUrl());

            delegate.execute(execution);

            assertEquals("userNotFound", execution.getVariable("unlockResult"));
        }
    }

    @Test
    void executeThrows_OnHttpError() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        try (var fixture = HttpFixture.start(captured, 500, "{\"error\":\"boom\"}")) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.test");
            var execution = newExecution("p", "e", "k:1:1", task, Map.of());
            var delegate = newDelegate(fixture.baseUrl());

            assertThrows(FlowableException.class, () -> delegate.execute(execution));
        }
    }

    @Test
    void executeThrows_OnUnknownBehavior404() throws Exception {
        var captured = new AtomicReference<CapturedRequest>();
        try (var fixture = HttpFixture.start(captured, 404, "{\"error\":\"unknown_behavior\"}")) {
            var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.unknown");
            var execution = newExecution("p", "e", "k:1:1", task, Map.of());
            var delegate = newDelegate(fixture.baseUrl());

            assertThrows(FlowableException.class, () -> delegate.execute(execution));
        }
    }

    @Test
    void executeThrows_WhenAutonateServiceKindIsMissing() {
        var props = newProps(URI.create("http://localhost:1"));
        var delegate = new AutoNateBehaviorDelegate(HttpClient.newHttpClient(), Mapper, props);
        var task = new ServiceTask();
        task.setId("ServiceTask_1");
        task.addAttribute(flowableAttribute("behaviorKey", "autonate.unlock-account"));
        var execution = newExecution("p", "e", "k:1:1", task, Map.of());

        assertThrows(FlowableException.class, () -> delegate.execute(execution));
    }

    @Test
    void executeThrows_WhenServiceKindIsUnsupported() {
        var props = newProps(URI.create("http://localhost:1"));
        var delegate = new AutoNateBehaviorDelegate(HttpClient.newHttpClient(), Mapper, props);
        var task = serviceTaskWithBehavior("ServiceTask_1", "http-call", "autonate.unlock-account");
        var execution = newExecution("p", "e", "k:1:1", task, Map.of());

        assertThrows(FlowableException.class, () -> delegate.execute(execution));
    }

    @Test
    void executeThrows_WhenBehaviorKeyIsBlank() {
        var props = newProps(URI.create("http://localhost:1"));
        var delegate = new AutoNateBehaviorDelegate(HttpClient.newHttpClient(), Mapper, props);
        var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "");
        var execution = newExecution("p", "e", "k:1:1", task, Map.of());

        assertThrows(FlowableException.class, () -> delegate.execute(execution));
    }

    @Test
    void executeThrows_WhenCallbackBaseUrlMissing() {
        var props = new FlowableExecutionEventProperties();
        props.setCallbackSharedSecret(SECRET);
        // callbackBaseUrl intentionally null.
        var delegate = new AutoNateBehaviorDelegate(HttpClient.newHttpClient(), Mapper, props);
        var task = serviceTaskWithBehavior("ServiceTask_1", "behavior", "autonate.unlock-account");
        var execution = newExecution("p", "e", "k:1:1", task, Map.of());

        assertThrows(FlowableException.class, () -> delegate.execute(execution));
    }

    private static AutoNateBehaviorDelegate newDelegate(URI baseUrl) {
        return new AutoNateBehaviorDelegate(HttpClient.newHttpClient(), Mapper, newProps(baseUrl));
    }

    private static FlowableExecutionEventProperties newProps(URI baseUrl) {
        var props = new FlowableExecutionEventProperties();
        props.setCallbackBaseUrl(baseUrl);
        props.setCallbackSharedSecret(SECRET);
        return props;
    }

    private static ServiceTask serviceTaskWithBehavior(String id, String kind, String behaviorKey) {
        var task = new ServiceTask();
        task.setId(id);
        task.addAttribute(flowableAttribute("autonateServiceKind", kind));
        task.addAttribute(flowableAttribute("behaviorKey", behaviorKey));
        return task;
    }

    private static ExtensionAttribute flowableAttribute(String localName, String value) {
        var attr = new ExtensionAttribute(localName);
        attr.setNamespace(FlowableNs);
        attr.setNamespacePrefix("flowable");
        attr.setValue(value);
        return attr;
    }

    private static DelegateExecution newExecution(
        String processInstanceId,
        String executionId,
        String processDefinitionId,
        ServiceTask currentFlowElement,
        Map<String, Object> initialVariables
    ) {
        var variables = new LinkedHashMap<>(initialVariables);
        return (DelegateExecution) Proxy.newProxyInstance(
            DelegateExecution.class.getClassLoader(),
            new Class<?>[] { DelegateExecution.class },
            (proxy, method, args) -> switch (method.getName()) {
                case "getProcessInstanceId" -> processInstanceId;
                case "getId" -> executionId;
                case "getCurrentActivityId" -> currentFlowElement.getId();
                case "getCurrentFlowElement" -> currentFlowElement;
                case "getProcessDefinitionId" -> processDefinitionId;
                case "getProcessInstanceBusinessKey" -> null;
                case "getVariables" -> new HashMap<>(variables);
                case "getVariableInstances" -> new HashMap<>();
                case "getVariable" -> variables.get((String) args[0]);
                case "setVariable" -> {
                    variables.put((String) args[0], args[1]);
                    yield null;
                }
                case "removeVariable" -> {
                    variables.remove(args[0]);
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

    private record CapturedRequest(String path, Map<String, String> headers, JsonNode body) {}

    private static final class HttpFixture implements AutoCloseable {
        private final HttpServer server;
        private final URI baseUrl;

        private HttpFixture(HttpServer server, URI baseUrl) {
            this.server = server;
            this.baseUrl = baseUrl;
        }

        URI baseUrl() {
            return baseUrl;
        }

        static HttpFixture start(AtomicReference<CapturedRequest> captured, int statusCode, String responseBody)
            throws IOException {
            var server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            HttpHandler handler = exchange -> {
                try {
                    var headerMap = new HashMap<String, String>();
                    exchange.getRequestHeaders().forEach(
                        (name, values) -> headerMap.put(name.toLowerCase(), String.join(",", values)));
                    var body = exchange.getRequestBody().readAllBytes();
                    JsonNode parsed = body.length == 0 ? Mapper.nullNode() : Mapper.readTree(body);
                    captured.set(new CapturedRequest(
                        exchange.getRequestURI().getPath(), headerMap, parsed));

                    var bytes = responseBody.getBytes(StandardCharsets.UTF_8);
                    exchange.getResponseHeaders().add("Content-Type", "application/json");
                    exchange.sendResponseHeaders(statusCode, bytes.length);
                    try (var os = exchange.getResponseBody()) {
                        os.write(bytes);
                    }
                } catch (Exception e) {
                    fail("HttpFixture handler failed", e);
                } finally {
                    exchange.close();
                }
            };
            server.createContext("/", handler);
            server.start();
            var port = server.getAddress().getPort();
            return new HttpFixture(server, URI.create("http://127.0.0.1:" + port + "/"));
        }

        @Override
        public void close() {
            server.stop(0);
        }
    }
}
