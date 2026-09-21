package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
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
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import org.flowable.bpmn.model.ExtensionAttribute;
import org.flowable.bpmn.model.ScriptTask;
import org.flowable.common.engine.api.FlowableException;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.impl.persistence.entity.ExecutionEntity;
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

    /**
     * #231. A generated ACCUMULATOR: one node per incoming flow, carrying the
     * flow id it stands for and the gateway it came from, on a branch execution
     * under a shared scope.
     */
    private static DelegateExecution accumulatorExecution(
        String allowedRoutes,
        String arrivingFlow,
        DelegateExecution scope,
        Map<String, Object> branchLocals
    ) {
        var element = new ScriptTask();
        element.setId("cg__acc__" + arrivingFlow);
        for (var pair : List.of(
            List.of("autonateAllowedRoutes", allowedRoutes),
            List.of("autonateArrivingFlow", arrivingFlow),
            List.of("autonateExpandedFrom", "cg"))) {
            var attribute = new ExtensionAttribute(pair.get(0));
            attribute.setNamespace("http://flowable.org/bpmn");
            attribute.setValue(pair.get(1));
            element.addAttribute(attribute);
        }
        return newScopedExecution(
            "branch-" + arrivingFlow, false, scope, new LinkedHashMap<>(), branchLocals, element);
    }

    @Test
    void eachArrivalIsRecordedOnTheSharedScopeAndHandedToTheScript() throws Exception {
        var scopeLocals = new LinkedHashMap<String, Object>();
        var scope = newScopedExecution("scope-1", true, null, new LinkedHashMap<>(), scopeLocals);

        var captured = new AtomicReference<String>();
        try (var fixture = HttpFixture.start(
                captured, 200, "{\"result\":\"autonateWait\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'autonateWait';", "route");

            behavior.runInSandbox(accumulatorExecution("f_out", "f1", scope, new LinkedHashMap<>()));
            behavior.runInSandbox(accumulatorExecution("f_out", "f2", scope, new LinkedHashMap<>()));

            // Arrivals accumulate on the scope the branches SHARE -- that is the
            // whole mechanism. On a branch execution each would see only itself.
            assertEquals("f1,f2", scopeLocals.get("autonateArrived__cg"));

            // ...and the second call handed the script both, so an author can
            // decide "have enough arrived" at all.
            var arrived = Mapper.readTree(captured.get()).get("variables").get("autonateArrived");
            assertEquals(2, arrived.size());
            assertEquals("f1", arrived.get(0).asText());
            assertEquals("f2", arrived.get(1).asText());
        }
    }

    @Test
    void oneBranchArrivingTwiceIsCountedOnce() throws Exception {
        // A join waiting for "three of three" must not be satisfied by one branch
        // arriving three times. No assertion about the happy path would see this.
        var scopeLocals = new LinkedHashMap<String, Object>();
        var scope = newScopedExecution("scope-1", true, null, new LinkedHashMap<>(), scopeLocals);

        try (var fixture = HttpFixture.start(
                new AtomicReference<>(), 200, "{\"result\":\"autonateWait\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'autonateWait';", "route");

            behavior.runInSandbox(accumulatorExecution("f_out", "f1", scope, new LinkedHashMap<>()));
            behavior.runInSandbox(accumulatorExecution("f_out", "f1", scope, new LinkedHashMap<>()));

            assertEquals("f1", scopeLocals.get("autonateArrived__cg"));
        }
    }

    @Test
    void aWaitAnswerIsAcceptedOnlyOnAnAccumulatingJoin() throws Exception {
        var scopeLocals = new LinkedHashMap<String, Object>();
        var scope = newScopedExecution("scope-1", true, null, new LinkedHashMap<>(), scopeLocals);

        try (var fixture = HttpFixture.start(
                new AtomicReference<>(), 200, "{\"result\":\"autonateWait\",\"mutations\":{}}")) {
            var behavior = newBehavior(fixture.baseUrl(), "return 'autonateWait';", "route");

            // Allowed here: the join is still waiting, which is not a failure.
            behavior.runInSandbox(accumulatorExecution("f_out", "f1", scope, new LinkedHashMap<>()));
            assertNull(scopeLocals.get("autonateFired__cg"), "waiting must not fire the join");

            // THE COMPLEMENT: the same answer from a SPLIT-only gateway is still
            // the #218 contract breach it always was. Without this row, "accept
            // autonateWait" would quietly accept it everywhere and a routing
            // script's typo would look like a deliberate wait.
            var plain = routedExecution("f_out");
            var failure = assertThrows(
                org.flowable.common.engine.api.FlowableException.class,
                () -> behavior.runInSandbox(plain));
            assertTrue(failure.getMessage().contains("not one of its routes"));
        }
    }

    @Test
    void aJoinThatHasFiredAbsorbsTheNextArrivalWithoutCallingTheScript() throws Exception {
        // #219's measured defect: "after 'Branch two' tasks: ['Enough arrived',
        // 'Enough arrived']" -- two live tokens down one path from one join.
        var scopeLocals = new LinkedHashMap<String, Object>();
        var scope = newScopedExecution("scope-1", true, null, new LinkedHashMap<>(), scopeLocals);

        try (var fixture = HttpFixture.start(
                new AtomicReference<>(), 200, "{\"result\":\"f_out\",\"mutations\":{}}")) {
            // The fixture already counts; `start` resets it.
            var calls = HttpFixture.calls;
            var behavior = newBehavior(fixture.baseUrl(), "return 'f_out';", "route");

            var first = accumulatorExecution("f_out", "f1", scope, new LinkedHashMap<>());
            behavior.runInSandbox(first);
            assertEquals(Boolean.TRUE, scopeLocals.get("autonateFired__cg"), "the first route fires the join");
            assertEquals("f_out", first.getVariable("route"));
            assertEquals(1, calls.get());

            var second = accumulatorExecution("f_out", "f2", scope, new LinkedHashMap<>());
            behavior.runInSandbox(second);

            // Absorbed: routed to the wait branch, and the author's script was
            // NOT consulted -- it already said yes once, and asking again invites
            // a second yes.
            assertEquals("autonateWait", second.getVariable("route"));
            assertEquals(1, calls.get(), "an already-fired join must not call the routing script again");
        }
    }

    private static ExecutorScriptTaskActivityBehavior newBehavior(
        URI baseUrl, String script, String resultVariable
    ) {
        return new ExecutorScriptTaskActivityBehavior(
            "ScriptTask_1", script, "javascript", resultVariable, null, false,
            HttpClient.newHttpClient(), Mapper, propertiesFor(baseUrl));
    }

    /**
     * #231. A fake that can tell a scope from a concurrent branch.
     *
     * <p>The {@link DelegateExecution} proxy above cannot: it is not an
     * {@link ExecutionEntity}, so {@code isScope()} and {@code getParent()} are
     * not reachable and every walk trivially stops where it started. A test
     * written against it would pass whether the scoped write landed on the right
     * execution or the wrong one, which is precisely the claim under test.
     *
     * <p>{@code locals} is separate from {@code variables} so the test can assert
     * WHERE a value landed rather than only that it exists somewhere.
     */
    private static DelegateExecution newScopedExecution(
        String executionId,
        boolean isScope,
        DelegateExecution parent,
        Map<String, Object> variables,
        Map<String, Object> locals
    ) {
        return newScopedExecution(executionId, isScope, parent, variables, locals, null);
    }

    private static DelegateExecution newScopedExecution(
        String executionId,
        boolean isScope,
        DelegateExecution parent,
        Map<String, Object> variables,
        Map<String, Object> locals,
        Object currentFlowElement
    ) {
        return (DelegateExecution) Proxy.newProxyInstance(
            ExecutionEntity.class.getClassLoader(),
            new Class<?>[] { ExecutionEntity.class },
            (proxy, method, args) -> switch (method.getName()) {
                case "getProcessInstanceId" -> "p-1";
                case "getId" -> executionId;
                case "getCurrentActivityId" -> "ScriptTask_1";
                case "getCurrentFlowElement" -> currentFlowElement;
                case "isScope" -> isScope;
                case "getParent" -> parent;
                case "getVariables" -> new HashMap<>(variables);
                case "getVariable" -> variables.get((String) args[0]);
                case "setVariable" -> {
                    variables.put((String) args[0], args[1]);
                    yield null;
                }
                case "getVariableLocal" -> locals.get((String) args[0]);
                case "setVariableLocal" -> {
                    locals.put((String) args[0], args[1]);
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

    @Test
    void aScopedWriteLandsOnTheEnclosingScopeAndNotOnTheBranch() throws Exception {
        // The tree #231 measured: a concurrent branch under a scope execution.
        // Parallel branches of one gateway are SIBLINGS under that scope, so a
        // write that landed on the branch would be private to it and an
        // accumulating join would never accumulate.
        var scopeLocals = new LinkedHashMap<String, Object>();
        var branchLocals = new LinkedHashMap<String, Object>();
        var shared = new LinkedHashMap<String, Object>();

        var scope = newScopedExecution("scope-1", true, null, shared, scopeLocals);
        var branch = newScopedExecution("branch-1", false, scope, shared, branchLocals);

        var captured = new AtomicReference<String>();
        var response = "{\"result\":null,\"mutations\":{},\"localMutations\":{\"arrived\":[\"f1\"]}}";
        try (var fixture = HttpFixture.start(captured, 200, response)) {
            var behavior = newBehavior(fixture.baseUrl(), "variables.setLocal('arrived', ['f1']);", null);

            behavior.runInSandbox(branch);

            // Asserted as its rendered form, not as a List: `toJavaValue` keeps a
            // JSON array as a JsonNode, which is pre-existing behaviour shared
            // with `mutations` and not this change's to alter. WHERE the value
            // landed is the claim under test.
            assertNotNull(scopeLocals.get("arrived"),
                "a scoped write must land on the enclosing scope, which the branches share");
            assertEquals("[\"f1\"]", String.valueOf(scopeLocals.get("arrived")));
            // THE COMPLEMENT, and the half that actually fails a wrong
            // implementation: landing on the branch too would still satisfy the
            // assertion above while making the value invisible to the sibling.
            assertTrue(branchLocals.isEmpty(),
                "a scoped write must NOT land on the branch execution; a sibling branch could not see it");
            assertTrue(shared.isEmpty(),
                "a scoped write must not fall back to the process-wide variables");
        }
    }

    @Test
    void aScopedWriteOnAScopeExecutionStaysThere() throws Exception {
        // The degenerate case: no subprocess, so the execution reached IS a
        // scope. Without this row, "always walk to the parent" passes the test
        // above and writes past the process instance on an ordinary process.
        var locals = new LinkedHashMap<String, Object>();
        var shared = new LinkedHashMap<String, Object>();
        var scope = newScopedExecution("proc-1", true, null, shared, locals);

        var captured = new AtomicReference<String>();
        var response = "{\"result\":null,\"mutations\":{},\"localMutations\":{\"n\":1}}";
        try (var fixture = HttpFixture.start(captured, 200, response)) {
            var behavior = newBehavior(fixture.baseUrl(), "variables.setLocal('n', 1);", null);

            behavior.runInSandbox(scope);

            assertEquals(1, locals.get("n"));
        }
    }

    @Test
    void anOrdinaryMutationIsStillProcessWideAlongsideAScopedOne() throws Exception {
        // The two bags must not be conflated in either direction. A reply
        // carrying both must put each where it belongs, or "scoped" becomes a
        // label rather than a behaviour.
        var scopeLocals = new LinkedHashMap<String, Object>();
        var branchLocals = new LinkedHashMap<String, Object>();
        var shared = new LinkedHashMap<String, Object>();

        var scope = newScopedExecution("scope-1", true, null, shared, scopeLocals);
        var branch = newScopedExecution("branch-1", false, scope, shared, branchLocals);

        var captured = new AtomicReference<String>();
        var response =
            "{\"result\":null,\"mutations\":{\"wide\":true},\"localMutations\":{\"narrow\":true}}";
        try (var fixture = HttpFixture.start(captured, 200, response)) {
            var behavior = newBehavior(
                fixture.baseUrl(), "variables.set('wide', true);", null);

            behavior.runInSandbox(branch);

            assertEquals(Boolean.TRUE, shared.get("wide"));
            assertEquals(Boolean.TRUE, scopeLocals.get("narrow"));
            assertTrue(!shared.containsKey("narrow"), "a scoped write must not become process-wide");
            assertTrue(!scopeLocals.containsKey("wide"), "a process-wide write must not become scoped");
        }
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
