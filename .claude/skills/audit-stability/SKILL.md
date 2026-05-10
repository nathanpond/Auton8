---
name: audit-stability
description: Codebase-wide stability audit for AutoNate. Looks for things that crash, hang, leak, or silently swallow errors — async void, BackgroundService loops without try/catch, swallowed exceptions, fire-and-forget Task.Run, IDisposable misuse, race conditions on singleton state, missing timeouts/cancellation, and cancellation tokens that don't propagate. Distinct from `audit-performance` (which covers throughput/scaling) and `audit-security` (which covers exploitability). Invoked by `/audit stability`; can also be invoked directly.
---

# Stability audit (whole codebase)

A focused pass for the classes of bug that don't show up in tests but bite in production: a `BackgroundService` that throws and tears down the host, a `catch {}` that hides the only signal a remote dependency is broken, a fire-and-forget task that never gets observed when it faults.

**Scope**: every project under `src/` and `plugins/`. Bias toward request handlers and `IHostedService` implementations — those are the surfaces that survive the longest in process and have the highest blast radius when they go wrong.

## Strategy

Parallel `Explore` agents, one per pattern. Each agent reports candidate matches with file:line; the audit verifies each candidate against the surrounding context before listing it. Many of these patterns have intentional uses (e.g. `BackgroundExceptionTrap.cs` deliberately uses `Wait(2s)` because the process is dying — flagging that as sync-over-async would be a false positive).

## Patterns to detect

### A. `async void` outside event handlers
- `private async void Foo(...)` or `public async void Bar(...)` — exceptions can't be caught by the caller. The runtime escalates them to the synchronization context, which on ASP.NET = process crash.
- Acceptable: traditional `EventHandler` overloads, `INotifyPropertyChanged`-style handlers. Everything else should be `async Task`.

### B. `BackgroundService.ExecuteAsync` without exception isolation
- The `while (!stoppingToken.IsCancellationRequested)` loop body must be inside a `try/catch (Exception)` that logs and continues — otherwise one bad tick kills the service for the rest of the host's lifetime.
- Pattern check: every concrete `BackgroundService` (or class inheriting from one) — does its loop body catch unhandled exceptions? `PeriodicIssueDetector` is the canonical correct example; mirror it.
- `Task.Delay` calls on the cancellation token must catch `OperationCanceledException` separately so shutdown is graceful (return cleanly, don't log it as an error).

### C. Swallowed exceptions
- `catch { }` or `catch (Exception) { }` with no logging in non-trivial paths. Even `_ =` discards on what should be observed work.
- The legitimate use is "I'm in a teardown path and I genuinely don't care if cleanup fails" (e.g., `Cleanup()` on a plugin) — those should have a comment explaining why.
- Filter false positives: `try { ... } catch { return null; }` for parse helpers is fine; `try { await DoCriticalWork(); } catch { }` is not.

### D. Fire-and-forget `Task.Run` / `Task.Factory.StartNew`
- `_ = Task.Run(...)` or `Task.Run(...)` where the task isn't awaited or stored. Faults disappear; the request thread pool gets unbounded contention from bursts.
- Especially bad inside request handlers and per-request scoped services. Acceptable inside startup paths or singleton workers if the spawned task observes its own exceptions.
- The canonical remediation in this codebase is a coalescing wake-signal on a singleton `IHostedService` — see `EfCoreMenuStore.OnMenuItemsChanged` → `MisconfiguredMenuItemDetector.RequestImmediateScan` for the pattern.

### E. `IDisposable` not in `using` / `await using`
- Constructed but never disposed: `new HttpClient(...)`, `new SemaphoreSlim(...)` as a local, `new FileStream(...)` without `using`.
- `DbContext` instances NOT inside `await using`. The `IDbContextFactory.CreateDbContextAsync` pattern requires it; missing `await using` leaks connections back to the pool.
- Static `SemaphoreSlim` and `Channel` are fine — they're meant to live forever.

### F. Race conditions on singleton mutable state
- Singleton services with mutable instance fields written from multiple threads without `Interlocked`, `lock`, `Volatile.Read/Write`, or a `SemaphoreSlim`. Pattern: `private XYZ _state;` mutated in two methods both reachable from request threads.
- The canonical correct pattern in this codebase is "snapshot field + refresh under lock" — see `RecordTypeShortCodeCache` and `AgentModelCatalog`. Mirror or flag.
- Special attention to caches that hold reference types: even if the field assignment is atomic on x64, the reference might point at a half-built object. Build the new snapshot fully, *then* assign.

### G. Missing timeouts / unbounded retries
- `HttpClient` calls without timeout — `HttpClient.Timeout` defaults to 100s but should be tuned per dependency. `IHttpClientFactory`-built clients ideally configure this in `AddHttpClient(...).ConfigureHttpClient(c => c.Timeout = ...)`.
- `while (true)` loops in non-`BackgroundService` code without an exit condition or cancellation check.
- `Task.WhenAny(operation, Task.Delay(timeout))` without cancelling the unfinished branch leaks tasks.
- DB queries without `CancellationToken` (any `*Async()` overload exists for a reason; `ToListAsync()` without the token leaves queries running on cancelled requests).

### H. Resource leaks (Channels / SemaphoreSlims / locks)
- `Channel<T>` written to but never read from — the writer blocks indefinitely once the channel fills (bounded) or the process accumulates memory (unbounded).
- `SemaphoreSlim.WaitAsync` without a guaranteed `Release` — usually a missing `try/finally`.
- `lock (obj) { ... await ... }` — you can't await inside a `lock` block. Use `SemaphoreSlim` for an async-safe critical section.
- `await using var x = ...` followed by `Task.Run(() => useX())` — the dispose runs before the task does.

### I. Cancellation token propagation
- Methods with a `CancellationToken cancellationToken` parameter that don't pass it through to inner async calls. Common shape: a public method takes the token, calls `_store.SaveAsync(...)` with no token, and the `_store` method silently uses `default`.
- Exception: methods that should explicitly NOT honor cancellation (audit-event publishing during exception unwind, cleanup paths). Those should pass `CancellationToken.None` deliberately and be commented.

### J. Hot-path observability
- Critical paths without telemetry: a `BackgroundService` that doesn't log on start/stop, a periodic worker without per-tick metrics, a remediator that never reports outcomes. Hard to detect a stability regression you can't see.
- Logging level discipline: error paths logged as `Information`, expected paths logged as `Error`. Both make alerting useless.

## Verification before reporting

- For "swallowed exception" findings: read 5 lines above and below to confirm there's no logger call in the catch (sometimes there's a log immediately before the throw it's catching, or the catch re-throws after recording — those are fine).
- For "missing cancellation propagation" findings: confirm the inner method actually has an overload that takes a token. If not, the finding belongs in a separate "interface evolution" pass, not stability.
- For `async void` findings: distinguish event handlers (legitimate) from accidental fire-and-forget (real bug). A signature like `private async void OnXyzChanged(object? sender, EventArgs e)` is fine.
- For `IDisposable` findings: confirm the type actually implements `IDisposable` or `IAsyncDisposable`. Some classes only implement `IDisposable` for opt-in cleanup that doesn't matter.

## Output

### 1. Punch list
Grouped by concern (A–J). Each finding:

```
**[H/M/L] file/path.cs:NN — short title**
- What: one-line description
- Failure mode: what breaks and how loudly (e.g. "host-killing on first throw" vs. "leaks one connection per request" vs. "silent fault under load")
- Fix: one-line concrete remediation. If a canonical pattern exists in this codebase, point at it.
```

Cap at the 15 most impactful findings.

### 2. What I checked and found clean
Bulleted list per concern so the user knows what was actually examined.

### 3. Tooling that would catch these going forward
- `Microsoft.VisualStudio.Threading.Analyzers` — VSTHRD rules catch sync-over-async, async void, etc.
- `Microsoft.CodeAnalysis.NetAnalyzers` — CA1849 sync-over-async, CA2007 ConfigureAwait, CA2016 cancellation token forwarding.
- `AsyncFixer` — async void, missing await, sync-over-async.
- `SonarAnalyzer.CSharp` — broader stability ruleset (catches swallowed exceptions, mutable state).

If any of these aren't yet wired in `Directory.Build.props` / the project files, flagging that here is itself a finding.

### 4. Out of scope
- Throughput / N+1 / caching → `/audit performance`.
- Exploitability of swallowed errors → `/audit security` (some swallowed errors mask security signals).
- Test coverage → not a stability check; needs a separate `audit-tests` skill if it becomes a concern.
