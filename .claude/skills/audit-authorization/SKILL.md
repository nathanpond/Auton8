---
name: audit-authorization
description: Codebase-wide authorization-posture audit for AutoNate. Checks gate-presence on every endpoint, comment/code mismatches near auth filters, EntityKind action vocabulary vs. actual enforcement, selector-compiler coverage vs. declared tags, suspicious AuthorizedInHandler/OpenToAuthenticated rationales, DisableAntiforgery without a permission filter, AllowAnonymous justifications, and per-kind enforcement test coverage. Produces a verified punch list with severity. Invoked by `/audit authorization`; can also be invoked directly. Distinct from `audit-security` (broader OWASP surface) — this skill goes deep on the authorization layer specifically.
---

# Authorization audit (whole codebase)

Goes deeper on the authorization surface than `audit-security`'s "B. Endpoint authorization coverage" concern. The trigger for this audit was a series of findings where the gate was either missing, inconsistent with a comment, or applied against an identifier the authorizer couldn't actually resolve. This skill exists to surface that whole class of issue on demand, with verification.

**Scope**: every project under `src/` and `plugins/` plus `tests/AutoNate.Web.Tests/Authorization/`. Do NOT just diff against `main` — that's what `security-review` is for.

## Strategy

Spin up parallel `Explore` agents — one per concern — with hard caps. Then verify each finding by reading the cited file before listing it. The Layer-1 gate-presence test (`AuthorizationGatePresenceTests`) is your friend: if it fails, that's the first finding to report. After confirming it passes, the rest of this audit checks the *quality* of the decisions it accepts.

## Concerns to cover

### A. Layer-1 gate-presence test status
- Run `dotnet test --filter "FullyQualifiedName~AuthorizationGatePresenceTests"`. It MUST pass before the rest of the audit can be trusted — a failure means a new endpoint shipped without an explicit auth decision.
- If the test is `[Skip]`-ed or commented out, that's the top finding regardless of what else turns up.

### B. AuthorizationDecisionMetadata rationale review
Every endpoint with `AuthorizedInHandler("...")` or `OpenToAuthenticated("...")` carries a reason string. These bypass the static gate, so the rationale must be audited:

- **`OpenToAuthenticated`** — re-read the rationale and the handler. The bar is "no per-tenant or per-actor data, system catalog only." Flag any handler whose return data could leak tenant info, even indirectly. Common slip: a "system catalog" endpoint that returns IDs of records the actor can't see.
- **`AuthorizedInHandler`** — re-read the rationale and verify the inline check actually does what the rationale claims. Common slips:
  - Rationale says "filters via `FilterQueryAsync(X, View)`" but handler uses `store.ListAsync(...)` (un-filtered) and forgot to swap.
  - Rationale says "scoped to actor" but handler reads `userId` from the request body or query string instead of `http.GetActorId()`.
  - Rationale says "authorizes Record:Edit on both endpoints" but handler only checks one.
  - Rationale references a filter method that no longer exists (refactor drift).

### C. Comment/code mismatch around auth filters
Greps to run:
- `"admin-only" -A 5 -B 1` near a `MapGet`/`MapPost`/etc. — confirm the gate matches.
- `"any signed-in user"`, `"all authenticated users"`, `"public"` — flag if the route name or comment implies anonymous but the gate is sign-in-only.
- `"reuse the same auth gate as"` and similar — verify the referenced endpoint actually uses what the comment claims.

We hit this exact class of finding twice in the original audit (`AuthorizationExplainEndpoints.cs:8`, `WorkflowBehaviorEndpoints.cs:49`). The pattern is: comment says "X-only" or "matches Y," gate doesn't.

### D. EntityKind action vocabulary vs. endpoint enforcement
Cross-check `Authorization/EntityTypes/CoreEntityTypes.cs` actions against actual endpoint enforcement:

- For each `EntityTypeDefinition`, list the `actions[]` array and find every endpoint that calls `RequirePermission(EntityKinds.X, Actions.<action>)` or `RequireKindPermission(...)`.
- **Grantable-but-inert**: action declared on a kind but no endpoint enforces it. Admin authoring a grant gets nothing. Either remove from `actions[]` or add an enforcing endpoint.
- **Inert-but-enforced**: endpoint gates on `(EntityKinds.X, Actions.Y)` where Y isn't in X's `actions[]`. The grant evaluator silently rejects every grant. Admin lockout. Either add Y to `actions[]` or change the gate.

### E. Selector compiler coverage vs. declared tags
For each `EntityTypeDefinition`, list `tags[]` and find the `ISelectorCompiler` registration in `Program.cs`.

- **`PathOnlySelectorCompiler` for a kind that declares tags**: predicate selectors authored against those tags compile-fail and get *silently skipped* by the authorizer (warning logged). Grants like `/<kind>/*[<tag>=value]` produce 0 matches → admin sees mysterious 403s. Was the gap behind the workflow-start mismatch fix.
- **Tag declared in registry but the kind's compiler doesn't handle it** — same silent-skip behavior. Treat as a finding.
- **Compiler handles a tag not in the registry** — flag as inconsistency; may indicate the tag was intended for grants but never got registered.

### F. CSRF-disabled writes without a permission filter
`grep -B 5 "DisableAntiforgery"` and check each one has at least one of: `RequirePermission`, `RequireKindPermission`, `AuthorizedInHandler`, `OpenToAuthenticated`, or `AllowAnonymous` (the last only with extreme prejudice).

A `MapPost(...).DisableAntiforgery()` without explicit auth metadata is the most-exploitable shape: state-changing, no CSRF token, sign-in-only at best. The gate-presence test catches the missing-metadata case but worth double-checking the metadata is also semantically right.

### G. AllowAnonymous justification
List every `.AllowAnonymous()` call. For each:
- Is it on a pre-auth endpoint (login, register, password-reset)? OK.
- Is it on a shared-secret-protected webhook (e.g. `/api/workflow-behaviors/{key}/execute` with `SharedSecretEndpointFilter`)? OK.
- Anything else: question it.

Pre-auth endpoints additionally need their own CSRF story (see `audit-security` concern A — this audit punts CSRF specifics to that skill).

### H. Per-kind enforcement test coverage
For each `(EntityKind, Action)` pair that has an enforcing endpoint, find a corresponding test in `tests/AutoNate.Web.Tests/Authorization/*EnforcementTests.cs`:

- At minimum a no-grant→403 case.
- For instance-level gates, ideally a positive control with a matching wildcard grant.
- For inline-authorized endpoints, a test that exercises both the included-row and excluded-row paths of the inline filter.

Missing tests aren't security holes per se but they're regression debt — flag the gaps so they can be filled in priority order.

### I. Inline-authorized handler correctness
For each endpoint marked `AuthorizedInHandler(...)`, audit the inline check:

- Actor ID source: `http.GetActorId()` (cookie-bound), not request body / query / route. Flag any deviation.
- For "filters via FilterQueryAsync" rationales: confirm the queryable being filtered is the same one being returned. Common mistake: filter `db.Records` but return `store.ListAsync(...)` (which doesn't go through the filter).
- For "actor-scoped" rationales: confirm the store's `*ForUserAsync` method actually scopes by user. Don't trust the method name; read the SQL.
- For "in-handler AuthorizeAsync" rationales: confirm both branches (allowed + denied) are handled, and that the deny path returns the expected status (403 vs 404 vs filtered-out).

### J. RequirePermission route-token / route-param mismatch
`RequirePermission(kind, action, "routeName")` reads the id from the route value `routeName`. If it doesn't match a `{...}` token in the route, the authorizer gets `null` and denies silently.

- Grep `RequirePermission(...,\s*"\w+"\)` and cross-check the third arg against the `MapXxx` route pattern.
- Default is `"id"`; if the route uses `{recordId:guid}` instead, the gate gets nothing.

## Verification before reporting

For every finding:
1. Read the cited file at the cited line. Confirm the issue is real.
2. For "test missing" claims: check `EnforcementTests.cs` files in the Authorization folder for a matching scenario. Don't trust filename alone; read the test bodies.
3. For "tag has no compiler" claims: open `Program.cs` and the tag's selector compiler file. Confirm the compiler's `CompileExpr` actually rejects the tag (not just "looks path-only at first glance").
4. For "comment mismatch" claims: read 5 lines of context around the comment and confirm the gate immediately below doesn't satisfy the comment's claim.
5. Drop speculative findings. Reporting verified findings is what separates this from a bare grep.

## Output

Markdown report with:

### 1. Punch list
Grouped by concern (A–J above). Each finding:

```
**[H/M/L] file/path.cs:NN — short title**
- What: one-line description of the issue
- Why it matters: one-line concrete consequence (e.g. "admin grants on `processkey` selectors silently produce 403")
- Fix: one-line concrete remediation (e.g. "register a tag-aware compiler for WorkflowModel mirroring `RecordSelectorCompiler`")
```

Cap at 15 most impactful findings; summarize lower-priority items in a footnote.

### 2. What I checked and found clean
Short bulleted list per concern (A–J) so the user knows what was actually examined. E.g. "A: gate-presence test green, 134 endpoints classified, 0 unauthorized."

### 3. Out of scope
- CSRF posture beyond DisableAntiforgery + missing-gate combo → `/audit security` concern A.
- Cookie/TLS/SameSite settings → `/audit security` concern I.
- Permission-system internals (selector parser bugs, evaluator correctness) → not in this checklist; flag for a future deep-dive if needed.
- PR-diff review → `/security-review`.

## Severity rubric

- **High** — exploitable today: missing gate on a state-changing endpoint, dead-tag selector that produces silent 403s for documented admin patterns, gate that doesn't authorize what the endpoint operates on.
- **Medium** — exploitable under future change: comment/code mismatch (next refactor will pick the wrong gate), missing test coverage for a kind/action that's actively enforced, AuthorizedInHandler rationale that's drifted from the handler.
- **Low** — defense-in-depth: stale OpenToAuthenticated rationale that's still technically accurate, missing positive-control test, registered action that's clearly future-work.
