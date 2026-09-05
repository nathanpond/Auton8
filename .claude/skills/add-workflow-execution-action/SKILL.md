---
name: add-workflow-execution-action
description: Use when adding a new operation on a running workflow execution (cancel, delete, signal, terminate, override-style admin actions, etc.). Walks the full backend → permissions → UI → tests path so the action shows up gated correctly in the executions page and the help modal.
---

# Adding a workflow execution action

This pattern recurs — `WorkflowExecution.actions` is currently `{ View, Cancel, Delete, Override, MoveState, DeleteAll }`. (Note there is no signal action or endpoint on executions, and force-complete is gated by `Override` rather than an action of its own.) Every iteration touches the same set of files in the same order. Skipping any one of them produces a half-wired feature: a button that 403s, an endpoint nobody can call, or a permission nobody can grant.

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

Append your action to `WorkflowExecution.actions` (or `WorkflowTask.actions`).

⚠️ **This does not enforce anything.** `Authorizer.cs` matches grants by raw string
compare (`pg.Action == action || pg.Action == Actions.Wildcard`) and never consults
the registry. `Actions.RefreshBindings` gates two shipped endpoints on
`EntityKinds.Document` without being registered on it, and works. Registering makes
the action **discoverable** in the Grants help table, `SelectorBuilder` and `Explain`.
The real hazard is the reverse: an action registered with no endpoint behind it gives
admins a grant that does nothing (`AnalyticsEntityTypes.cs:68-71` records that
happening). See `add-permission-gate` for the full picture.

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

**Kind-level (bulk) variant.** If the action has no per-instance id (e.g. "delete-all", "cancel-all"), use `RequireKindPermission(kind, action)` instead — it routes through `RequireKindPermissionFilter`, which calls the authorizer with `EntityRef(kind, "*")`. The endpoint route then has no `{processInstanceId}` token. Match this on the SPA by checking the permission with `id: "*"`.

### 5. Wire the SPA hook and UI
Files:
- `src/AutoNate.Spa/src/hooks/useExecutions.ts` — add a `useMyAction` mutation following `useCancelExecution` / `useDeleteExecution`. Invalidate `EXECUTIONS_QUERY_KEY` on success.
- `src/AutoNate.Spa/src/pages/workflow-executions/WorkflowExecutions.tsx` — add the button. Permission gating uses the batched `usePermissionChecks` hook from `@/hooks/usePermissionChecks`:
  - **Per-row actions:** add the new `{ kind, action, id }` tuple to the existing `rowActionChecks` `useMemo` array so the lookup is batched with the other row buttons. Read with `rowActionPermissions?.get(permissionKey({ kind: "workflowexecution", action: "myaction", id: execution.id })) ?? false`.
  - **Kind-level actions:** add a separate `useMemo` returning a single-element array with `id: "*"`, pass it to `usePermissionChecks`, and read the result with `permissionKey` the same way. Mirror whatever id the backend filter uses (`RequireKindPermissionFilter` uses `"*"`).

Confirmation modals in this page funnel through the `pendingAction` state — extend its discriminated union (`{ kind: "cancel" | "delete" | "myaction"; ... }`, with kind-level actions usually being a no-payload variant like `{ kind: "delete-all" }`) and the dispatch in the modal's confirm handler. Don't forget the `pendingActionInFlight` boolean — add an `||` clause for your new mutation's `isPending`.

### 6. Update the help modal
File: `src/AutoNate.Spa/src/pages/admin/GrantsHelpModal.tsx`

There is no per-action row to add. The kind→actions table in `GrantsHelpModal.tsx` is
generated from `/api/admin/registry`, so step 2 already documents the action there.
The hand-written material is prose under `<Title order={6}>` example blocks, which
already cover `cancel` / `delete` / `deleteall` / `movestate` / `override` — add to it
only if your action is dangerous enough to warrant an example.

(There is also no role editor: `Roles.tsx` is role CRUD and assignment only.)

### 7. Tests
- Backend: extend `FlowableEnforcementTests` (or the relevant execution endpoint tests) to assert both the allowed and forbidden cases.
- E2E: extend `WorkflowOverrideTests` (or the closest existing scenario) to drive the new button. Use the existing `SignInAsAdminAsync` helper.

## Common slip-ups

- **Forgetting step 2 (CoreEntityTypes registration).** Grants editor lets admins try to grant the action, but the evaluator rejects it at runtime. Always check `WorkflowExecution.actions` after adding to `Actions.cs`.
- **Using a different route param name than the third `RequirePermission` arg.** The filter does **not** throw. `RequirePermissionFilter.cs:37-44` publishes an `auth.access.denied` event with reason `missing_target_id` and returns a silent **403 to everyone except super-admins**, permanently. Looking for an exception will send you the wrong way. Note the arg defaults to `"id"`, so omitting it on a route with no `{id}` token produces the same silent 403 — kind-level gating is `RequireKindPermission`, not omission.
- **Skipping `.DisableAntiforgery()` on mutations.** The SPA sends `application/json`; without this you get 400s from the antiforgery middleware on POST/PUT/DELETE.
