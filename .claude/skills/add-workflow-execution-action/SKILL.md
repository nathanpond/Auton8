---
name: add-workflow-execution-action
description: Use when adding a new operation on a running workflow execution (cancel, delete, signal, terminate, override-style admin actions, etc.). Walks the full backend → permissions → UI → tests path so the action shows up gated correctly in the executions page and the help modal.
---

# Adding a workflow execution action

This pattern has hit ~5x in recent history (cancel, delete, signal, override, force-complete). Every iteration touches the same set of files in the same order. Skipping any one of them produces a half-wired feature: a button that 403s, an endpoint nobody can call, or a permission nobody can grant.

## When to invoke this

- Adding a verb to an existing workflow execution (e.g. "suspend", "resume", "retry-failed-jobs").
- Adding a similar verb to workflow tasks — the pattern is the same with `EntityKinds.WorkflowTask` and the relevant Flowable endpoint.

If you're gating a *new entity kind* (not just a new action on an existing one), use `add-permission-gate` first, then come back here.

## Steps in order

### 1. Add the action constant
File: `src/AutoNate.Web/Authorization/Actions.cs`

Add `public const string MyAction = "myaction";` (lowercase, no separators — match the existing style).

### 2. Register the action on the entity type
File: `src/AutoNate.Web/Authorization/EntityTypes/CoreEntityTypes.cs`

Append your action to `WorkflowExecution.actions` (or `WorkflowTask.actions`). The grant evaluator validates that any granted action is in the registered list — if you skip this, grants silently no-op.

### 3. Add the Flowable client method
Files:
- `src/AutoNate.Web/Services/Flowable/IFlowableClient.cs` — declare the method.
- `src/AutoNate.Web/Services/Flowable/FlowableClient.cs` — implement it.

Follow the existing methods (`CancelWorkflowExecutionAsync`, `DeleteWorkflowExecutionAsync`) — they all take `processInstanceId` first, `CancellationToken` last, and use the shared `HttpClient` wired in DI.

### 4. Map the endpoint with the permission filter
File: `src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`

Add to the `executions` group. Pattern:

```csharp
executions.MapPost("/{processInstanceId}/myaction", async (
    string processInstanceId,
    IFlowableClient flowable,
    CancellationToken cancellationToken) =>
{
    await flowable.MyActionAsync(processInstanceId, cancellationToken);
    return Results.NoContent();
}).DisableAntiforgery()
  .RequirePermission(EntityKinds.WorkflowExecution, Actions.MyAction, "processInstanceId");
```

The third arg to `RequirePermission` is the route param name carrying the entity id — must match the `{...}` route token. Mutating endpoints need `.DisableAntiforgery()` because the SPA calls them with `application/json` rather than form posts.

### 5. Wire the SPA hook and UI
Files:
- `src/AutoNate.Spa/src/hooks/useExecutions.ts` — add a `useMyAction` mutation following `useCancelExecution` / `useDeleteExecution`. Invalidate the executions query on success.
- `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx` — add the button. Gate it with `permissions.has(permissionKey({ kind: "workflowexecution", action: "myaction", id: execution.id }))`. Add the action key to the `usePermissionPrefetch` array near the top of the component so the gate is preloaded.

Confirmation modals in this page funnel through the `pendingAction` state — extend its discriminated union (`{ kind: "cancel" | "delete" | "myaction"; ... }`) and the dispatch in the modal's confirm handler.

### 6. Update the help modal
File: `src/AutoNate.Spa/src/pages/admin/GrantsHelpModal.tsx`

Add a row describing what the new action allows. Admins read this when assigning role grants — if you skip it, the action will appear in the role editor with no documentation.

### 7. Tests
- Backend: extend `FlowableEnforcementTests` (or the relevant execution endpoint tests) to assert both the allowed and forbidden cases.
- E2E: extend `WorkflowOverrideTests` (or the closest existing scenario) to drive the new button. Use the existing `SignInAsAdminAsync` helper.

## Common slip-ups

- **Forgetting step 2 (CoreEntityTypes registration).** Grants editor lets admins try to grant the action, but the evaluator rejects it at runtime. Always check `WorkflowExecution.actions` after adding to `Actions.cs`.
- **Using a different route param name than the third `RequirePermission` arg.** The filter throws "route value not found" at runtime, not at startup.
- **Skipping `.DisableAntiforgery()` on mutations.** The SPA sends `application/json`; without this you get 400s from the antiforgery middleware on POST/PUT/DELETE.
