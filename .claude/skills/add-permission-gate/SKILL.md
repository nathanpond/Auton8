---
name: add-permission-gate
description: Use when adding a new permission — either a new EntityKind (gating a new resource type) or a new Action that needs to be grantable by admins. Covers the backend authorization registry, endpoint filter, admin UI, and enforcement tests so the permission is actually grantable and enforceable end-to-end.
---

# Adding a permission gate

The authorization layer has three coupled surfaces: the **registry** (kinds + actions + per-kind action vocabulary), the **endpoint filter** (`.RequirePermission()`), and the **admin UI** (where humans actually grant the thing). All three must move together or the permission is unusable.

If you're only adding a new *action* to an existing kind, prefer the `add-workflow-execution-action` skill — it walks the same path with workflow-execution-specific details.

## When to invoke this

- Adding a new resource type that needs gating (new `EntityKind`).
- Adding a brand-new action verb that doesn't already exist in `Actions.cs`.
- Granting an existing action on an existing kind for the first time (just registry + UI; no new constants).

## Decision: new kind, new action, or both?

- New kind → step 1 + 3 + 4 + 5 + 6 + 7.
- New action only → step 2 + 3 (extend the existing kind's `actions[]`) + 4 + 5 + 6 + 7.
- Both → all steps.

## Steps

### 1. (New kind only) Define the entity kind constant
File: `src/AutoNate.Web/Authorization/EntityKinds.cs`

Add `public const string MyKind = "mykind";` (lowercase, no separators).

### 2. (New action only) Define the action constant
File: `src/AutoNate.Web/Authorization/Actions.cs`

Add the const. Check first — many useful verbs (`View`, `Edit`, `Delete`, `Assign`, `Comment`, `Archive`, `Restore`, `Cancel`, `Signal`, `Override`, `Publish`, `Start`) already exist. Reuse before inventing.

### 3. Register on the entity type
File: `src/AutoNate.Web/Authorization/EntityTypes/CoreEntityTypes.cs`

- New kind: add an `EntityTypeDefinition` static property and append it to the array inside `_all`. Fill in `kind`, `clrType` (use `typeof(object)` if the model lives in Flowable or hasn't been built yet — see `WorkflowExecution`), `idClrType`, `actions[]`, and `tags[]` (the field names selectors are allowed to reference).
- New action on existing kind: append to the existing definition's `actions[]` array.

The grant evaluator rejects any granted `(kind, action)` pair where the action isn't in `actions[]` — this step is what makes the permission *grantable*.

### 4. Gate the endpoints
Pattern (from `Endpoints/ExecutionEndpoints.cs`):

```csharp
.RequirePermission(EntityKinds.MyKind, Actions.MyAction, "routeIdParam");
```

Third arg is the name of the route token carrying the entity id. Omit it for endpoints that gate at the kind level (no per-instance check) — but note most existing usages pass an id. For mutating endpoints, also chain `.DisableAntiforgery()`.

### 5. Update the admin UI
Files:
- `src/AutoNate.Spa/src/pages/admin/Grants.tsx` and `Roles.tsx` — these read the entity type registry from a backend endpoint, so new kinds/actions show up automatically *if* steps 1–3 are done. Verify by loading the page; if the new action doesn't appear in the role editor, you missed step 3.
- `src/AutoNate.Spa/src/pages/admin/GrantsHelpModal.tsx` — add a human-readable description of what the new permission allows. This is the *only* place an admin learns what a permission means.

### 6. Update SPA-side gating where relevant
If there's a UI element guarded by the permission (button, page section, route), use `permissions.has(permissionKey({ kind, action, id }))` and add the key to the page's `usePermissionPrefetch` list so it's loaded before render.

### 7. Tests
- Add an enforcement test (look at `FlowableEnforcementTests` or the closest endpoint test file) that hits the endpoint with a user who has the grant (200/204) and one who doesn't (403).
- If the kind is new, add a test that an admin can grant the permission via the grants API and that the grant takes effect.

## Common slip-ups

- **Granted but not registered.** Admin grants `(mykind, myaction)`, evaluator silently denies because `myaction` isn't in `MyKind.actions[]`. Always do step 3.
- **Endpoint not filtered.** Adding the action to the registry doesn't enforce anything — endpoints must explicitly chain `.RequirePermission(...)`.
- **Help modal stale.** The Grants UI will display new actions automatically; the help text won't. Update `GrantsHelpModal.tsx` whenever you add a permission an admin will see.
- **Route param mismatch.** The third `RequirePermission` arg must match a route `{token}` exactly, or the filter throws at request time, not startup.
