package com.autonate.flowableevents;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Iterator;
import java.util.Map;
import java.util.UUID;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.impl.bpmn.behavior.ScriptTaskActivityBehavior;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Executes BPMN script tasks in the AutoNate executor sandbox instead of inside
 * this JVM (#147, GHSA-82rh-gjhw-rg9r).
 *
 * <p>The engine's own {@link ScriptTaskActivityBehavior} evaluates author code
 * through JSR-223, which on this image means Nashorn — whose Java interop is on
 * by default, so a script task could reach {@code java.lang.System} and through
 * it the whole JVM and every database the process can see. This subclass
 * overrides {@link #execute} and <strong>never</strong> calls
 * {@code super.execute} or {@code executeScript}, so that evaluation path is
 * not reached at all. The vulnerability is closed by removing the surface, not
 * by filtering what author code may say.
 *
 * <p>Instead the script and the execution's variables are POSTed to
 * AutoNate.Web, which runs them in the V8 isolate the pipeline code nodes
 * already use and returns the variables the script wrote.
 *
 * <p><strong>Fail-closed.</strong> Every failure — unconfigured callback,
 * transport error, non-2xx, unparseable body — throws {@link FlowableException}
 * so Flowable's job executor retries through the existing
 * JOB_EXECUTION_FAILURE pipeline. There is deliberately no fallback to
 * in-JVM execution: falling back would reinstate the vulnerability at exactly
 * the moment the system is degraded.
 */
public class ExecutorScriptTaskActivityBehavior extends ScriptTaskActivityBehavior {

    private static final Logger Logger = LoggerFactory.getLogger(ExecutorScriptTaskActivityBehavior.class);

    private static final String CallbackSecretHeader = "X-AutoNate-Internal-Token";
    private static final String CorrelationIdHeader = "X-Correlation-Id";

    private final transient HttpClient httpClient;
    private final transient ObjectMapper objectMapper;
    private final transient FlowableExecutionEventProperties properties;

    public ExecutorScriptTaskActivityBehavior(
        String scriptTaskId,
        String script,
        String language,
        String resultVariable,
        String skipExpression,
        boolean storeScriptVariables,
        HttpClient httpClient,
        ObjectMapper objectMapper,
        FlowableExecutionEventProperties properties
    ) {
        super(scriptTaskId, script, language, resultVariable, skipExpression, storeScriptVariables);
        this.httpClient = httpClient;
        this.objectMapper = objectMapper;
        this.properties = properties;
    }

    @Override
    public void execute(DelegateExecution execution) {
        runInSandbox(execution);
        leave(execution);
    }

    /**
     * Everything {@link #execute} does apart from handing control back to the
     * engine. Separated so the behaviour can be tested without a Flowable
     * command context — {@code leave} needs the engine's agenda, which a unit
     * test has no way to provide, and folding it in would leave the parts worth
     * asserting reachable only through a full engine.
     */
    void runInSandbox(DelegateExecution execution) {
        var activityId = execution.getCurrentActivityId();

        if (script == null || script.isBlank()) {
            throw new FlowableException(
                "Script task '" + activityId + "' has no script body.");
        }

        // The sandbox runs JavaScript and Python (#154); anything else is
        // refused here rather than forwarded. The base image still ships groovy
        // and flowable-groovy-script-static-engine, and sending a Groovy body
        // to a JavaScript isolate would fail with a syntax error that says
        // nothing about the real reason.
        //
        // This is also the criterion that Nashorn and Groovy cannot serve
        // script tasks — they cannot, because this behaviour replaced the
        // engine's script path entirely and an unsupported format stops here.
        if (language != null && !language.isBlank() && !isSupportedFormat(language)) {
            throw new FlowableException(
                "Script task '" + activityId + "' uses scriptFormat '" + language +
                "'. Supported formats are 'javascript' and 'python'; scripts run in the " +
                "AutoNate sandbox, not in a JVM script engine.");
        }

        var callbackBase = properties.getCallbackBaseUrl();
        var sharedSecret = properties.getCallbackSharedSecret();
        if (callbackBase == null) {
            throw new FlowableException(
                "Script-task callback base URL is not configured (autonate.flowable-events.callback-base-url).");
        }
        if (sharedSecret == null || sharedSecret.isBlank()) {
            throw new FlowableException(
                "Script-task callback shared secret is not configured (autonate.flowable-events.callback-shared-secret).");
        }

        var correlationId = UUID.randomUUID().toString();
        var endpointUri = callbackBase.resolve(
            normaliseBasePath(callbackBase) + "api/workflow-script-tasks/execute");

        var response = post(endpointUri, buildRequestBody(execution, correlationId), sharedSecret, correlationId);
        applyResult(execution, response, activityId, correlationId);
    }

    private ObjectNode buildRequestBody(DelegateExecution execution, String correlationId) {
        var body = objectMapper.createObjectNode();
        body.put("processInstanceId", execution.getProcessInstanceId());
        body.put("executionId", execution.getId());
        body.put("nodeId", execution.getCurrentActivityId());
        body.put("code", script);
        // The author's declared format travels with the script; the host maps
        // it to the executor's runner name. Sending it rather than assuming
        // JavaScript is what lets a Python script task work at all.
        body.put("scriptFormat", language == null || language.isBlank() ? "javascript" : language);
        body.put("correlationId", correlationId);
        body.set("variables", snapshotVariables(execution));
        return body;
    }

    /**
     * The execution's variables, filtered to the types that round-trip as JSON.
     * A dropped variable is logged rather than silently omitted, because a
     * script reading it would otherwise see undefined with no explanation.
     */
    private ObjectNode snapshotVariables(DelegateExecution execution) {
        var variables = objectMapper.createObjectNode();
        for (Map.Entry<String, Object> entry : execution.getVariables().entrySet()) {
            var value = entry.getValue();
            if (value != null && !isJsonSafe(value)) {
                Logger.warn(
                    "Script task {} : variable '{}' of type {} is not JSON-safe and was not sent to the sandbox.",
                    execution.getCurrentActivityId(), entry.getKey(), value.getClass().getName());
                continue;
            }
            variables.set(entry.getKey(), objectMapper.valueToTree(value));
        }
        return variables;
    }

    private static boolean isJsonSafe(Object value) {
        return value instanceof String
            || value instanceof Boolean
            || value instanceof Number
            || value instanceof java.util.Date
            || value instanceof java.time.temporal.Temporal
            || value instanceof JsonNode;
    }

    private HttpResponse<String> post(URI uri, ObjectNode body, String secret, String correlationId) {
        try {
            var payload = objectMapper.writeValueAsBytes(body);
            var request = HttpRequest.newBuilder(uri)
                .header("Content-Type", "application/json")
                .header(CallbackSecretHeader, secret)
                .header(CorrelationIdHeader, correlationId)
                .POST(HttpRequest.BodyPublishers.ofByteArray(payload))
                .timeout(Duration.ofSeconds(Math.max(1, properties.getBehaviorTimeoutSeconds())))
                .build();
            return httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (IOException exception) {
            throw new FlowableException(
                "Script-task callback to '" + uri + "' failed: " + exception.getMessage(), exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new FlowableException("Script-task callback to '" + uri + "' was interrupted.", exception);
        }
    }

    private void applyResult(
        DelegateExecution execution,
        HttpResponse<String> response,
        String activityId,
        String correlationId
    ) {
        var status = response.statusCode();
        if (status == 422) {
            // The author's code failed. Distinct from an unreachable executor
            // so the workflow error surface can tell them apart; both fail the
            // activity, but only one is worth retrying.
            throw new FlowableException(
                "Script task '" + activityId + "' failed: " + errorMessage(response.body()) +
                " (correlationId " + correlationId + ").");
        }
        if (status < 200 || status >= 300) {
            throw new FlowableException(
                "Script-task callback for '" + activityId + "' returned HTTP " + status +
                ": " + errorMessage(response.body()) + " (correlationId " + correlationId + ").");
        }

        JsonNode parsed;
        try {
            parsed = objectMapper.readTree(response.body() == null ? "{}" : response.body());
        } catch (IOException exception) {
            throw new FlowableException(
                "Script-task callback for '" + activityId + "' returned a body that wasn't valid JSON " +
                "(correlationId " + correlationId + ").", exception);
        }

        var mutations = parsed.get("mutations");
        if (mutations != null && mutations.isObject()) {
            for (Iterator<Map.Entry<String, JsonNode>> it = mutations.fields(); it.hasNext(); ) {
                var mutation = it.next();
                execution.setVariable(mutation.getKey(), toJavaValue(mutation.getValue()));
            }
        }

        // `resultVariable` is what the studio already writes onto script tasks,
        // so it keeps working unchanged.
        if (resultVariable != null && !resultVariable.isBlank()) {
            var result = parsed.get("result");
            execution.setVariable(resultVariable, result == null ? null : toJavaValue(result));
        }
    }

    private String errorMessage(String body) {
        if (body == null || body.isBlank()) return "(no body)";
        try {
            var node = objectMapper.readTree(body);
            var message = node.get("message");
            return message == null ? body : message.asText();
        } catch (IOException exception) {
            return body;
        }
    }

    /**
     * JSON back into the Java types Flowable stores. Objects and arrays stay as
     * {@link JsonNode}, which Flowable persists through its json variable type —
     * the same shape the studio's global data object already uses.
     */
    private static Object toJavaValue(JsonNode node) {
        if (node == null || node.isNull()) return null;
        if (node.isTextual()) return node.asText();
        if (node.isBoolean()) return node.asBoolean();
        if (node.isIntegralNumber()) {
            var asLong = node.asLong();
            return (asLong >= Integer.MIN_VALUE && asLong <= Integer.MAX_VALUE)
                ? (Object) (int) asLong
                : (Object) asLong;
        }
        if (node.isFloatingPointNumber()) return node.asDouble();
        return node;
    }

    private static boolean isSupportedFormat(String format) {
        return "javascript".equalsIgnoreCase(format)
            || "js".equalsIgnoreCase(format)
            || "python".equalsIgnoreCase(format);
    }

    private static String normaliseBasePath(URI base) {
        var path = base.getPath();
        if (path == null || path.isEmpty()) return "/";
        return path.endsWith("/") ? path : path + "/";
    }
}
