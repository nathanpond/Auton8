---
name: audit-security
description: Codebase-wide security audit for AutoNate. Checks CSRF posture (especially pre-auth endpoints like /account/login), missing endpoint authorization, hardcoded secrets, SQL/path/command injection, unsafe deserialization, open redirect, mass assignment, plugin-load isolation, and cookie + TLS posture. Produces a verified punch list with severity. Invoked by `/audit security`; can also be invoked directly.
---

# Security audit (whole codebase)

This is a focused, repeatable security pass over the entire AutoNate repo. Designed to catch the classes of issues that an external scanner would flag — particularly the ones I've historically missed in narrower passes (login CSRF, pre-auth state changes, plugin sandbox escapes).

**Scope**: every project under `src/` and `plugins/`. Do NOT just diff against `main` — that's what the `security-review` skill is for.

## Strategy

Spin up parallel `Explore` agents, one per concern, with hard caps. Then verify each finding by reading the cited file before listing it in the report.

Why parallel agents: each concern needs a different grep+inspection pattern, and they're independent. Eight agents in parallel finishes faster than eight sequential greps and protects the main context window.

## Concerns to cover (one agent per concern unless noted)

### A. CSRF + antiforgery posture
- Every `MapPost` / `MapPut` / `MapDelete` / `MapPatch` either has `[FromForm]` (auto-validates), `.RequireAntiforgery()` metadata, or has been deliberately exempted via `.DisableAntiforgery()`.
- Pre-auth state-changing endpoints get extra scrutiny. Those are the login-CSRF class. The standard remediation is the antiforgery flow (server-issued token + SPA fetch) or Origin/Referer validation. `SameSite` on the auth cookie does NOT mitigate login CSRF — the new cookie is set after the attack POSTs.
- Cookie `SameSite` and `SecurePolicy` settings on `AddCookie` and `AddAntiforgery`. Strict + Always-in-non-Dev is the current AutoNate posture; flag deviations.

### B. Endpoint authorization coverage
- Every `MapGet`/`MapPost`/etc. that's not in an `AllowAnonymous` group has either group-level `RequireAuthorization()` or per-endpoint `RequirePermission`/`RequireKindPermission`.
- Cross-check `Authorization/EntityTypes/CoreEntityTypes.cs` actions against actual endpoint enforcement: an action registered on a kind that has no enforcing endpoint is grantable-but-inert (admin confusion). An endpoint that gates on `EntityKinds.X` where X isn't in `_all` rejects every grant (admin lockout).
- For each `EntityKind`, list the actions it advertises and the endpoints that enforce each one. Mismatches are the finding.

### C. Hardcoded secrets / committed credentials
- `grep -rn -E "(api[_-]?key|secret|password|token|connection[_-]?string)\s*[=:]\s*[\"']\w" src/ plugins/`. Filter out test fixtures and dev-only `appsettings.Development.json` entries (those are documented in the README's deployment-config section as required prod overrides).
- `appsettings.json` (production-default) must not contain real values for `WorkflowBehaviors:CallbackSharedSecret`, `ConnectionStrings:Default`, `Flowable:Password`, etc.
- Check `.gitignore` covers `.env`, `data/`, `*.user`, etc.

### D. Injection (SQL, command, path traversal)
- **SQL**: `ExecuteSqlRaw`, `FromSqlRaw` with string interpolation. The `FromSqlInterpolated` family is parameterized — distinguish carefully.
- **Path traversal**: file ops (`File.Open`, `Path.Combine`, `Directory.*`) that take user input. Look for `Path.GetFullPath` + `StartsWith` boundary checks (the canonical mitigation in this codebase — see `PluginRuntime.TryResolveThumbnailDir` for the pattern).
- **Command**: `Process.Start` with composed argument strings. Should be vanishingly rare in this codebase.

### E. Unsafe deserialization
- `JsonSerializer.Deserialize<object>`, `JsonSerializer.Deserialize<dynamic>` with TypeNameHandling-style polymorphism. `BinaryFormatter` (banned in .NET 5+ but worth a grep).
- `JsonElement` parsing without `try` around the parse — corrupted DB rows shouldn't 500 the app.

### F. Open redirect
- `Results.Redirect(...)` with user-supplied URLs that aren't passed through `LocalRedirect` or a same-origin allowlist. Login `returnUrl` is the canonical place this surfaces.

### G. Mass assignment
- Endpoints that bind a request DTO containing fields the caller shouldn't be able to set (`IsAdmin`, `CreatedBy`, `Id` overrides, `ServiceFlags`, etc.). Cross-check against the matching store's `CreateAsync` / `UpdateAsync` — does the endpoint pass the user-supplied field straight through?

### H. Plugin sandbox + load-context isolation
- `PluginAssemblyLoadContext.SharedAssemblies` covers every assembly the host loads (otherwise plugins ship a duplicate copy, fail type identity, and cast errors at load time — that's a stability issue, but the inverse is a security issue: a plugin shipping its own copy of an abstractions assembly that diverges from the host's).
- `plugins/Directory.Build.targets` zip-exclusion list matches `SharedAssemblies`.
- Plugin-code path validation: anything that takes a plugin's `Code` and builds a filesystem path must boundary-check (see `PluginRuntime.TryResolveThumbnailDir`). The plugin code itself is constrained to `[a-z][a-z0-9]{7}` by `PluginSchemaProvisioner.GenerateCode`, but defense-in-depth is the rule for paths.
- Per-plugin Postgres role uses `LOGIN` and is restricted to `plg_<code>` schema reads/writes (verify in `DatabaseSchemaInitializer`).

### I. Cookie + TLS + reverse-proxy posture
- `AddCookie` options: `SameSite`, `SecurePolicy`, `HttpOnly`, `ExpireTimeSpan`, `SlidingExpiration`.
- `AddAntiforgery` options: same shape.
- `AllowedHosts` in `appsettings.json` — `"*"` is fine in dev (per README) but flag it as something the deployment must override.
- Reverse-proxy assumptions: app expects TLS termination upstream and `X-Forwarded-*` headers. Look for `UseForwardedHeaders` configuration or document its absence as deployment-config debt.

### J. Anonymous endpoints sanity
- Every `.AllowAnonymous()` is intentional. List them. Each one should be either explicitly safe (login, branding, health) or flagged as a question mark.

## Verification before reporting

For every finding:
1. Read the cited file at the cited line. Confirm the issue is real (not a false-positive grep).
2. If the finding is "X has no callers" or "Y is dead," run `dotnet build` and check there are no warnings/errors. For TS, delete the relevant `tsconfig.*.tsbuildinfo` and run `npx tsc -b --force`. (See `feedback_unused_ts_module_verification.md` for why.)
3. Drop the finding if verification reveals it's not real. Don't list speculative items.

## Output

Markdown report with:

### 1. Punch list
Grouped by concern (A–J above). Each finding:

```
**[H/M/L] file/path.cs:NN — short title**
- What: one-line description of the issue
- Why it matters: one-line concrete consequence
- Fix: one-line concrete remediation (or pointer to a remediation skill if one exists)
```

Cap at the 15 most impactful findings. If more exist, summarize them in a "Lower-priority items also seen" footnote.

### 2. What I checked and found clean
Short bulleted list per concern (A–J) so the user knows what was actually examined. E.g. "C: ran the secret-shape regex against `src/` and `plugins/` (3,200 files); no matches outside the documented dev-only `appsettings.Development.json`."

### 3. Out of scope
- PR-diff review → use `/security-review` instead.
- Dependency CVEs → use `dotnet list package --vulnerable` (run separately).
- Runtime / network attacks (DDoS, SSRF on outbound calls) → not in this checklist; flag for a future `audit-network` skill if it becomes a concern.
