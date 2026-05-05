# Workflow Script-Task Error Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture the root-cause exception message and full stack trace from every Flowable `JOB_EXECUTION_FAILURE` event into `workflow_execution_errors`, surface the message in a hover tooltip on errored nodes in the execution-viewer diagram tab, and add a "Show stack trace" expand affordance to the History tab.

**Architecture:** Java extension extracts cause-chain text on failure events and attaches it to the existing `job.execution.failed` event payload. C# recorder persists the two new fields into a new `error_stack_trace` column (and the existing `error_message` column). The `/api/executions/{id}/diagram` endpoint adds an `errorMessagesByActivityId` map; `/api/executions/{id}/history` adds a per-row `errorStackTrace`. The SPA hover-tooltip mechanism is generalized beyond `bpmn:UserTask` so errored nodes of any type can render an "Error" row, and the History tab gains a small expand toggle.

**Tech Stack:** Java 21 + Flowable 8 (flowable-extension), C# 10 + EF Core (AutoNate.Web), Postgres, React 19 + TypeScript + bpmn-js (AutoNate.Spa). Tests: JUnit 5 (Java), xUnit + `PostgresTestDatabase` integration fixture (C#); SPA changes covered by `npm run type-check` and existing Playwright suite — no SPA unit tests.

**Spec deviation (YAGNI):** The spec proposed an `ActivityErrorSummary(string? Message)` wrapper record on the diagram DTO. The plan uses a plain `IReadOnlyDictionary<string, string>` named `ErrorMessagesByActivityId` (entry only present when a non-empty message exists). One fewer type, identical behavior, easy to extend later if a second field appears.

---

## Task 1: Java `ExceptionDetails` helper (TDD)

**Files:**
- Create: `flowable-extension/src/main/java/com/autonate/flowableevents/ExceptionDetails.java`
- Create: `flowable-extension/src/test/java/com/autonate/flowableevents/ExceptionDetailsTests.java`

- [ ] **Step 1: Write the failing test**

Create `flowable-extension/src/test/java/com/autonate/flowableevents/ExceptionDetailsTests.java`:

```java
package com.autonate.flowableevents;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class ExceptionDetailsTests {

    @Test
    void rootCauseMessage_returnsTopMessage_whenNoCause() {
        var ex = new RuntimeException("boom");
        assertEquals("boom", ExceptionDetails.rootCauseMessage(ex));
    }

    @Test
    void rootCauseMessage_walksCauseChain_returnsDeepest() {
        var deepest = new IllegalStateException("script line 7: x is undefined");
        var middle = new RuntimeException("Problem evaluating script", deepest);
        var top = new RuntimeException("Job execution failed", middle);
        assertEquals("script line 7: x is undefined", ExceptionDetails.rootCauseMessage(top));
    }

    @Test
    void rootCauseMessage_nullThrowable_returnsNull() {
        assertNull(ExceptionDetails.rootCauseMessage(null));
    }

    @Test
    void rootCauseMessage_terminatesOnSelfReferentialCause() {
        // Some libraries set t.cause = t to break naive walkers. The helper
        // must not infinite-loop when getCause() == this.
        var ex = new SelfCausingException("loop");
        assertEquals("loop", ExceptionDetails.rootCauseMessage(ex));
    }

    @Test
    void fullStackTrace_includesClassAndMessage() {
        var ex = new RuntimeException("hello");
        var trace = ExceptionDetails.fullStackTrace(ex);
        assertTrue(trace.contains("RuntimeException"), "trace should contain exception class");
        assertTrue(trace.contains("hello"), "trace should contain message");
        assertTrue(trace.contains("at "), "trace should contain frame markers");
    }

    @Test
    void fullStackTrace_includesCauseChain() {
        var deepest = new IllegalStateException("deep");
        var top = new RuntimeException("top", deepest);
        var trace = ExceptionDetails.fullStackTrace(top);
        assertTrue(trace.contains("Caused by"), "trace should print causes");
        assertTrue(trace.contains("deep"), "trace should include cause message");
    }

    @Test
    void fullStackTrace_nullThrowable_returnsNull() {
        assertNull(ExceptionDetails.fullStackTrace(null));
    }

    private static final class SelfCausingException extends RuntimeException {
        SelfCausingException(String m) {
            super(m);
        }

        @Override
        public synchronized Throwable getCause() {
            return this;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd flowable-extension && mvn -q -Dtest=ExceptionDetailsTests test
```

Expected: compile error / test failure — `ExceptionDetails` does not exist yet.

- [ ] **Step 3: Implement `ExceptionDetails`**

Create `flowable-extension/src/main/java/com/autonate/flowableevents/ExceptionDetails.java`:

```java
package com.autonate.flowableevents;

import java.io.PrintWriter;
import java.io.StringWriter;

// Helpers for extracting human-readable text from a Throwable raised inside
// the Flowable job executor. The values feed the workflow_execution_errors
// table: error_message <- rootCauseMessage; error_stack_trace <- fullStackTrace.
final class ExceptionDetails {

    private ExceptionDetails() {
    }

    static String rootCauseMessage(Throwable t) {
        if (t == null) {
            return null;
        }
        var current = t;
        // Self-referential cause is legal in Java (some libraries set it to
        // break getCause() == null checks). Bail when current.getCause() == current.
        while (current.getCause() != null && current.getCause() != current) {
            current = current.getCause();
        }
        return current.getMessage();
    }

    static String fullStackTrace(Throwable t) {
        if (t == null) {
            return null;
        }
        var sw = new StringWriter();
        t.printStackTrace(new PrintWriter(sw));
        return sw.toString();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd flowable-extension && mvn -q -Dtest=ExceptionDetailsTests test
```

Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add flowable-extension/src/main/java/com/autonate/flowableevents/ExceptionDetails.java \
        flowable-extension/src/test/java/com/autonate/flowableevents/ExceptionDetailsTests.java
git commit -m "Add ExceptionDetails helper for cause-chain message + stack extraction"
```

---

## Task 2: Add `errorMessage` + `errorStackTrace` to the event payload

**Files:**
- Modify: `flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowExecutionEventPayload.java`

- [ ] **Step 1: Extend the record**

Replace the entire body of `WorkflowExecutionEventPayload.java` with:

```java
package com.autonate.flowableevents;

import java.time.Instant;

// Payload shape published on the workflow telemetry stream. Mirrored on the
// C# side by WorkflowExecutionErrorRecorder + the audit/system-issue
// detectors. Any field added here MUST also be read on the consumer side
// (or it'll be silently ignored — the recorder uses optional ReadString).
record WorkflowExecutionEventPayload(
    String eventId,
    String eventType,
    Instant occurredAtUtc,
    String processInstanceId,
    String processDefinitionId,
    String processDefinitionKey,
    String processDefinitionName,
    String activityId,
    String activityName,
    String taskId,
    String taskName,
    String assignee,
    String tenantId,
    String rawFlowableEventType,
    String sourceAppId,
    // Populated only on job.execution.failed events. Root-cause message
    // walked from event.getCause(). Capped at 4 KB by the mapper.
    String errorMessage,
    // Populated only on job.execution.failed events. Full chained
    // stack trace from event.getCause(). Capped at 64 KB by the mapper.
    String errorStackTrace
) {
}
```

- [ ] **Step 2: Verify it builds**

```bash
cd flowable-extension && mvn -q compile
```

Expected: BUILD SUCCESS — but the mapper will not yet pass the new fields, so failures elsewhere may still be visible. (Compile alone won't fail because the record is purely additive at this point; the existing mapper call sites still compile against the new constructor only after Task 3 updates them. Run Task 3 immediately to keep the build green.)

If the build fails because the mapper's `new WorkflowExecutionEventPayload(...)` calls are now missing arguments, that's the expected coupling — proceed to Task 3 in the same commit window.

- [ ] **Step 3: Commit deferred** — combine with Task 3 commit since this leaves the build broken.

---

## Task 3: Mapper threads the throwable through

**Files:**
- Modify: `flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowExecutionEventMapper.java`

- [ ] **Step 1: Drop `final` from the class declaration**

The Task 4 test relies on subclassing the mapper to capture the `cause`
argument. Java forbids extending `final` classes. Change the line:

```java
final class WorkflowExecutionEventMapper {
```

to:

```java
class WorkflowExecutionEventMapper {
```

(Class is still package-private — only test code in the same package can
subclass it. No public-API surface change.)

- [ ] **Step 2: Add caps + new map overload**

In `WorkflowExecutionEventMapper.java`, add these constants near the class top:

```java
// Caps applied at the boundary so a runaway throwable can't blow up the
// event payload or the workflow_execution_errors row. Truncated values get
// a trailing marker so it's obvious in the UI.
private static final int ERROR_MESSAGE_CAP = 4 * 1024;
private static final int ERROR_STACK_TRACE_CAP = 64 * 1024;
private static final String TRUNCATION_MARKER = "\n… [truncated]";
```

Replace the existing `map(...)` method with:

```java
WorkflowExecutionEventEnvelope map(
    String eventType,
    FlowableEngineEvent flowableEvent,
    DelegateExecution execution,
    RepositoryService repositoryService
) {
    return map(eventType, flowableEvent, execution, repositoryService, /*cause*/ null);
}

WorkflowExecutionEventEnvelope map(
    String eventType,
    FlowableEngineEvent flowableEvent,
    DelegateExecution execution,
    RepositoryService repositoryService,
    Throwable cause
) {
    var processDefinitionId = firstNonBlank(flowableEvent.getProcessDefinitionId(), execution != null ? execution.getProcessDefinitionId() : null);
    var definition = definitionMetadataResolver.resolve(repositoryService, processDefinitionId);

    var activity = activityDetails(flowableEvent, execution);
    var task = taskDetails(flowableEvent);
    var processInstanceId = firstNonBlank(flowableEvent.getProcessInstanceId(), execution != null ? execution.getProcessInstanceId() : null);
    var tenantId = firstNonBlank(task.tenantId(), execution != null ? execution.getTenantId() : null);

    var payload = new WorkflowExecutionEventPayload(
        UUID.randomUUID().toString(),
        eventType,
        Instant.now(),
        processInstanceId,
        definition.processDefinitionId(),
        definition.processDefinitionKey(),
        definition.processDefinitionName(),
        activity.activityId(),
        activity.activityName(),
        task.taskId(),
        task.taskName(),
        task.assignee(),
        tenantId,
        flowableEvent.getType().name(),
        properties.getSourceAppId(),
        truncate(ExceptionDetails.rootCauseMessage(cause), ERROR_MESSAGE_CAP),
        truncate(ExceptionDetails.fullStackTrace(cause), ERROR_STACK_TRACE_CAP)
    );

    return new WorkflowExecutionEventEnvelope(topicFor(payload), payload);
}
```

Add `truncate` near the bottom of the class:

```java
private static String truncate(String value, int max) {
    if (value == null || value.length() <= max) {
        return value;
    }
    return value.substring(0, max - TRUNCATION_MARKER.length()) + TRUNCATION_MARKER;
}
```

Update the `mapProcessStarted(...)` constructor call (just below the map overloads): the existing constructor call must be extended with two trailing `null` arguments to match the new payload arity:

```java
var payload = new WorkflowExecutionEventPayload(
    UUID.randomUUID().toString(),
    eventType,
    Instant.now(),
    processInstance.getProcessInstanceId(),
    processInstance.getProcessDefinitionId(),
    processDefinitionKey,
    processDefinitionName,
    processInstance.getActivityId(),
    null,
    null,
    null,
    null,
    processInstance.getTenantId(),
    flowableEvent.getType().name(),
    properties.getSourceAppId(),
    null,   // errorMessage (process-started never has a cause)
    null    // errorStackTrace
);
```

- [ ] **Step 3: Verify the build**

```bash
cd flowable-extension && mvn -q compile
```

Expected: BUILD SUCCESS.

- [ ] **Step 4: Run the existing mapper-related tests**

```bash
cd flowable-extension && mvn -q -Dtest='Workflow*Tests' test
```

Expected: pre-existing tests still pass (they don't assert on the new fields and the no-throwable overload preserves behavior).

- [ ] **Step 5: Commit**

```bash
git add flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowExecutionEventPayload.java \
        flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowExecutionEventMapper.java
git commit -m "Thread exception cause + stack into workflow event payload"
```

---

## Task 4: Failure listener extracts the cause

**Files:**
- Modify: `flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowFailureEventListener.java`
- Modify: `flowable-extension/src/test/java/com/autonate/flowableevents/WorkflowExecutionEventListenerTests.java`

- [ ] **Step 1: Write the failing test**

Append a test to `flowable-extension/src/test/java/com/autonate/flowableevents/WorkflowExecutionEventListenerTests.java`. Add this import at the top of the file:

```java
import org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent;
```

Then add a new test method inside the existing class:

```java
@Test
void failureListenerExtractsCauseFromExceptionEvent() {
    // The failure listener must cast the event to FlowableExceptionEvent and
    // pass the cause Throwable into the mapper, so the payload carries the
    // root-cause message and stack trace into workflow_execution_errors.
    // This test pins the contract: event.getCause() result MUST end up as the
    // mapper's `cause` argument.
    var capturedCause = new java.util.concurrent.atomic.AtomicReference<Throwable>();
    var fakeMapper = new WorkflowExecutionEventMapper(
        new FlowableExecutionEventProperties(), new WorkflowDefinitionMetadataResolver()) {
        // Override the 5-arg overload to capture the cause without needing
        // a real Flowable event. We return null so the listener short-
        // circuits before calling publisher.publish (which is also null).
        @Override
        WorkflowExecutionEventEnvelope map(
            String eventType,
            org.flowable.common.engine.api.delegate.event.FlowableEngineEvent flowableEvent,
            org.flowable.engine.delegate.DelegateExecution execution,
            org.flowable.engine.RepositoryService repositoryService,
            Throwable cause) {
            capturedCause.set(cause);
            return null;
        }
    };
    // Compiles because Task 3 dropped `final` from
    // WorkflowExecutionEventMapper and the class is package-private (test
    // class is in the same package).

    var rootCause = new IllegalStateException("script line 7: x is undefined");
    var listener = new WorkflowFailureEventListener(fakeMapper, /*publisher*/ null, /*repoProvider*/ null);

    // Drive the protected hook directly so we don't need a live Flowable engine.
    var event = new TestExceptionEntityEvent(rootCause);
    listener.jobExecutionFailure(event);

    org.junit.jupiter.api.Assertions.assertSame(rootCause, capturedCause.get(),
        "WorkflowFailureEventListener must thread event.getCause() into the mapper");
}

// Minimal stand-in for FlowableEngineEntityEvent + FlowableExceptionEvent.
// Production code calls only getCause(); the rest of the interface methods
// return null — they would fail the test loudly if the listener started
// touching them, which is what we want.
// If Flowable adds methods to FlowableEngineEntityEvent, add no-op overrides here —
// production code only calls getCause()/getType().
private static final class TestExceptionEntityEvent
    implements org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent,
               org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent {

    private final Throwable cause;

    TestExceptionEntityEvent(Throwable cause) {
        this.cause = cause;
    }

    @Override public Throwable getCause() { return cause; }
    @Override public Object getEntity() { return null; }
    @Override public org.flowable.common.engine.api.delegate.event.FlowableEngineEventType getType() {
        return org.flowable.common.engine.api.delegate.event.FlowableEngineEventType.JOB_EXECUTION_FAILURE;
    }
    @Override public String getProcessDefinitionId() { return null; }
    @Override public String getProcessInstanceId() { return null; }
    @Override public String getExecutionId() { return null; }
    @Override public String getScopeId() { return null; }
    @Override public String getScopeType() { return null; }
    @Override public String getScopeDefinitionId() { return null; }
    @Override public String getSubScopeId() { return null; }
}
```

- [ ] **Step 2: Run the test to verify failure**

```bash
cd flowable-extension && mvn -q -Dtest=WorkflowExecutionEventListenerTests#failureListenerExtractsCauseFromExceptionEvent test
```

Expected: compile failure (`invokeJobExecutionFailureForTest` does not exist) — this drives the next step.

- [ ] **Step 3: Update `WorkflowFailureEventListener`**

Replace the body of `flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowFailureEventListener.java` with:

```java
package com.autonate.flowableevents;

import java.util.Set;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEntityEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEvent;
import org.flowable.common.engine.api.delegate.event.FlowableEngineEventType;
import org.flowable.common.engine.api.delegate.event.FlowableExceptionEvent;
import org.flowable.engine.RepositoryService;
import org.flowable.engine.delegate.DelegateExecution;
import org.flowable.engine.delegate.event.AbstractFlowableEngineEventListener;
import org.springframework.beans.factory.ObjectProvider;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

// JOB_EXECUTION_FAILURE is dispatched inside Flowable's failing work transaction,
// which then rolls back. The main listener filters on TransactionState.COMMITTED
// so it would never see a failure. This second listener intentionally omits the
// transaction filter so failure events fire immediately.
final class WorkflowFailureEventListener extends AbstractFlowableEngineEventListener {

    private static final Logger Logger = LoggerFactory.getLogger(WorkflowFailureEventListener.class);

    private final WorkflowExecutionEventMapper eventMapper;
    private final DaprWorkflowEventPublisher publisher;
    private final ObjectProvider<RepositoryService> repositoryServiceProvider;

    WorkflowFailureEventListener(
        WorkflowExecutionEventMapper eventMapper,
        DaprWorkflowEventPublisher publisher,
        ObjectProvider<RepositoryService> repositoryServiceProvider
    ) {
        super(Set.of(FlowableEngineEventType.JOB_EXECUTION_FAILURE));
        this.eventMapper = eventMapper;
        this.publisher = publisher;
        this.repositoryServiceProvider = repositoryServiceProvider;
    }

    @Override
    public boolean isFailOnException() {
        return false;
    }

    @Override
    protected void jobExecutionFailure(FlowableEngineEntityEvent event) {
        publish("job.execution.failed", event, getExecution(event), causeFrom(event));
    }

    private void publish(String eventType, FlowableEngineEvent event, DelegateExecution execution, Throwable cause) {
        try {
            var envelope = eventMapper.map(eventType, event, execution, repositoryServiceProvider != null
                ? repositoryServiceProvider.getIfAvailable()
                : null, cause);
            if (envelope != null && publisher != null) {
                publisher.publish(envelope);
            }
        } catch (RuntimeException exception) {
            Logger.warn("Failed to publish Flowable workflow event '{}'.", eventType, exception);
        }
    }

    private static Throwable causeFrom(FlowableEngineEvent event) {
        if (event instanceof FlowableExceptionEvent exceptionEvent) {
            return exceptionEvent.getCause();
        }
        return null;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd flowable-extension && mvn -q -Dtest=WorkflowExecutionEventListenerTests test
```

Expected: all tests in the class pass, including the new one.

- [ ] **Step 5: Run the full Java test suite**

```bash
cd flowable-extension && mvn -q test
```

Expected: BUILD SUCCESS, all tests green.

- [ ] **Step 6: Commit**

```bash
git add flowable-extension/src/main/java/com/autonate/flowableevents/WorkflowFailureEventListener.java \
        flowable-extension/src/test/java/com/autonate/flowableevents/WorkflowExecutionEventListenerTests.java
git commit -m "Capture exception cause on JOB_EXECUTION_FAILURE events"
```

---

## Task 5: Postgres schema migration (`error_stack_trace` column)

**Files:**
- Modify: `infra/postgres/init/02-create-autonate-app-schema.sql:959-970`
- Modify: `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs:1588-1602`

- [ ] **Step 1: Add column to docker init script**

In `infra/postgres/init/02-create-autonate-app-schema.sql`, after the existing index creation for the table (around line 970), append:

```sql
-- error_stack_trace was added after the initial table; idempotent so it's
-- safe on both fresh installs (column was just created above) and upgrades.
ALTER TABLE workflow_execution_errors
    ADD COLUMN IF NOT EXISTS error_stack_trace TEXT NULL;
```

- [ ] **Step 2: Mirror in `DatabaseSchemaInitializer`**

In `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs`, replace the `WorkflowExecutionErrorsSql` constant (around lines 1588–1602) with:

```csharp
private const string WorkflowExecutionErrorsSql =
    """
    CREATE TABLE IF NOT EXISTS workflow_execution_errors (
        id UUID PRIMARY KEY,
        process_instance_id TEXT NOT NULL,
        activity_id TEXT NOT NULL,
        activity_name TEXT NULL,
        error_message TEXT NULL,
        raw_flowable_event_type TEXT NULL,
        occurred_at_utc TIMESTAMPTZ NOT NULL
    );

    CREATE INDEX IF NOT EXISTS ix_workflow_execution_errors_process_instance_id
        ON workflow_execution_errors (process_instance_id);

    -- Added after initial table; ALTER ADD COLUMN IF NOT EXISTS is idempotent
    -- on fresh installs and additive on upgrades.
    ALTER TABLE workflow_execution_errors
        ADD COLUMN IF NOT EXISTS error_stack_trace TEXT NULL;
    """;
```

- [ ] **Step 3: Verify the SQL is parseable**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj -c Debug
```

Expected: BUILD SUCCEEDED. (No syntax check on the SQL string yet — the test database in Task 11 will exercise it.)

- [ ] **Step 4: Commit**

```bash
git add infra/postgres/init/02-create-autonate-app-schema.sql \
        src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs
git commit -m "Add error_stack_trace column to workflow_execution_errors"
```

---

## Task 6: EF model + DbContext mapping

**Files:**
- Modify: `src/AutoNate.Web/Persistence/Scaffolded/WorkflowExecutionError.cs`
- Modify: `src/AutoNate.Web/Persistence/AutoNateDbContext.cs:177-183`

- [ ] **Step 1: Add property to the EF model**

Replace `src/AutoNate.Web/Persistence/Scaffolded/WorkflowExecutionError.cs` with:

```csharp
using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowExecutionError
{
    public Guid Id { get; set; }

    public string ProcessInstanceId { get; set; } = null!;

    public string ActivityId { get; set; } = null!;

    public string? ActivityName { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorStackTrace { get; set; }

    public string? RawFlowableEventType { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
```

- [ ] **Step 2: Map the column in `AutoNateDbContext`**

Open `src/AutoNate.Web/Persistence/AutoNateDbContext.cs` and locate the
`WorkflowExecutionError` entity configuration (around line 175). The
existing block uses explicit `HasColumnName(...)` calls. Insert this line
immediately after the existing `ErrorMessage` mapping (line 189):

```csharp
entity.Property(e => e.ErrorStackTrace).HasColumnName("error_stack_trace");
```

- [ ] **Step 3: Build**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj -c Debug
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Web/Persistence/Scaffolded/WorkflowExecutionError.cs \
        src/AutoNate.Web/Persistence/AutoNateDbContext.cs
git commit -m "Map error_stack_trace column on WorkflowExecutionError EF model"
```

---

## Task 7: Recorder populates the new fields

**Files:**
- Modify: `src/AutoNate.Web/Services/Workflow/WorkflowExecutionErrorRecorder.cs:106-115`

- [ ] **Step 1: Update `TryBuildRow`**

In `src/AutoNate.Web/Services/Workflow/WorkflowExecutionErrorRecorder.cs`, replace the `return new WorkflowExecutionError { ... }` block (around lines 106–115) with:

```csharp
return new WorkflowExecutionError
{
    Id = Guid.NewGuid(),
    ProcessInstanceId = processInstanceId,
    ActivityId = activityId,
    ActivityName = ReadString(root, "activityName"),
    ErrorMessage = ReadString(root, "errorMessage"),
    ErrorStackTrace = ReadString(root, "errorStackTrace"),
    RawFlowableEventType = ReadString(root, "rawFlowableEventType"),
    OccurredAtUtc = ReadDateTime(root, "occurredAtUtc") ?? DateTime.UtcNow
};
```

- [ ] **Step 2: Build**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj -c Debug
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit (no test yet — Task 8 adds it)**

```bash
git add src/AutoNate.Web/Services/Workflow/WorkflowExecutionErrorRecorder.cs
git commit -m "Read errorMessage + errorStackTrace into the recorder row"
```

---

## Task 8: Recorder integration test

**Files:**
- Create: `tests/AutoNate.Web.Tests/Workflow/WorkflowExecutionErrorRecorderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AutoNate.Web.Tests/Workflow/WorkflowExecutionErrorRecorderTests.cs`:

```csharp
using System.Reflection;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

[Collection("Postgres")]
public sealed class WorkflowExecutionErrorRecorderTests : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _db;

    public WorkflowExecutionErrorRecorderTests(PostgresTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task HandleAsync_PersistsErrorMessageAndStackTrace_WhenPayloadCarriesBoth()
    {
        var processId = $"proc-{Guid.NewGuid():N}";
        var activityId = "scriptTask_1";
        var payload = $$"""
        {
          "eventType": "job.execution.failed",
          "processInstanceId": "{{processId}}",
          "activityId": "{{activityId}}",
          "activityName": "Eval Script",
          "errorMessage": "ReferenceError: x is not defined",
          "errorStackTrace": "ReferenceError: x is not defined\n  at line 7\n  at engine.run",
          "rawFlowableEventType": "JOB_EXECUTION_FAILURE",
          "occurredAtUtc": "2026-05-05T12:00:00Z"
        }
        """;

        await InvokeRecorderHandleAsync(payload);

        await using var read = await _db.CreateDbContextFactory().CreateDbContextAsync();
        var row = await read.WorkflowExecutionErrors
            .AsNoTracking()
            .Where(e => e.ProcessInstanceId == processId)
            .SingleAsync();

        Assert.Equal(activityId, row.ActivityId);
        Assert.Equal("Eval Script", row.ActivityName);
        Assert.Equal("ReferenceError: x is not defined", row.ErrorMessage);
        Assert.Contains("at line 7", row.ErrorStackTrace);
    }

    [Fact]
    public async Task HandleAsync_PersistsRow_WhenErrorFieldsAreAbsent()
    {
        var processId = $"proc-{Guid.NewGuid():N}";
        var payload = $$"""
        {
          "eventType": "job.execution.failed",
          "processInstanceId": "{{processId}}",
          "activityId": "scriptTask_2",
          "rawFlowableEventType": "JOB_EXECUTION_FAILURE",
          "occurredAtUtc": "2026-05-05T12:01:00Z"
        }
        """;

        await InvokeRecorderHandleAsync(payload);

        await using var read = await _db.CreateDbContextFactory().CreateDbContextAsync();
        var row = await read.WorkflowExecutionErrors
            .AsNoTracking()
            .Where(e => e.ProcessInstanceId == processId)
            .SingleAsync();

        Assert.Null(row.ErrorMessage);
        Assert.Null(row.ErrorStackTrace);
    }

    private async Task InvokeRecorderHandleAsync(string payload)
    {
        var busWatcher = new BusWatcherStreamService(NullLogger<BusWatcherStreamService>.Instance);
        var recorder = new WorkflowExecutionErrorRecorder(
            busWatcher,
            _db.CreateDbContextFactory(),
            NullLogger<WorkflowExecutionErrorRecorder>.Instance);

        // HandleAsync is private; reflection keeps the test laser-focused on
        // the persistence behavior without standing up the full hosted-service
        // subscription.
        var method = typeof(WorkflowExecutionErrorRecorder)
            .GetMethod("HandleAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(WorkflowExecutionErrorRecorder), "HandleAsync");

        var message = new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            BusWatcherStreamService.TopicName,
            "application/json",
            new Dictionary<string, string>(),
            payload);

        var task = (Task)method.Invoke(recorder, new object[] { message })!;
        await task;
    }
}
```

> Verify the namespace + signature of `BusWatcherStreamService` and `BusWatcherMessage` against the current code before running — `WorkflowSignalDispatcherTests.BuildMessage` (line 240) is the canonical reference. If `BusWatcherStreamService`'s constructor differs from `(ILogger)`, mirror what that test uses.

- [ ] **Step 2: Run the test — expect it to pass on a fresh DB**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj \
    --filter "FullyQualifiedName~WorkflowExecutionErrorRecorderTests"
```

Expected: PASS. (The recorder change in Task 7 is what makes this green; if it fails, the recorder isn't reading the JSON fields.)

- [ ] **Step 3: Commit**

```bash
git add tests/AutoNate.Web.Tests/Workflow/WorkflowExecutionErrorRecorderTests.cs
git commit -m "Test: recorder persists errorMessage + errorStackTrace"
```

---

## Task 9: Diagram DTO + endpoint projection

**Files:**
- Modify: `src/AutoNate.Web/Models/FlowableModels.cs:93-116` (add field to `WorkflowExecutionDiagramDetail`)
- Modify: `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs:74-104` (project the map)

- [ ] **Step 1: Add `ErrorMessagesByActivityId` to the DTO**

In `src/AutoNate.Web/Models/FlowableModels.cs`, inside `WorkflowExecutionDiagramDetail`, add this property after `FailedActivityIds`:

```csharp
// Latest non-empty error message per failed activity. Sourced from the
// workflow_execution_errors table. Only populated for activity ids that
// also appear in FailedActivityIds AND have at least one captured message
// (rows from before the capture feature shipped won't surface here).
public IReadOnlyDictionary<string, string> ErrorMessagesByActivityId { get; init; }
    = new Dictionary<string, string>(StringComparer.Ordinal);
```

- [ ] **Step 2: Update the endpoint to populate the dictionary**

In `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`, replace the body of the `/{processInstanceId}/diagram` handler (lines 74–104) with:

```csharp
executions.MapGet("/{processInstanceId}/diagram", async (
    string processInstanceId,
    IFlowableClient flowable,
    IDbContextFactory<AutoNateDbContext> dbFactory,
    IAuditEventPublisher auditPublisher,
    CancellationToken cancellationToken) =>
{
    var detail = await flowable.GetWorkflowExecutionDiagramDetailAsync(processInstanceId, cancellationToken);

    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    // Project only the three columns the handler actually reads — ErrorStackTrace
    // can be tens of KB and is surfaced on the history endpoint, not here.
    var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
        .Where(e => e.ProcessInstanceId == processInstanceId)
        .Select(e => new { e.ActivityId, e.ErrorMessage, e.OccurredAtUtc })
        .ToListAsync(cancellationToken);

    var failedActivityIds = errorRows
        .Select(e => e.ActivityId)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    await auditPublisher.PublishAsync(
        WorkflowAdminEventTopic.TopicName,
        WorkflowAdminEventTypes.ExecutionDiagramViewed,
        WorkflowResourceKinds.Execution,
        resource: new { processInstanceId },
        details: new { failedActivityCount = failedActivityIds.Count },
        cancellationToken);

    if (errorRows.Count == 0)
    {
        return Results.Ok(detail);
    }

    // Latest non-empty message per activity. We take the freshest error
    // because retries can produce successively different messages and the
    // most recent one is what the operator wants to see in the tooltip.
    var errorMessagesByActivityId = errorRows
        .GroupBy(e => e.ActivityId, StringComparer.Ordinal)
        .Select(g => new
        {
            ActivityId = g.Key,
            Message = g.OrderByDescending(e => e.OccurredAtUtc)
                       .Select(e => e.ErrorMessage)
                       .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
        })
        .Where(x => x.Message != null)
        .ToDictionary(x => x.ActivityId, x => x.Message!, StringComparer.Ordinal);

    return Results.Ok(detail with
    {
        FailedActivityIds = failedActivityIds,
        ErrorMessagesByActivityId = errorMessagesByActivityId
    });
}).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");
```

- [ ] **Step 3: Build**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj -c Debug
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Web/Models/FlowableModels.cs \
        src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs
git commit -m "Diagram endpoint returns errorMessagesByActivityId"
```

---

## Task 10: History DTO + endpoint stack trace

**Files:**
- Modify: `src/AutoNate.Web/Models/FlowableModels.cs` (add `ErrorStackTrace` to `WorkflowExecutionHistoryEvent`)
- Modify: `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs:106-238` (history handler)

- [ ] **Step 1: Add `ErrorStackTrace` to `WorkflowExecutionHistoryEvent`**

In `src/AutoNate.Web/Models/FlowableModels.cs`, after the existing `ErrorMessage` property on `WorkflowExecutionHistoryEvent` (around line 170), add:

```csharp
// Latest captured full stack trace from workflow_execution_errors for this
// activityId. Often null on legacy rows; the SPA hides the "Show stack
// trace" toggle when this is null.
public string? ErrorStackTrace { get; init; }
```

- [ ] **Step 2: Project the trace in the history handler**

In `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`, locate the `errorsByActivity` projection (around lines 143–156) and replace it with:

```csharp
var errorsByActivity = errorRows
    .GroupBy(e => e.ActivityId, StringComparer.Ordinal)
    .ToDictionary(
        g => g.Key,
        g =>
        {
            // Pick the latest row that has either a non-empty message or a
            // non-empty stack. Surfacing both fields from the SAME row keeps
            // the operator's mental model honest — message X belongs to
            // stack Y, not "latest message OR latest stack from possibly
            // different retries."
            var latest = g.Reverse()
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)
                                  || !string.IsNullOrWhiteSpace(e.ErrorStackTrace));
            return new
            {
                Count = g.Count(),
                LatestMessage = latest?.ErrorMessage,
                LatestStackTrace = latest?.ErrorStackTrace
            };
        },
        StringComparer.Ordinal);
```

In the same handler, find the `if (errorsByActivity.TryGetValue(e.ActivityId, out var errorAgg))` block (around lines 178–186) and update it:

```csharp
if (errorsByActivity.TryGetValue(e.ActivityId, out var errorAgg))
{
    updated = updated with
    {
        IsErrored = true,
        ErrorCount = errorAgg.Count,
        ErrorMessage = errorAgg.LatestMessage,
        ErrorStackTrace = errorAgg.LatestStackTrace
    };
}
```

Then in the synthesized-row block lower down (around lines 201–230) — the `foreach (var errorRow in errorRows.GroupBy(e => e.ActivityId, StringComparer.Ordinal))` block — replace the inline message lookup with both message and stack and update the `enriched.Add(...)` call:

```csharp
foreach (var errorRow in errorRows.GroupBy(e => e.ActivityId, StringComparer.Ordinal))
{
    if (historyActivityIds.Contains(errorRow.Key))
    {
        continue;
    }

    var first = errorRow.OrderBy(e => e.OccurredAtUtc).First();
    // Pick the latest row that has either a non-empty message or a non-empty
    // stack. Same recency-of-pair rule as the errorsByActivity projection.
    var latest = errorRow.Reverse()
        .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)
                          || !string.IsNullOrWhiteSpace(e.ErrorStackTrace));
    var nameFromRow = errorRow
        .Select(e => e.ActivityName)
        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

    enriched.Add(new WorkflowExecutionHistoryEvent
    {
        ActivityId = errorRow.Key,
        ActivityName = nameFromRow,
        ActivityType = null,
        StartedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(first.OccurredAtUtc, DateTimeKind.Utc)),
        EndedAtUtc = null,
        DurationMs = null,
        Assignee = null,
        TaskId = null,
        DeleteReason = null,
        IsErrored = true,
        ErrorCount = errorRow.Count(),
        ErrorMessage = latest?.ErrorMessage,
        ErrorStackTrace = latest?.ErrorStackTrace
    });
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/AutoNate.Web/AutoNate.Web.csproj -c Debug
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Web/Models/FlowableModels.cs \
        src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs
git commit -m "History endpoint returns errorStackTrace per row"
```

---

## Task 11: Endpoint integration tests

**Files:**
- Create: `tests/AutoNate.Web.Tests/Workflow/ExecutionEndpointsErrorTests.cs`

The suite already provides `AutoNateWebApplicationFactory.CreateAsync()`,
which wires up `PostgresTestDatabase`, dev auto-login as `admin`, and a
`StubFlowableClient` for `IFlowableClient`. Use it directly — no new harness
needed. `SystemIssueEndpointsTests.cs` is the canonical reference.

- [ ] **Step 1: Locate the StubFlowableClient setter API**

```bash
grep -n "DiagramDetailToReturn\|HistoryToReturn\|StubFlowableClient" \
    tests/AutoNate.Web.Tests/StubFlowableClient.cs
```

This shows you the exact field/property names the stub exposes for setting
the diagram detail and history list returned by the endpoint. Use those
names verbatim in Step 2 (substitute names below if they differ — the stub
is the source of truth).

- [ ] **Step 2: Write the tests**

Create `tests/AutoNate.Web.Tests/Workflow/ExecutionEndpointsErrorTests.cs`:

```csharp
using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

[Trait("Category", "Integration")]
public sealed class ExecutionEndpointsErrorTests
{
    [Fact]
    public async Task DiagramEndpoint_ReturnsLatestErrorMessagePerActivity()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        ConfigureFlowableStub(factory, processId);
        await SeedErrorsAsync(factory, processId,
            ("scriptTask_1", "older message", "older trace", "2026-05-05T10:00:00Z"),
            ("scriptTask_1", "newer message", "newer trace", "2026-05-05T11:00:00Z"),
            ("scriptTask_2", "another",       "trace2",      "2026-05-05T10:30:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var detail = await client.GetFromJsonAsync<WorkflowExecutionDiagramDetail>(
            $"/api/executions/{processId}/diagram");

        Assert.NotNull(detail);
        Assert.Equal("newer message", detail!.ErrorMessagesByActivityId["scriptTask_1"]);
        Assert.Equal("another", detail.ErrorMessagesByActivityId["scriptTask_2"]);
    }

    [Fact]
    public async Task HistoryEndpoint_ReturnsErrorStackTrace()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        ConfigureFlowableStub(factory, processId);
        await SeedErrorsAsync(factory, processId,
            ("scriptTask_1", "boom", "Caused by: ReferenceError\n  at line 7",
             "2026-05-05T11:00:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var rows = await client.GetFromJsonAsync<WorkflowExecutionHistoryEvent[]>(
            $"/api/executions/{processId}/history");

        Assert.NotNull(rows);
        var row = Assert.Single(rows!, r => r.ActivityId == "scriptTask_1");
        Assert.True(row.IsErrored);
        Assert.Equal("boom", row.ErrorMessage);
        Assert.Contains("ReferenceError", row.ErrorStackTrace ?? "");
    }

    private static void ConfigureFlowableStub(
        AutoNateWebApplicationFactory factory, string processId)
    {
        // The diagram endpoint calls IFlowableClient.GetWorkflowExecutionDiagramDetailAsync
        // before reading the DB, and the history endpoint calls GetWorkflowExecutionHistoryAsync.
        // Both must return non-null responses or the endpoints short-circuit.
        // Use the StubFlowableClient setters located in Step 1.
        var stub = factory.FlowableStub;

        // Replace these property/method names with whatever the stub exposes
        // (Step 1 grep shows the truth). Both responses are intentionally empty
        // — Postgres rows seeded by SeedErrorsAsync drive the assertions.
        stub.DiagramDetailToReturn = new WorkflowExecutionDiagramDetail
        {
            ExecutionId = processId,
            BpmnXml = "<definitions/>",
            CompletedActivityIds = Array.Empty<string>(),
            CurrentActivityIds = Array.Empty<string>()
        };
        stub.HistoryToReturn = Array.Empty<WorkflowExecutionHistoryEvent>();
    }

    private static async Task SeedErrorsAsync(
        AutoNateWebApplicationFactory factory,
        string processId,
        params (string ActivityId, string Message, string Trace, string OccurredAtUtc)[] rows)
    {
        var dbFactory = factory.Services.GetRequiredService<
            IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var row in rows)
        {
            db.WorkflowExecutionErrors.Add(new WorkflowExecutionError
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = processId,
                ActivityId = row.ActivityId,
                ActivityName = null,
                ErrorMessage = row.Message,
                ErrorStackTrace = row.Trace,
                RawFlowableEventType = "JOB_EXECUTION_FAILURE",
                OccurredAtUtc = DateTime.Parse(row.OccurredAtUtc).ToUniversalTime()
            });
        }
        await db.SaveChangesAsync();
    }
}
```

> If `StubFlowableClient` exposes setters with different names (e.g.
> `SetDiagramDetail(...)` rather than `DiagramDetailToReturn`), use the
> actual names. The Step 1 grep makes this obvious. If the stub today
> doesn't expose any way to override the diagram/history responses, add the
> minimal property setters in `StubFlowableClient.cs` as part of this task —
> a 4-line addition (`public WorkflowExecutionDiagramDetail? DiagramDetailToReturn { get; set; }`
> plus return it from the existing method) — and commit alongside the test.

- [ ] **Step 3: Run the tests**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj \
    --filter "FullyQualifiedName~ExecutionEndpointsErrorTests"
```

Expected: both tests PASS. If a test fails with auth/403, the auto-login
prime call (`/api/auth/me`) wasn't issued — confirm Step 2 has it before
the GET-from-JSON.

- [ ] **Step 4: Commit**

```bash
git add tests/AutoNate.Web.Tests/Workflow/ExecutionEndpointsErrorTests.cs \
        tests/AutoNate.Web.Tests/StubFlowableClient.cs   # only if Step 2 modified it
git commit -m "Test: diagram + history endpoints return error message + stack"
```

---

## Task 12: SPA TypeScript mirror

**Files:**
- Modify: `src/AutoNate.Spa/src/types/flowable.ts:34-76`

- [ ] **Step 1: Add `errorMessagesByActivityId` to `WorkflowExecutionDiagramDetail`**

In `src/AutoNate.Spa/src/types/flowable.ts`, inside the `WorkflowExecutionDiagramDetail` type, add after `failedActivityIds`:

```ts
  // Latest non-empty error message per failed activity. Keyed by the BPMN
  // activity id. Empty when no captured messages exist (legacy rows or
  // pre-feature failures).
  errorMessagesByActivityId: Record<string, string>;
```

- [ ] **Step 2: Add `errorStackTrace` to `WorkflowExecutionHistoryEvent`**

In the same file, inside `WorkflowExecutionHistoryEvent`, add after the existing `errorCount` field:

```ts
  // Latest captured full stack trace for this activity in this process.
  // Null on legacy rows or when capture wasn't available.
  errorStackTrace: string | null;
```

- [ ] **Step 3: Verify type-check**

```bash
cd src/AutoNate.Spa && npm run type-check
```

Expected: 0 errors. Some component code may now refer to undefined fields if the rest of the SPA tasks aren't done yet — that's expected; the only failure that matters here is a syntax/type-mirror error inside `flowable.ts`. If TypeScript flags missing usages elsewhere, those fall under Tasks 13–15 and will be cleaned up as you proceed.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Spa/src/types/flowable.ts
git commit -m "Mirror errorMessagesByActivityId + errorStackTrace in SPA types"
```

---

## Task 13: Generalize `workflow.js` hover beyond UserTask

**Files:**
- Modify: `src/AutoNate.Spa/src/lib/bpmn/workflow.js:1994-2063`

- [ ] **Step 1: Update `enableUserTaskHoverTooltip` to fire on errored nodes too**

In `src/AutoNate.Spa/src/lib/bpmn/workflow.js`, replace the `enableUserTaskHoverTooltip` function (starting line 1994) with:

```js
// options.getInfo(activityId, activityName, bpmn) → { title, rows } | null
//   Where bpmn is { assignee, dueDate } pulled from flowable:* attributes
//   (always present for shape sake; null on non-userTask elements). Returning
//   null suppresses the tooltip for that element.
//
// options.failedActivityIds: readonly string[] — the set of activities the
//   diagram has marked failed. Hover fires on any element whose id is in
//   this set, in addition to all bpmn:UserTask elements. The React side
//   decides what to render via getInfo.
export function enableUserTaskHoverTooltip(viewerHandle, options) {
  if (!viewerHandle?.viewer) {
    return;
  }

  const opts = options || {};
  const getInfo = typeof opts.getInfo === "function" ? opts.getInfo : null;
  if (!getInfo) {
    return;
  }

  // Held in a closure-mutable ref so the React side can update the failed set
  // without rebuilding the viewer. workflow.js exposes setFailedActivityIds
  // below for the hook to call on prop change.
  let failedActivityIds = new Set(Array.isArray(opts.failedActivityIds) ? opts.failedActivityIds : []);

  const eventBus = viewerHandle.viewer.get("eventBus");
  const tooltip = createUserTaskHoverTooltip(viewerHandle.cssScopeAttribute);

  const shouldShowFor = (element) => {
    if (!element || element.waypoints) return false;
    const businessObject = element.businessObject;
    if (!businessObject) return false;
    if (businessObject.$type === "bpmn:UserTask") return true;
    return failedActivityIds.has(element.id);
  };

  const onHover = (event) => {
    const element = event?.element;
    if (!shouldShowFor(element)) {
      return;
    }
    const businessObject = element.businessObject;
    const activityName = typeof businessObject.name === "string" ? businessObject.name : null;
    const bpmn = {
      assignee: readFlowableString(businessObject, "assignee"),
      dueDate: readFlowableString(businessObject, "dueDate")
    };

    const info = getInfo(element.id, activityName, bpmn);
    if (!info) {
      tooltip.hide();
      return;
    }

    const gfx = event?.gfx;
    const rect = gfx?.getBoundingClientRect?.();
    if (!rect) {
      return;
    }
    tooltip.show(info, rect);
  };

  const onOut = (event) => {
    const element = event?.element;
    if (!shouldShowFor(element)) {
      return;
    }
    tooltip.hide();
  };

  const onCanvasClick = () => tooltip.hide();
  const onViewboxChanged = () => tooltip.hide();

  eventBus.on("element.hover", onHover);
  eventBus.on("element.out", onOut);
  eventBus.on("canvas.click", onCanvasClick);
  eventBus.on("canvas.viewbox.changed", onViewboxChanged);

  viewerHandle.setHoverTooltip({
    setFailedActivityIds(ids) {
      failedActivityIds = new Set(Array.isArray(ids) ? ids : []);
    },
    dispose() {
      tooltip.dispose();
      eventBus.off("element.hover", onHover);
      eventBus.off("element.out", onOut);
      eventBus.off("canvas.click", onCanvasClick);
      eventBus.off("canvas.viewbox.changed", onViewboxChanged);
    }
  });
}
```

- [ ] **Step 2: Verify the SPA type-checks**

```bash
cd src/AutoNate.Spa && npm run type-check
```

Expected: 0 errors. Pure JS change — TypeScript only validates the call sites.

- [ ] **Step 3: Commit**

```bash
git add src/AutoNate.Spa/src/lib/bpmn/workflow.js
git commit -m "Generalize hover tooltip to fire on errored nodes, not just UserTask"
```

---

## Task 14: `useBpmnReadonlyViewer` threads failed ids into the tooltip

**Files:**
- Modify: `src/AutoNate.Spa/src/hooks/useBpmnReadonlyViewer.ts:155-165`, plus the highlighting effect at lines 193-210

- [ ] **Step 1: Pass `failedActivityIds` into `enableUserTaskHoverTooltip` and refresh on change**

In `src/AutoNate.Spa/src/hooks/useBpmnReadonlyViewer.ts`, update the `enableUserTaskHoverTooltip` call (around line 158) to pass the current failed list:

```ts
if (enableHoverTooltip) {
  workflow.enableUserTaskHoverTooltip(created, {
    getInfo: (
      activityId: string,
      activityName: string | null,
      bpmn: { assignee: string | null; dueDate: string | null }
    ) => hoverTooltipRef.current?.getInfo(activityId, activityName, bpmn) ?? null,
    failedActivityIds: failedActivityIds ?? []
  });
}
```

Then add a new effect — placed AFTER the existing re-highlight effect (around line 210) — that pushes failed-id changes into the tooltip without rebuilding the viewer:

```ts
// The hover tooltip captures failedActivityIds at viewer-creation time.
// Push subsequent updates through the setFailedActivityIds hook installed
// by workflow.js so a hover after retry/recovery sees the latest set.
useEffect(() => {
  const handle = viewerRef.current as { hoverTooltip?: { setFailedActivityIds?: (ids: readonly string[]) => void } } | null;
  handle?.hoverTooltip?.setFailedActivityIds?.(failedActivityIds ?? []);
}, [failedActivityIds]);
```

> If `viewerRef.current` doesn't expose `hoverTooltip` directly, look at `workflow.js`'s `setHoverTooltip(...)` call site (line 2054 area) to confirm where the handle is stored. The viewer handle uses `setHoverTooltip` to attach an object that has `dispose()` and now `setFailedActivityIds(...)`. Mirror however the handle exposes that object — likely via `viewerRef.current?.hoverTooltip` or a getter. If unclear, grep `setHoverTooltip` and `hoverTooltip` inside `workflow.js` to find the storage convention.

- [ ] **Step 2: Type-check**

```bash
cd src/AutoNate.Spa && npm run type-check
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/AutoNate.Spa/src/hooks/useBpmnReadonlyViewer.ts
git commit -m "Forward failedActivityIds into the hover tooltip on each change"
```

---

## Task 15: `WorkflowExecutions.tsx` builds error-aware tooltip rows

**Files:**
- Modify: `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx:597-614, 936-994`

- [ ] **Step 1: Thread `errorMessagesByActivityId` into the tooltip options**

In `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx`, update the `hoverTooltipOptions` `useMemo` (around lines 597–614):

```tsx
const hoverTooltipOptions = useMemo(
  () => ({
    getInfo: (
      activityId: string,
      activityName: string | null,
      bpmn: { assignee: string | null; dueDate: string | null }
    ): UserTaskHoverInfo | null =>
      buildActivityHoverInfo({
        activityId,
        activityName,
        bpmn,
        tasks,
        history,
        errorMessagesByActivityId: detail?.errorMessagesByActivityId ?? {},
        resolveAssigneeLabel
      })
  }),
  [tasks, history, detail?.errorMessagesByActivityId, resolveAssigneeLabel]
);
```

- [ ] **Step 2: Rename `buildUserTaskHoverInfo` to `buildActivityHoverInfo` and add the errored branch**

In the same file, locate `buildUserTaskHoverInfo` (around line 946) and replace the entire function with:

```tsx
// Builds the data the BPMN hover tooltip shows on a node. Resolution order,
// in priority:
//   1. Errored — the activityId is in errorMessagesByActivityId. Render
//      Status: Errored + Error: <message or "(no message captured)">.
//      Wins over userTask state because an errored node is the most
//      actionable info on the diagram.
//   2. Active runtime tasks (taskDefinitionKey match) — userTask only.
//   3. Most-recent historic record for the activity — userTask only.
//   4. Design-time BPMN attributes — userTask only.
function buildActivityHoverInfo(args: {
  activityId: string;
  activityName: string | null;
  bpmn: { assignee: string | null; dueDate: string | null };
  tasks: readonly FlowableTaskSummary[];
  history: readonly WorkflowExecutionHistoryEvent[];
  errorMessagesByActivityId: Record<string, string>;
  resolveAssigneeLabel: (raw: string | null | undefined) => string;
}): UserTaskHoverInfo {
  const {
    activityId,
    activityName,
    bpmn,
    tasks,
    history,
    errorMessagesByActivityId,
    resolveAssigneeLabel
  } = args;

  const title = activityName ?? activityId;
  const rows: Array<{ label: string; value: string }> = [];

  // 1. Errored branch
  const erroredMessage = Object.prototype.hasOwnProperty.call(errorMessagesByActivityId, activityId)
    ? errorMessagesByActivityId[activityId]
    : null;
  const isErrored = erroredMessage !== null
    || history.some((e) => e.activityId === activityId && e.isErrored === true);
  if (isErrored) {
    rows.push({ label: "Status", value: "Errored" });
    rows.push({
      label: "Error",
      value:
        typeof erroredMessage === "string" && erroredMessage.length > 0
          ? erroredMessage
          : "(no message captured)"
    });
    return { title, rows };
  }

  // 2-4. Existing userTask resolution (unchanged).
  const activeMatches = tasks.filter((t) => t.taskDefinitionKey === activityId);
  if (activeMatches.length > 0) {
    if (activeMatches.length === 1) {
      const t = activeMatches[0];
      rows.push({ label: "Assignee", value: resolveAssigneeLabel(t.assignee) });
      rows.push({ label: "Due", value: formatAbsoluteDueDate(t.dueDate) });
    } else {
      rows.push({
        label: "Assignees",
        value: activeMatches.map((t) => resolveAssigneeLabel(t.assignee)).join(", ")
      });
      const dueValues = activeMatches
        .map((t) => formatAbsoluteDueDate(t.dueDate))
        .filter((v, i, arr) => arr.indexOf(v) === i);
      rows.push({ label: "Due", value: dueValues.join(", ") });
    }
    rows.push({ label: "Status", value: "In progress" });
    return { title, rows };
  }

  const historicMatches = history
    .filter((e) => e.activityId === activityId && e.activityType === "userTask" && e.endedAtUtc)
    .sort((a, b) => (b.endedAtUtc ?? "").localeCompare(a.endedAtUtc ?? ""));
  if (historicMatches.length > 0) {
    const latest = historicMatches[0];
    rows.push({ label: "Assignee", value: resolveAssigneeLabel(latest.assignee) });
    rows.push({ label: "Completed", value: formatAbsoluteDueDate(latest.endedAtUtc) });
    return { title, rows };
  }

  rows.push({ label: "Assignee", value: resolveAssigneeLabel(bpmn.assignee) });
  rows.push({ label: "Due", value: formatBpmnDueDate(bpmn.dueDate) });
  return { title, rows };
}
```

- [ ] **Step 3: Type-check**

```bash
cd src/AutoNate.Spa && npm run type-check
```

Expected: 0 errors. (If the rename leaves stale references to `buildUserTaskHoverInfo`, fix them — there should only be the call site updated in Step 1.)

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx
git commit -m "Show error message on hover over errored execution nodes"
```

---

## Task 16: History tab — `ErrorDetails` expand toggle for stack trace

**Files:**
- Modify: `src/AutoNate.Spa/src/pages/workflow-executions/ExecutionHistory.tsx:87-91`

- [ ] **Step 1: Replace the inline `<code>` block with `<ErrorDetails>`**

In `src/AutoNate.Spa/src/pages/workflow-executions/ExecutionHistory.tsx`, change the import section to add `useState`:

```tsx
import { useState } from "react";
```

Replace the existing message block (lines 87–91):

```tsx
{event.errorMessage && (
  <div className="small text-danger mt-1">
    <code className="text-danger">{event.errorMessage}</code>
  </div>
)}
```

with:

```tsx
{event.errorMessage && (
  <ErrorDetails
    message={event.errorMessage}
    stackTrace={event.errorStackTrace}
  />
)}
```

Then add the `ErrorDetails` component at the bottom of the file (after `formatDuration`):

```tsx
type ErrorDetailsProps = {
  message: string;
  stackTrace: string | null;
};

function ErrorDetails({ message, stackTrace }: ErrorDetailsProps) {
  const [expanded, setExpanded] = useState(false);
  const hasStack = typeof stackTrace === "string" && stackTrace.length > 0;

  return (
    <div className="small text-danger mt-1">
      <code className="text-danger">{message}</code>
      {hasStack && (
        <>
          {" "}
          <button
            type="button"
            className="btn btn-link btn-sm p-0 align-baseline text-danger"
            aria-expanded={expanded}
            onClick={() => setExpanded((v) => !v)}
          >
            {expanded ? "Hide stack trace" : "Show stack trace"}
          </button>
          {expanded && (
            <pre className="workflow-execution-history-stack mt-1 mb-0 small">
              {stackTrace}
            </pre>
          )}
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Add a minimal style for the `<pre>` block**

In `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.css`, append:

```css
.workflow-execution-history-stack {
    background: rgba(220, 53, 69, 0.08);
    color: var(--bs-danger);
    padding: .5rem .75rem;
    border-radius: .25rem;
    white-space: pre-wrap;
    word-break: break-word;
    max-height: 18rem;
    overflow: auto;
}
```

- [ ] **Step 3: Type-check**

```bash
cd src/AutoNate.Spa && npm run type-check
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Spa/src/pages/workflow-executions/ExecutionHistory.tsx \
        src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.css
git commit -m "Add Show/Hide stack trace toggle in execution History tab"
```

---

## Task 17: Final end-to-end verification

**Files:** none new — this task just runs the suite + manual smoke.

- [ ] **Step 1: Java tests**

```bash
cd flowable-extension && mvn -q test
```

Expected: BUILD SUCCESS, all tests pass.

- [ ] **Step 2: C# tests**

```bash
dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj
```

Expected: all tests pass — paying particular attention to `WorkflowExecutionErrorRecorderTests` and `ExecutionEndpointsErrorTests`.

- [ ] **Step 3: SPA type-check + production build**

```bash
cd src/AutoNate.Spa && npm run type-check && npm run build
```

Expected: 0 errors, build succeeds.

- [ ] **Step 4: Manual smoke**

Run the full stack locally per the project's normal dev loop (`make` target or equivalent — check the `Makefile`). Then:

1. Deploy a simple workflow with a script task that throws — e.g. a `<scriptTask scriptFormat="javascript"><script>throw new Error("boom");</script></scriptTask>`.
2. Start an execution.
3. Verify the executions list shows the run as **Errored**.
4. Open the modal, switch to the **Diagram** tab, hover the failed node — confirm the tooltip shows `Status: Errored` and `Error: boom`.
5. Switch to the **History** tab — confirm the failed row shows the message in red and a "Show stack trace" link. Click it — confirm the full stack renders in a scrollable `<pre>`.

If any of these steps don't behave as expected, capture the failing observation and treat the corresponding task as not actually complete — re-open and fix.

- [ ] **Step 5: Update the spec status**

Add a header note to the spec confirming implementation:

```bash
# In docs/superpowers/specs/2026-05-05-workflow-script-error-capture-design.md,
# add at the top, just below the title:
#
#   **Status:** Implemented in <commit-sha-or-PR-url> (2026-05-05).
```

- [ ] **Step 6: Final commit**

```bash
git add docs/superpowers/specs/2026-05-05-workflow-script-error-capture-design.md
git commit -m "Mark workflow script-error capture spec as implemented"
```

---

## Spec coverage check

| Spec section | Task |
|---|---|
| Java capture details (`ExceptionDetails`, mapper threading) | 1, 2, 3 |
| Java listener wiring (`FlowableExceptionEvent` cast) | 4 |
| Postgres schema migration (both files) | 5 |
| EF model + DbContext mapping | 6 |
| Recorder reads new fields | 7 |
| Recorder integration test | 8 |
| Diagram endpoint shape | 9 |
| History endpoint shape | 10 |
| Endpoint integration tests | 11 |
| SPA TypeScript mirror | 12 |
| SPA `workflow.js` hover generalization | 13, 14 |
| SPA `buildActivityHoverInfo` | 15 |
| History tab `ErrorDetails` + CSS | 16 |
| Final type-check + manual smoke | 17 |

All spec sections covered. The spec's "Out of scope" items (backfill, log-tab stack, redaction) remain out of scope.
