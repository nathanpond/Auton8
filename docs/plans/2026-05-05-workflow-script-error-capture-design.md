# Workflow Script Task Error Capture & Display

**Status:** Implemented on 2026-05-05. The implementation plan file (formerly under `docs/superpowers/plans/`) was removed along with the superpowers workflow.

## Goal

When a workflow execution errors at a script task (or any other activity that
raises a `JOB_EXECUTION_FAILURE`), capture the error message and full stack
trace into `workflow_execution_errors`, and display the root-cause message in a
tooltip when the operator hovers over the errored node in the execution
viewer's diagram tab. The history tab also gains an expand affordance to show
the full stack trace on demand.

The infrastructure for marking nodes as errored already exists end-to-end —
the row is written, the node is rendered red, and the History tab reserves an
`errorMessage` column. The Java listener simply never extracts the exception
text and the diagram tab never asks for it.

## Scope

- **All activity failures**, not just script tasks. The existing
  `WorkflowFailureEventListener` already fires on every
  `JOB_EXECUTION_FAILURE`; restricting capture to script-task activities would
  add filtering code without expanding usefulness. Service tasks, async
  user-task entries, and any other failing job all benefit from the same
  message.
- **Both root-cause message and full chained stack trace** are persisted.
  Tooltip shows only the root-cause message; History tab gets a "Show stack
  trace" expand control. Execution log tab is unchanged.
- **No backfill** of existing `workflow_execution_errors` rows. Pre-feature
  rows remain with `error_message = NULL` and `error_stack_trace = NULL`.

## Architecture

| Layer | File(s) | Change |
|---|---|---|
| Flowable extension (Java) | `WorkflowExecutionEventPayload.java`, `WorkflowExecutionEventMapper.java`, `WorkflowFailureEventListener.java` | Extract root cause + stack from `FlowableExceptionEvent.getCause()`, add two new payload fields. |
| Postgres schema | `infra/postgres/init/02-create-autonate-app-schema.sql`, `src/AutoNate.Web/Persistence/DatabaseSchemaInitializer.cs` | Add `error_stack_trace TEXT NULL`. Idempotent `ALTER TABLE … ADD COLUMN IF NOT EXISTS …` follows the create block. |
| EF model | `src/AutoNate.Web/Persistence/Scaffolded/WorkflowExecutionError.cs`, `AutoNateDbContext.cs` | Map the new column. |
| Recorder | `src/AutoNate.Web/Services/Workflow/WorkflowExecutionErrorRecorder.cs` | Read both new fields off the JSON payload, truncate, persist. |
| Diagram endpoint | `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`, `src/AutoNate.Web/Models/FlowableModels.cs` (`WorkflowExecutionDiagramDetail` record) | Return `errorsByActivityId` map alongside `failedActivityIds`. |
| History endpoint | `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`, `src/AutoNate.Web/Models/FlowableModels.cs` (`WorkflowExecutionHistoryEvent` record) | Add `errorStackTrace` to per-activity history DTO. |
| SPA types | `src/AutoNate.Spa/src/types/flowable.ts` | Mirror DTO additions. |
| SPA — diagram tab | `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx`, `src/AutoNate.Spa/src/lib/bpmn/workflow.js`, `src/AutoNate.Spa/src/hooks/useBpmnReadonlyViewer.ts` | Generalize hover tooltip beyond userTask; render error branch when activity is in `errorsByActivityId`. |
| SPA — history tab | `src/AutoNate.Spa/src/pages/workflow-executions/ExecutionHistory.tsx` | Inline `useState` expand toggle for stack trace. |

## Java capture details

`JOB_EXECUTION_FAILURE` events implement `FlowableExceptionEvent`, which
exposes `getCause(): Throwable`. Add a small helper (new file
`flowable-extension/.../ExceptionDetails.java`) with two static methods:

```java
public static String rootCauseMessage(Throwable t) {
    if (t == null) return null;
    Throwable cur = t;
    // Guard against a self-referential cause loop (cur.getCause() == cur is
    // legal in the Java spec; some libraries set it to break getCause() == null
    // checks).
    while (cur.getCause() != null && cur.getCause() != cur) {
        cur = cur.getCause();
    }
    return cur.getMessage();
}

public static String fullStackTrace(Throwable t) {
    if (t == null) return null;
    var sw = new StringWriter();
    t.printStackTrace(new PrintWriter(sw));
    return sw.toString();
}
```

Caps applied at the Java boundary so the JSON payload stays bounded:
- root-cause message: 4 KB
- stack trace: 64 KB
Truncated values get a trailing `… [truncated]` marker so it's obvious in the
UI.

`WorkflowFailureEventListener.jobExecutionFailure(...)` casts the event to
`FlowableExceptionEvent` (it's both an entity event and an exception event)
and threads the `Throwable` into the mapper. The mapper grows an overload
that takes the optional throwable and writes the two new payload fields;
non-failure code paths pass `null`.

`WorkflowExecutionEventPayload` becomes:

```java
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
    String errorMessage,        // new — root-cause message, may be null
    String errorStackTrace      // new — full chained stack, may be null
) { }
```

## Schema migration

Both schema sources are updated. The runtime initializer
(`DatabaseSchemaInitializer.WorkflowExecutionErrorsSql`) and the docker init
script (`02-create-autonate-app-schema.sql`) get a parallel `ALTER TABLE`
after the existing `CREATE TABLE IF NOT EXISTS …` block:

```sql
ALTER TABLE workflow_execution_errors
    ADD COLUMN IF NOT EXISTS error_stack_trace TEXT NULL;
```

This is idempotent for fresh installs (the column was just created with the
table and `ADD COLUMN IF NOT EXISTS` is a no-op) and additive for upgrades.

## Recorder

`WorkflowExecutionErrorRecorder.TryBuildRow(...)` currently sets
`ErrorMessage = null`. Change it to read `errorMessage` and `errorStackTrace`
off the payload via the existing `ReadString` helper. No new caps on the C#
side — the Java side already truncated. The hot-path is unchanged: still one
INSERT per `job.execution.failed` event, with the two strings now populated.

## Diagram endpoint

`WorkflowExecutionDiagramDetail` (mirror of `WorkflowExecutionDiagramDetail`
TypeScript record) gains:

```csharp
public sealed record ActivityErrorSummary(string? Message);

// on WorkflowExecutionDiagramDetail
public IReadOnlyDictionary<string, ActivityErrorSummary> ErrorsByActivityId { get; init; }
    = new Dictionary<string, ActivityErrorSummary>(StringComparer.Ordinal);
```

The endpoint groups `WorkflowExecutionErrors` by `ActivityId`, picks the
latest non-empty `ErrorMessage`, and projects:

```csharp
var errorsByActivityId = errorRows
    .GroupBy(e => e.ActivityId, StringComparer.Ordinal)
    .ToDictionary(
        g => g.Key,
        g => new ActivityErrorSummary(
            Message: g.OrderByDescending(e => e.OccurredAtUtc)
                      .Select(e => e.ErrorMessage)
                      .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))),
        StringComparer.Ordinal);
```

Stack trace is intentionally **not** in this DTO — the tooltip never shows it.

## History endpoint

`WorkflowExecutionHistoryEvent` (C# record + TS mirror) gains a nullable
`ErrorStackTrace` field. The existing aggregation in `ExecutionEndpoints.cs`
that picks the latest non-empty `ErrorMessage` per activity gets a parallel
selector for the latest non-empty `ErrorStackTrace`. The synthesized rows
(failed activities Flowable rolled out of the historic-instance table) also
carry the stack.

## SPA — diagram tab hover

Today, `workflow.js`'s `enableUserTaskHoverTooltip` (line 1994) only fires
when the hovered element is `bpmn:UserTask`. Generalize the function in place
(do **not** rename — it has one call site in `useBpmnReadonlyViewer.ts` and a
churning rename adds zero value):

1. Accept an additional `failedActivityIds: readonly string[]` option, which
   `useBpmnReadonlyViewer` already holds.
2. Fire on any element whose business-object id is present in
   `failedActivityIds`, OR whose BPMN type is `userTask`. The decision lives
   in workflow.js and is purely a "should I bother calling getInfo?" gate.
3. The existing `getInfo(activityId, activityName, bpmn)` callback remains
   the single decision point; React decides what rows to render.

The React-side `buildUserTaskHoverInfo` in `WorkflowExecutions.tsx` is
renamed `buildActivityHoverInfo` (single call site, low-cost rename) and
gains a precedence rule:

- If `errorsByActivityId[activityId]` is set → return an "errored" tooltip
  with rows: `Status: Errored`, `Error: <message or "(no message captured)">`.
  For activities that are also userTasks, append the assignee/status rows
  beneath.
- Otherwise, the existing userTask resolution chain runs unchanged.

The `errorsByActivityId` map is sourced from the diagram-detail query and
threaded through `hoverTooltipOptions` deps in `WorkflowExecutions.tsx`.

## SPA — history tab expand

`ExecutionHistory.tsx` line 87-91 currently renders the message in a
`<code>` block with no stack-trace affordance. Replace with a small inline
expand control:

```tsx
{event.errorMessage && (
  <ErrorDetails
    message={event.errorMessage}
    stackTrace={event.errorStackTrace}
  />
)}
```

`ErrorDetails` is a 25-line local component (same file) that uses
`useState<boolean>(false)` to toggle a `<pre>` block when `stackTrace` is
non-null. Always-collapsed when `stackTrace` is null (just renders the
message in `<code>`, identical to today).

## Out of scope

- Backfilling existing rows.
- Surfacing the stack trace in the Execution Log tab's `error` entries —
  `WorkflowExecutionLogError` keeps its current shape; the History tab is
  the canonical place for the full diagnostic view.
- Truncation or redaction of script-source paths inside stack traces. The
  4 KB / 64 KB caps are the only sanitization.
- Restructuring the "errored" detection to read live from Flowable's
  historic-incidents table instead of `workflow_execution_errors`.

## Testing

**Java (`flowable-extension/.../ExceptionDetailsTests.java`)**
- Plain exception → message + single-frame stack.
- Nested cause chain → root cause message returned; stack contains all frames.
- Self-referential cause (`cur.getCause() == cur`) → terminates without
  infinite loop.
- Null input → null output, no NPE.

**C# (`tests/AutoNate.Web.Tests/Workflow/WorkflowExecutionErrorRecorderTests.cs`,
new file or extension to existing)**
- Payload with `errorMessage` + `errorStackTrace` round-trips into both
  columns.
- Payload with neither field still produces a valid row (both columns null).

**C# endpoints (`tests/AutoNate.Web.Tests/Workflow/ExecutionEndpointsTests.cs`,
new file or extension)**
- `/api/executions/{id}/diagram` returns `errorsByActivityId` keyed by
  activity, with the latest non-empty message.
- `/api/executions/{id}/history` returns `errorStackTrace` on errored rows.

**SPA**
The SPA has no Vitest/Jest setup (no test runner in `package.json`), so SPA
changes are verified by:
- `npm run type-check` to confirm the type-mirror updates compile against
  the rest of the SPA.
- Manual smoke: trigger a script-task failure in dev (the existing
  `__tests__`-style harness doesn't apply here), hover the errored node,
  confirm the tooltip renders with the root-cause message; open the History
  tab and confirm the "Show stack trace" toggle reveals the trace.
- The existing Playwright suite in `tests/AutoNate.E2E.Tests/` is
  unchanged — those tests assert the executions page renders, not specific
  tooltip content.

If we later introduce a SPA unit-test runner, `buildActivityHoverInfo` and
`ErrorDetails` are good first targets.

## Implementation order

1. Java payload + listener + helper + Java tests.
2. Postgres migration (both files) + EF model.
3. Recorder + recorder test.
4. Diagram endpoint DTO + projection + endpoint test.
5. History endpoint DTO + projection + endpoint test.
6. SPA types mirror.
7. SPA workflow.js hover generalization.
8. SPA `buildActivityHoverInfo` rewrite + tooltip wiring + tests.
9. SPA `ErrorDetails` history component + test.

Each step is mergeable on its own; steps 1-5 are backend-only (no UI
regression risk) and steps 7-9 are frontend-only.
