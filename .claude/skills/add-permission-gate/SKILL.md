---
name: add-permission-gate
description: Use when adding a new permission — either a new EntityKind (gating a new resource type) or a new Action that needs to be grantable by admins. Covers the authorization registry, the DI registrations that actually decide allow/deny, the endpoint filter, the admin UI and the enforcement tests, so the permission is genuinely enforceable and not merely advertised.
---

# Adding a permission gate

Read the two facts below before the steps. Both were stated backwards in an earlier
version of this skill, and both change what you do.

## What actually enforces what

**1. The registry does NOT gate anything.** `Authorizer.cs` matches grants by raw
string compare:

```csharp
.Where(pg => (pg.Action == action || pg.Action == Actions.Wildcard) && …)
```

`IEntityType.Actions` is never consulted on the authorization path — its only
consumers are `RegistryEndpoints` (the admin picker), `BuildCapabilityMap` (a display
summary) and `LookupPermissionsSkill`. Grant creation validates principal kind,
non-empty action, selector parse and effect, and nothing else.

Proof: `Actions.RefreshBindings` gates two shipped endpoints
(`ContentDocumentBindingEndpoints.cs:189,268`) on `EntityKinds.Document`, whose
registered actions are `{ View, Comment, Edit }`. It is not registered. It works.

So registering an action makes it **discoverable** — in the Grants help table,
`SelectorBuilder` and `Explain`. Skipping registration leaves a working, invisible
permission. The real hazard is the opposite: **registering an action no endpoint
enforces hands admins a grant that does nothing.** `AnalyticsEntityTypes.cs:68-71`
records exactly that happening.

**2. For a NEW KIND, two DI registrations decide allow/deny — and omitting either
denies everyone.** `Authorizer.cs:131-136`:

```csharp
if (!_instanceAuthorizers.TryGetValue(target.Kind, out var handler))
    return (MaybeDryRun(AuthDecision.Deny($"no instance handler for kind '{target.Kind}'"), action, target), null);
```

Note the blast radius is the **opposite** of the missing-route-id case in step 4. This
one runs *inside* `Authorizer`, so it is reached only after the `Enabled=false` bypass
and the super-admin short-circuit — meaning it denies everyone **except** super-admins,
and does nothing at all when authorization is disabled. And `MaybeDryRun` means that
under `Authorization:DryRun=true` — a real, documented rollout window — a missing
instance authorizer **allows everyone** and merely logs. Do not rely on "it will
obviously fail" to catch this.

This has already shipped five times. `Program.cs:380-381` carries the scar:

> *"These five had selector compilers (above) but no instance handler, so every
> RequirePermission endpoint for them denied everyone but super-admins."*

## When to invoke this

- Adding a new resource type that needs gating (new `EntityKind`).
- Adding a brand-new action verb not already in `Actions.cs`.
- Granting an existing action on an existing kind for the first time.

## Decision: new kind, new action, or both?

- **New action only** → steps 2, 3, 4, 6, 7, 8 — **plus 5c if the kind is a content kind**. `Actions.RefreshBindings` is exactly this case: a new action, no new kind, whose real work was the project-role bundle switch.
- **New kind** → all steps. Steps 5a and 5b are the ones that decide allow/deny.

## Steps

### 1. (New kind only) Define the kind constant
`src/AutoNate.Web/Authorization/EntityKinds.cs` — `public const string MyKind = "mykind";` (lowercase, no separators).

### 2. (New action only) Define the action constant
`src/AutoNate.Web/Authorization/Actions.cs`. Check first — `View`, `Edit`, `Delete`,
`Assign`, `Comment`, `Archive`, `Restore`, `Cancel`, `Override`, `Publish`, `Start`
and others exist. Reuse before inventing. (There is **no** `Actions.Signal`.)

### 3. Register on the entity type — for discoverability
`src/AutoNate.Web/Authorization/EntityTypes/CoreEntityTypes.cs`, **or**
`AnalyticsEntityTypes.cs` — both feed DI (`Program.cs:324-329`). Add an
`EntityTypeDefinition` and append it to `_all`, or append your action to an existing
definition's `actions[]`.

`clrType` may be `typeof(object)` when the model lives in Flowable (see
`WorkflowExecution`). `tags[]` is the field names selectors may reference — and it
must agree with what your selector compiler understands, or a grant that parses will
not match.

Per fact 1: this buys discoverability, not enforcement. Do it anyway — an admin
cannot grant what they cannot see.

### 4. Gate the endpoints

```csharp
.RequirePermission(EntityKinds.MyKind, Actions.MyAction, "routeIdParam")   // instance-level
.RequireKindPermission(EntityKinds.MyKind, Actions.MyAction)               // kind-level
```

⚠️ **The third argument defaults to `"id"` — omitting it does NOT mean kind-level.**
On a route with no `{id}` token the id resolves empty and `RequirePermissionFilter`
returns 403 **immediately, before calling `AuthorizeAsync` at all**. So it is 403 for
**everyone — super-admins included, and even with `Authorization:Enabled=false`**,
because neither bypass lives in the filter; both are inside `Authorizer`, which is
never reached. It publishes an `auth.access.denied` with reason `missing_target_id`
and does not throw.

That last part matters for diagnosis: an engineer who tests this as super-admin still
gets 403, which is the opposite of the usual signal. Use `RequireKindPermission` for
kind-level gating.

There is also a third overload taking `Func<EndpointFilterInvocationContext, string?>`,
which is the only way to gate on an id in the body or a nested route.

Chain `.DisableAntiforgery()` on mutating endpoints, before the permission call.

`AuthorizationGatePresenceTests` also accepts `AuthorizedInHandler(reason)` and
`OpenToAuthenticated(reason)` for routes that genuinely cannot use a filter — real
options, used in `ExecutionEndpoints.cs`.

### 5. (New kind only) The two registrations that decide allow/deny

**5a. `IInstanceAuthorizer`** — `src/AutoNate.Web/Authorization/Evaluator/InstanceAuthorizers.cs`,
registered in `Program.cs:370-386`. Without it every instance-level check denies. See
fact 2.

**5b. `ISelectorCompiler`** — registered `Program.cs:333-367`. Without it
`FilterQueryAsync` cannot compile the grant. `PathOnlySelectorCompiler<T>` is the
minimum and keeps `/<kind>/<id>` and `/<kind>/*` grants working.

⚠️ If you write a real compiler rather than the path-only one, read the wildcard
warning in the `add-projection` skill first: `WildcardValue` must mean *match-any* and
must agree with `InMemorySelectorEvaluator`. The shipped workflow compilers get this
backwards (GHSA-vrw7-qxhw-m9q8) — do not copy them.

**5c. Content kinds are different — and this applies to a new *action* too, not just a new kind.** `RequirePermissionFilter.cs:47-59` diverts any
`ContentKinds.IsContentKind` to `IContentAuthorizer`, and a new action must be added
to the project-role bundle switch at `ContentAuthorizer.cs:738-744` or the role
baseline never grants it.

### 6. Admin UI — what is automatic and what is not

- `Grants.tsx` — the Action field is a plain `TextInput`. Nothing appears automatically and nothing can fail to appear.
- `Roles.tsx` — role CRUD and assignment only. **It has no permission editor.**
- Registry-driven surfaces are `components/SelectorBuilder.tsx`, `GrantsHelpModal.tsx` (its kind→action table is generated from `/api/admin/registry`) and `pages/admin/Explain.tsx`.
- `GrantsHelpModal.tsx` has no per-permission row to add to. Its hand-written prose is an "Examples" section covering a few dangerous permissions — add to it only if yours is one.

### 7. SPA-side gating

There is no `permissions.has()` and no `usePermissionPrefetch`. The real API is
`usePermissionChecks(checks)` from `src/AutoNate.Spa/src/hooks/usePermissionChecks.ts`,
returning a `Map`:

```ts
const checks = useMemo(() => rows.map(r => ({ kind: "mykind", action: "myaction", id: r.id })), [rows]);
const { data: perms } = usePermissionChecks(checks);
const allowed = perms?.get(permissionKey({ kind: "mykind", action: "myaction", id })) ?? false;
```

Exemplars: `WorkflowExecutions.tsx:88-104`, `ManageUsers.tsx:75-76`,
`shell/PermissionRoute.tsx:31-41`. Kind-level checks pass `id: "*"`.

### 8. Tests

- ⚠️ **The test factory defaults authorization OFF.** `AutoNateWebApplicationFactory` sets `Authorization:Enabled=false` and `Enforcement=off`, so an enforcement test needs the three-key override every real one in the repo carries:

  ```csharp
  ["Authorization:Enabled"] = "true",
  ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
  ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
  ```

  Without it your *ungranted* user gets 200. Write only the positive half — very natural — and you ship a green test that proves nothing.
- Enforcement test: the endpoint with a granted user (200/204) and an ungranted one (403).
- ⚠️ **For a new kind, that test must hit an INSTANCE-level route** (`RequirePermission(..., "id")`) with a concrete id. A kind-level route returns from `AuthorizeKindLevelAsync` *before* the instance-handler lookup, so a `/<kind>/*` grant passes with zero instance authorizers registered — the exact failure 5a exists to prevent, invisible to the obvious test.
- **`EntityRegistryTests` hard-asserts the kind count.** Adding to `CoreEntityTypes` fails it with "Expected N, Actual N+1" and no explanation; adding to `AnalyticsEntityTypes` does not. Bump it and add an `Assert.Contains`.
- **`KindGateEnforcementTests` rows are GET-only** — all three theories call `GetAsync`. A row for a `POST /` create route returns 405, which the allow-theory reads as "not Forbidden" and passes. Only add GET routes there.
- **Map the endpoint group in `Program.cs`.** `AuthorizationGatePresenceTests` walks the live `EndpointDataSource`, so an unmapped group passes the invariant by being invisible.
- **`KindGateEnforcementTests` is a hard-coded `TheoryData`, not a sweep** — add a row for a new `RequireKindPermission` route or it is silently uncovered.
- `AuthorizationGatePresenceTests` must pass (invariant 3).
- New kind: a test that an admin can grant it via the grants API **and that the grant takes effect** — that is what catches a missing 5a.

## Common slip-ups

- **New kind, no `IInstanceAuthorizer`** — everything 403s except super-admins, with no startup error. The single most likely failure; it has shipped five times.
- **Action registered, no endpoint** — admins get a grant that does nothing (`archived-24`).
- **Third arg omitted for a kind-level gate** — permanent silent 403. Use `RequireKindPermission`.
- **`tags[]` disagreeing with the selector compiler** — grants parse and never match.
