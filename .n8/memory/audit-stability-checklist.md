# Stability audit checklist (AutoNate-specific)

Harvested from `.claude/skills/audit-stability` on 2026-08-30. Bias toward request handlers and `IHostedService` implementations. Known intentional exception: `BackgroundExceptionTrap.cs` uses `Wait(2s)` deliberately because the process is dying — not a sync-over-async finding.

**A. `async void`** outside `EventHandler`-style signatures → unobservable exception → process crash on ASP.NET.
**B. `BackgroundService.ExecuteAsync`** loop body must sit in `try/catch (Exception)` that logs and continues; `Task.Delay(token)` catches `OperationCanceledException` separately for graceful shutdown. Canonical: `PeriodicIssueDetector`.
**C. Swallowed exceptions** — `catch { }` with no log on non-trivial paths. Legit only in teardown (plugin `Cleanup()`) with a comment; parse helpers returning null are fine.
**D. Fire-and-forget `Task.Run`** in request handlers / scoped services. Fix: coalescing wake-signal on a singleton — `EfCoreMenuStore.OnMenuItemsChanged` → `MisconfiguredMenuItemDetector.RequestImmediateScan`.
**E. `IDisposable` not disposed** — especially `DbContext` from `IDbContextFactory.CreateDbContextAsync` without `await using` (leaks pooled connections). Static `SemaphoreSlim` / `Channel` are fine.
**F. Singleton mutable state races** — mutable fields written from multiple request threads without `Interlocked`/`lock`/`Volatile`/`SemaphoreSlim`. Pattern to mirror: "build full snapshot, then assign" as in `RecordTypeShortCodeCache`, `AgentModelCatalog`.
**G. Missing timeouts / unbounded retries** — `HttpClient` timeout per dependency via `AddHttpClient(...).ConfigureHttpClient`; `while(true)` without cancellation; `WhenAny(op, Delay)` leaking the loser; `*Async()` without the `CancellationToken`.
**H. Channel / semaphore leaks** — writer with no reader; `WaitAsync` without `try/finally Release`; `await` inside `lock`; `await using var x` then `Task.Run(() => useX())`.
**I. Cancellation propagation** — token parameter accepted but not forwarded; deliberate `CancellationToken.None` (audit publish during unwind, cleanup) must be commented.
**J. Hot-path observability** — hosted services log start/stop, periodic workers emit per-tick outcomes, remediators report; log levels honest (errors not `Information`, expected paths not `Error`).

Analyzers already wired in `Directory.Build.props`: `Microsoft.VisualStudio.Threading.Analyzers`, `AsyncFixer`, `SonarAnalyzer.CSharp`, NetAnalyzers (`AnalysisMode=Recommended`). Anything these should catch but was suppressed in `.editorconfig` is worth re-checking here.
