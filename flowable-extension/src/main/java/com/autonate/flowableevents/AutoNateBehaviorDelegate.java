package com.autonate.flowableevents;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.io.IOException;
import java.math.BigDecimal;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.Date;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import org.flowable.bpmn.model.BaseElement;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.delegate.JavaDelegate;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Bridge between Flowable service tasks and AutoNate workflow behaviors.
 *
 * <p>Wired in BPMN as
 * {@code flowable:delegateExpression="${autonateBehaviorDelegate}"} with two
 * extension attributes the studio writes onto the serviceTask element:
 * {@code flowable:autonateServiceKind} (always {@code "behavior"} v1) and
 * {@code flowable:behaviorKey} (the registered behavior id). The delegate
 * reads them off {@code execution.getCurrentFlowElement()} at execute time.
 *
 * <p>Synchronously POSTs the execution context + filtered process variables
 * to AutoNate.Web's {@code /api/workflow-behaviors/&#123;key&#125;/execute}
 * endpoint. Predictable failures come back as {@code failed: true} on the
 * result; this method does <strong>not</strong> throw for those — the
 * workflow author handles them via a downstream gateway. System failures
 * (HTTP non-2xx, timeout, IOException, missing config, malformed response)
 * throw {@link FlowableException} so Flowable's job-executor retries hit
 * the existing JOB_EXECUTION_FAILURE pipeline.
 */
public class AutoNateBehaviorDelegate implements JavaDelegate {

    private static final String FlowableExtensionNamespace = "http://flowable.org/bpmn";

    private static final Logger Logger = LoggerFactory.getLogger(AutoNateBehaviorDelegate.class);

    private static final String CallbackSecretHeader = "X-AutoNate-Internal-Token";
    private static final String CorrelationIdHeader = "X-Correlation-Id";

    // Flowable variable type names that round-trip safely as JSON. Anything
    // else (e.g. "serializable" — Java byte streams) is dropped from the
    // outbound payload with a warning. Behaviors that need richer types
    // should ship them through json-typed variables instead.
    private static final Set<String> AllowedVariableTypeNames = Set.of(
        "string",
        "boolean",
        "integer",
        "long",
        "short",
        "double",
        "bigdecimal",
        "date",
        "instant",
        "json"
    );

    private final HttpClient httpClient;
    private final ObjectMapper objectMapper;
    private final FlowableExecutionEventProperties properties;

    public AutoNateBehaviorDelegate(FlowableExecutionEventProperties properties) {
        this(defaultHttpClient(properties), defaultObjectMapper(), properties);
    }

    AutoNateBehaviorDelegate(
        HttpClient httpClient,
        ObjectMapper objectMapper,
        FlowableExecutionEventProperties properties
    ) {
        this.httpClient = httpClient;
        this.objectMapper = objectMapper;
        this.properties = properties;
    }

    @Override
    public void execute(DelegateExecution execution) {
        var flowElement = execution.getCurrentFlowElement();
        if (!(flowElement instanceof BaseElement baseElement)) {
            throw new FlowableException(
                "Service task '" + execution.getCurrentActivityId() +
                "' is not a recognized BPMN element.");
        }

        var resolvedKind = readFlowableAttribute(baseElement, "autonateServiceKind");
        if (resolvedKind == null || resolvedKind.isBlank()) {
            throw new FlowableException(
                "Service task '" + execution.getCurrentActivityId() +
                "' is missing required attribute 'flowable:autonateServiceKind'.");
        }
        if (!"behavior".equals(resolvedKind)) {
            throw new FlowableException(
                "Service task '" + execution.getCurrentActivityId() +
                "' has unsupported autonateServiceKind '" + resolvedKind + "'.");
        }

        var resolvedKey = readFlowableAttribute(baseElement, "behaviorKey");
        if (resolvedKey == null || resolvedKey.isBlank()) {
            throw new FlowableException(
                "Service task '" + execution.getCurrentActivityId() +
                "' is missing required attribute 'flowable:behaviorKey'.");
        }

        var callbackBase = properties.getCallbackBaseUrl();
        var sharedSecret = properties.getCallbackSharedSecret();
        if (callbackBase == null) {
            throw new FlowableException(
                "Workflow-behavior callback base URL is not configured (autonate.flowable-events.callback-base-url).");
        }
        if (sharedSecret == null || sharedSecret.isBlank()) {
            throw new FlowableException(
                "Workflow-behavior callback shared secret is not configured (autonate.flowable-events.callback-shared-secret).");
        }

        var correlationId = UUID.randomUUID().toString();
        var requestBody = buildRequestBody(execution, correlationId);

        var encodedKey = URLEncoder.encode(resolvedKey, StandardCharsets.UTF_8);
        var endpointUri = callbackBase.resolve(
            normaliseBasePath(callbackBase) + "api/workflow-behaviors/" + encodedKey + "/execute");

        Logger.info(
            "Invoking workflow behavior '{}' for activity {} (process {}, correlationId {}).",
            resolvedKey, execution.getCurrentActivityId(), execution.getProcessInstanceId(), correlationId);

        HttpResponse<String> response;
        try {
            byte[] payload = objectMapper.writeValueAsBytes(requestBody);
            var request = HttpRequest.newBuilder(endpointUri)
                .header("Content-Type", "application/json")
                .header(CallbackSecretHeader, sharedSecret)
                .header(CorrelationIdHeader, correlationId)
                .POST(HttpRequest.BodyPublishers.ofByteArray(payload))
                .timeout(Duration.ofSeconds(Math.max(1, properties.getBehaviorTimeoutSeconds())))
                .build();
            response = httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (IOException exception) {
            throw new FlowableException(
                "Workflow-behavior callback to '" + endpointUri + "' failed: " + exception.getMessage(),
                exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new FlowableException(
                "Workflow-behavior callback to '" + endpointUri + "' was interrupted.",
                exception);
        }

        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            throw new FlowableException(
                "Workflow-behavior callback to '" + endpointUri + "' returned HTTP " + response.statusCode() +
                " (correlationId " + correlationId + ").");
        }

        JsonNode parsed;
        try {
            parsed = objectMapper.readTree(response.body() == null ? "{}" : response.body());
        } catch (IOException exception) {
            throw new FlowableException(
                "Workflow-behavior callback returned a body that wasn't valid JSON (correlationId " + correlationId + ").",
                exception);
        }

        applyVariableUpdates(execution, parsed.get("variableUpdates"));

        if (parsed.path("failed").asBoolean(false)) {
            // Predictable failure: the behavior chose to set a status
            // variable (already applied above). Don't throw — let the
            // workflow's gateway branch on it.
            Logger.info(
                "Workflow behavior '{}' reported predictable failure '{}' for process {} (correlationId {}).",
                resolvedKey,
                parsed.path("failureCode").asText(""),
                execution.getProcessInstanceId(),
                correlationId);
        } else {
            Logger.debug(
                "Workflow behavior '{}' completed successfully for process {} (correlationId {}).",
                resolvedKey, execution.getProcessInstanceId(), correlationId);
        }
    }

    // Snapshots execution variables (filtered to JSON-friendly types) and
    // identity into the request body. The variables map matches the
    // BehaviorContext.Variables shape on the C# side.
    private ObjectNode buildRequestBody(DelegateExecution execution, String correlationId) {
        var root = objectMapper.createObjectNode();
        root.put("processInstanceId", execution.getProcessInstanceId());
        root.put("executionId", execution.getId());
        root.put("processDefinitionKey", safeProcessDefinitionKey(execution));
        root.set("processName", null); // Engine-side enrichment lives in the events listener; the bridge keeps the bridge simple.
        root.put("activityId", execution.getCurrentActivityId());
        root.set("businessKey", execution.getProcessInstanceBusinessKey() == null
            ? null
            : objectMapper.getNodeFactory().textNode(execution.getProcessInstanceBusinessKey()));
        root.put("correlationId", correlationId);

        var variablesNode = root.putObject("variables");
        var instances = execution.getVariableInstances();
        var dropped = new HashSet<String>();
        if (instances != null) {
            for (var entry : instances.entrySet()) {
                var name = entry.getKey();
                var instance = entry.getValue();
                var typeName = instance == null ? null : instance.getTypeName();
                if (typeName != null && !AllowedVariableTypeNames.contains(typeName.toLowerCase())) {
                    dropped.add(name);
                    continue;
                }
                var value = instance == null ? null : instance.getValue();
                variablesNode.set(name, toJsonNode(value));
            }
        }
        if (!dropped.isEmpty()) {
            Logger.warn(
                "Workflow behavior callback dropped non-JSON-friendly process variables: {}.", dropped);
        }
        return root;
    }

    private JsonNode toJsonNode(Object value) {
        if (value == null) {
            return objectMapper.getNodeFactory().nullNode();
        }
        if (value instanceof JsonNode node) {
            return node;
        }
        if (value instanceof Date date) {
            return objectMapper.getNodeFactory().textNode(date.toInstant().toString());
        }
        if (value instanceof Instant instant) {
            return objectMapper.getNodeFactory().textNode(instant.toString());
        }
        if (value instanceof BigDecimal decimal) {
            // Send BigDecimal as a string to avoid double-rounding in JSON.
            return objectMapper.getNodeFactory().textNode(decimal.toPlainString());
        }
        return objectMapper.valueToTree(value);
    }

    private void applyVariableUpdates(DelegateExecution execution, JsonNode updates) {
        if (updates == null || updates.isNull() || !updates.isObject()) {
            return;
        }
        for (var entry : updates.properties()) {
            var name = entry.getKey();
            var node = entry.getValue();
            if (node == null || node.isNull() || !node.isObject()) {
                continue;
            }
            var type = node.path("type").asText("");
            var raw = node.get("value");
            try {
                applyTypedUpdate(execution, name, type, raw);
            } catch (RuntimeException exception) {
                throw new FlowableException(
                    "Failed to apply variable update '" + name + "' (type '" + type + "'): " + exception.getMessage(),
                    exception);
            }
        }
    }

    private void applyTypedUpdate(DelegateExecution execution, String name, String type, JsonNode raw) {
        switch (type.toLowerCase()) {
            case "string":
                execution.setVariable(name, raw == null || raw.isNull() ? null : raw.asText());
                break;
            case "long":
                execution.setVariable(name, raw == null || raw.isNull() ? null : Long.valueOf(raw.asLong()));
                break;
            case "double":
                execution.setVariable(name, raw == null || raw.isNull() ? null : Double.valueOf(raw.asDouble()));
                break;
            case "bool":
                execution.setVariable(name, raw == null || raw.isNull() ? null : Boolean.valueOf(raw.asBoolean()));
                break;
            case "date":
                if (raw == null || raw.isNull()) {
                    execution.setVariable(name, null);
                } else {
                    execution.setVariable(name, Date.from(Instant.parse(raw.asText())));
                }
                break;
            case "json":
                // The SDK serializes JsonElement directly into `value`, so
                // the node *is* the json variable's content. Flowable stores
                // JsonNode directly when the engine config registers a
                // jackson-aware variable type.
                execution.setVariable(name, raw == null ? null : raw.deepCopy());
                break;
            case "bigdecimal":
                execution.setVariable(name, raw == null || raw.isNull() ? null : new BigDecimal(raw.asText()));
                break;
            case "remove":
                execution.removeVariable(name);
                break;
            default:
                throw new IllegalArgumentException(
                    "Unsupported variable update type '" + type + "' for variable '" + name + "'.");
        }
    }

    // Reads a flowable: attribute off a BaseElement. Returns the trimmed
    // value or null when absent. Flowable's BPMN model exposes unknown-
    // namespace attributes via `getAttributes()` keyed by local name; the
    // matching ExtensionAttribute carries the namespace.
    private static String readFlowableAttribute(BaseElement element, String localName) {
        var attributes = element.getAttributes();
        if (attributes == null) return null;
        var matching = attributes.get(localName);
        if (matching == null) return null;
        for (var attribute : matching) {
            if (FlowableExtensionNamespace.equals(attribute.getNamespace())) {
                var value = attribute.getValue();
                return value == null ? null : value.trim();
            }
        }
        return null;
    }

    private static String safeProcessDefinitionKey(DelegateExecution execution) {
        // Flowable's DelegateExecution doesn't expose the process definition
        // key directly without RepositoryService; pull it from the id, which
        // is "<key>:<version>:<deploymentId>".
        var id = execution.getProcessDefinitionId();
        if (id == null) return "";
        var separator = id.indexOf(':');
        return separator < 0 ? id : id.substring(0, separator);
    }

    private static String normaliseBasePath(URI base) {
        var path = base.getPath();
        if (path == null || path.isEmpty()) return "/";
        return path.endsWith("/") ? path : path + "/";
    }

    private static HttpClient defaultHttpClient(FlowableExecutionEventProperties properties) {
        return HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(Math.max(1, properties.getBehaviorTimeoutSeconds())))
            .build();
    }

    private static ObjectMapper defaultObjectMapper() {
        return new ObjectMapper().registerModule(new JavaTimeModule());
    }
}
