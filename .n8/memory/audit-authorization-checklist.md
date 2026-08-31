# Authorization audit checklist (AutoNate-specific)

Harvested from `.claude/skills/audit-authorization` on 2026-08-30. Deeper than the security checklist's concern B. Scope: `src/`, `plugins/`, `tests/AutoNate.Web.Tests/Authorization/`.

**A. Layer-1 gate-presence test** — `dotnet test --filter "FullyQualifiedName~AuthorizationGatePresenceTests"` must pass first; a `[Skip]` or comment-out is the top finding regardless of anything else.

**B. `AuthorizedInHandler("…")` / `OpenToAuthenticated("…")` rationale review** — these bypass the static gate. `OpenToAuthenticated` bar: system catalog only, no per-tenant/per-actor data (watch for "catalog" endpoints leaking IDs of records the actor can't see). `AuthorizedInHandler` slips seen historically: rationale says `FilterQueryAsync(X, View)` but handler uses unfiltered `store.ListAsync`; "scoped to actor" but reads `userId` from body/query instead of `http.GetActorId()`; "authorizes both" but checks one; rationale names a filter method that no longer exists.

**C. Comment/code mismatch** — grep `"admin-only"`, `"any signed-in user"`, `"all authenticated users"`, `"public"`, `"reuse the same auth gate as"` near `MapXxx` and confirm the gate matches. Hit twice in the original audit (`AuthorizationExplainEndpoints.cs`, `WorkflowBehaviorEndpoints.cs`).

**D. EntityKind action vocabulary vs. enforcement** — for each `EntityTypeDefinition` in `Authorization/EntityTypes/CoreEntityTypes.cs`: *grantable-but-inert* (action declared, no endpoint enforces → admin grants do nothing) and *inert-but-enforced* (endpoint gates on an action not in `actions[]` → evaluator rejects every grant → admin lockout).

**E. Selector compiler coverage vs. declared `tags[]`** — a kind with tags registered to `PathOnlySelectorCompiler` in `Program.cs` means predicate selectors like `/<kind>/*[<tag>=value]` compile-fail and are *silently skipped* (warning only) → mysterious 403s. This was the workflow-start mismatch bug. Also flag compiler handles tag not in registry.

**F. `DisableAntiforgery` without a permission filter** — `grep -B 5 DisableAntiforgery`; each needs `RequirePermission` / `RequireKindPermission` / `AuthorizedInHandler` / `OpenToAuthenticated` (or `AllowAnonymous` with extreme prejudice).

**G. `AllowAnonymous` justification** — pre-auth (login/register/reset) or shared-secret webhook (e.g. `/api/workflow-behaviors/{key}/execute` + `SharedSecretEndpointFilter`); anything else is questioned.

**H. Per-(kind, action) enforcement tests** — `tests/AutoNate.Web.Tests/Authorization/*EnforcementTests.cs`: at least no-grant→403; positive control with wildcard grant for instance gates; included+excluded row paths for inline filters. Read test bodies, not filenames.

**I. Inline-authorized handler correctness** — actor from `http.GetActorId()` only; the queryable filtered is the queryable returned; `*ForUserAsync` actually scopes by user (read the SQL); both allow/deny branches handled with the intended status.

**J. `RequirePermission(kind, action, "routeName")` token mismatch** — third arg must match a `{…}` token in the route (default `"id"`; a route using `{recordId:guid}` hands the authorizer null → silent deny).

Severity: High = missing gate on state change, dead-tag selector, gate authorizing the wrong thing. Medium = comment/code drift, missing enforcement test for an active gate, drifted `AuthorizedInHandler` rationale. Low = stale-but-accurate rationale, missing positive control.
