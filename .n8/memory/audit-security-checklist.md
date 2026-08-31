# Security audit checklist (AutoNate-specific)

Harvested from `.claude/skills/audit-security` on 2026-08-30. Use as the concern list when `/n8-audit` runs a security pass. Scope: `src/` and `plugins/`.

**A. CSRF / antiforgery** — every `MapPost/Put/Delete/Patch` has `[FromForm]`, `.RequireAntiforgery()`, or a deliberate `.DisableAntiforgery()`. Pre-auth state changers (login) are the login-CSRF class: remediation is server-issued token + SPA fetch or Origin/Referer check. `SameSite` on the auth cookie does NOT mitigate login CSRF (cookie is set after the attack POSTs). Posture to hold: `SameSite=Strict`, `SecurePolicy=Always` outside Development on both `AddCookie` and `AddAntiforgery`.

**B. Endpoint authorization** — anything outside an `AllowAnonymous` group has group `RequireAuthorization()` or per-endpoint `RequirePermission` / `RequireKindPermission`. Cross-check `Authorization/EntityTypes/CoreEntityTypes.cs` actions vs. enforcing endpoints (see the authorization checklist for the full treatment).

**C. Secrets** — `grep -rn -E "(api[_-]?key|secret|password|token|connection[_-]?string)\s*[=:]\s*[\"']\w" src/ plugins/`; dev-only values in `appsettings.Development.json` are documented in README "Deployment configuration" as required prod overrides. `appsettings.json` must not carry real `WorkflowBehaviors:CallbackSharedSecret`, `ConnectionStrings:Default`, `Flowable:Password`.

**D. Injection** — `ExecuteSqlRaw` / `FromSqlRaw` with interpolation (the `*Interpolated` family is parameterized). Path traversal: user input into `Path.Combine` / `File.*` / `Directory.*` needs `Path.GetFullPath` + `StartsWith` boundary check — canonical pattern `PluginRuntime.TryResolveThumbnailDir`. `Process.Start` with composed args should be ~nonexistent.

**E. Deserialization** — no `Deserialize<object|dynamic>` with type-name polymorphism, no `BinaryFormatter`; `JsonElement` parses of DB rows wrapped in try so corrupt rows don't 500.

**F. Open redirect** — `Results.Redirect` with user URLs must go through `LocalRedirect` / same-origin allowlist; login `returnUrl` is the canonical spot.

**G. Mass assignment** — request DTOs exposing `IsAdmin`, `CreatedBy`, `Id`, `ServiceFlags`…; check the store's `CreateAsync`/`UpdateAsync` doesn't pass them through.

**H. Plugin isolation** — `PluginAssemblyLoadContext.SharedAssemblies` must cover every host-loaded assembly a plugin might duplicate, and `plugins/Directory.Build.targets` zip-exclusion list must match it. Plugin `Code` is constrained to `[a-z][a-z0-9]{7}` by `PluginSchemaProvisioner.GenerateCode`, but path building from it still boundary-checks. Per-plugin Postgres role: `LOGIN`, restricted to `plg_<code>` schema (verify in `DatabaseSchemaInitializer`).

**I. Cookie / TLS / proxy** — `AddCookie` + `AddAntiforgery` options (`SameSite`, `SecurePolicy`, `HttpOnly`, `ExpireTimeSpan`, `SlidingExpiration`); `AllowedHosts` `"*"` is dev-only; TLS terminates upstream — look for `UseForwardedHeaders` or record its absence as deployment debt.

**J. AllowAnonymous inventory** — list every `.AllowAnonymous()`; each is login / branding / health / shared-secret webhook (`SharedSecretEndpointFilter`, `YjsInternalSecretEndpointFilter`) or a question mark.

Out of scope: dependency CVEs → `dotnet list package --vulnerable --include-transitive`; SSRF/DDoS → separate pass.
