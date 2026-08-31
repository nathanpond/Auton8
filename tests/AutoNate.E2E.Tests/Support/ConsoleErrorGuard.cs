using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit.Sdk;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Listens for browser-side errors on an <see cref="IPage"/> and fails the
/// owning test on dispose if any non-allowlisted error was emitted. Catches
/// the class of bugs an "h1 exists" / "row appears" assertion misses — invalid
/// DOM nesting, hydration warnings, uncaught exceptions in event handlers, 4xx
/// responses logged by axios interceptors, React's act-warning storms, and so
/// on. Two event sources are tracked:
/// <list type="bullet">
///   <item><description><c>page.Console</c> filtered to <c>type == "error"</c>
///   (everything React, Mantine, or our own code writes via
///   <c>console.error</c>).</description></item>
///   <item><description><c>page.PageError</c> — uncaught exceptions that
///   reach <c>window.onerror</c>.</description></item>
/// </list>
/// Allowlist matches by substring (case-insensitive) — most browser-side
/// messages have stable identifying text. A test that intentionally drives an
/// error path can call <see cref="Allow"/> before triggering it.
/// </summary>
public sealed class ConsoleErrorGuard : IAsyncDisposable
{
    // Default allowlist applied to every guard. These are *not* the class of
    // bug this guard is trying to catch (DOM nesting / hydration / uncaught
    // JS), they're either browser-level network telemetry or intentional
    // deny-path signals the suite exercises in multiple tests.
    //
    // Resist the urge to grow this list. Each entry hides a category of
    // regressions for the entire suite — prefer per-test `Allow(...)` when
    // a single test legitimately drives an error path.
    private static readonly string[] DefaultAllowed =
    [
        // Chromium emits this for any non-2xx HTTP response, even when the
        // SPA caught the axios error and rendered a graceful state. Asserting
        // the rendered state is the right check for those flows; the browser
        // log is noise here.
        "Failed to load resource",

        // Yjs's WebSocket layer logs an error on the server-side
        // permission-denied path. The DocumentEditorTests and NotesTests
        // grant/revoke tests deliberately drive that path.
        "authentication-failed",

        // Chromium's benign "observer changed a size it was observing" notice.
        // Mantine >= 9.4 `Textarea autosize` (components/Textarea/Autosize)
        // observes the textarea and sets its own height inside the callback
        // whenever the width changes (sidebar resize, viewport resize), so
        // the browser reports the loop as a window `error` event. It is not a
        // JS exception and layout settles on the next frame. Tracked in the
        // repo issue for the upstream Mantine bug; drop this entry once
        // upstream defers the height write.
        "ResizeObserver loop completed with undelivered notifications"
    ];

    private readonly IPage _page;
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly List<string> _allowed = new(DefaultAllowed);
    private readonly EventHandler<IConsoleMessage> _onConsole;
    private readonly EventHandler<string> _onPageError;
    private bool _disposed;

    public ConsoleErrorGuard(IPage page)
    {
        _page = page;
        _onConsole = (_, msg) =>
        {
            if (msg.Type == "error") _errors.Enqueue($"[console.error] {msg.Text}");
        };
        _onPageError = (_, text) =>
        {
            _errors.Enqueue($"[pageerror] {text}");
        };
        _page.Console += _onConsole;
        _page.PageError += _onPageError;
    }

    /// <summary>
    /// Allow any future or already-collected error whose text contains the
    /// given substring (case-insensitive). Use sparingly: each suppression
    /// hides a class of regressions the guard would otherwise catch.
    /// </summary>
    public ConsoleErrorGuard Allow(string substring)
    {
        _allowed.Add(substring);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _page.Console -= _onConsole;
        _page.PageError -= _onPageError;

        var unexpected = _errors
            .Where(e => !_allowed.Any(a =>
                e.Contains(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unexpected.Count == 0) return;

        // XunitException + an empty completed Task lets us throw from an
        // async-dispose path the test framework will surface as a normal
        // test failure (Xunit awaits the IAsyncDisposable.DisposeAsync of
        // values caught by `await using` and reports a thrown exception as
        // a test-level failure).
        await Task.CompletedTask;
        throw new XunitException(
            $"Captured {unexpected.Count} unexpected browser error(s) on {_page.Url}:\n  - "
            + string.Join("\n  - ", unexpected));
    }
}
