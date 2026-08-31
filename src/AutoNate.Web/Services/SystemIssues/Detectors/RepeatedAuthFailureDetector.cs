using System.Collections.Concurrent;
using System.Text.Json;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.BusWatcher;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Reactive. Listens to auth.events and tracks a per-username sliding window
// of auth.login.failed events. When the window crosses Threshold, opens a
// category=auth issue. The window expires entries as it slides, so steady
// background failures (one bad password every 15 min) never trip the
// detector — only bursts do.
//
// Per-username (not per-IP) because the username is what identifies "the
// account being attacked." A determined attacker rotating IPs against one
// account still produces a clear signal here. IP-based detection is a
// natural Phase-6 follow-up if needed.
//
// In-memory window is rebuilt on restart — fine, the window is short and
// the bus stream feeds it again immediately. Persisting the window would
// be overkill.
public sealed class RepeatedAuthFailureDetector(
    BusWatcherStreamService busWatcher,
    ISystemIssueRecorder recorder,
    IOptions<RepeatedAuthFailureDetectorOptions> authOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<RepeatedAuthFailureDetector> logger) : IHostedService
{
    public const string DetectorIdValue = "repeated_auth_failure";

    private readonly RepeatedAuthFailureDetectorOptions _authOptions = authOptions.Value;
    private readonly SystemIssueOptions _systemIssueOptions = systemIssueOptions.Value;

    // Concurrent: bus watcher dispatches messages on whatever thread Dapr
    // hands the publish to.
    //
    // The key is an attacker-supplied username from the unauthenticated login
    // endpoint, so this map is only safe if entries leave it. Credential
    // stuffing with rotating usernames would otherwise add a string + window +
    // queue per attempt to a singleton that lives for the process, long past
    // the 5-minute window they fall out of (#72). Every RecordFailure sweeps
    // windows that have gone empty, and MaxTrackedUsernames is a hard ceiling
    // for the case where arrivals outpace the sweep.
    private readonly ConcurrentDictionary<string, FailureWindow> _windows =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxTrackedUsernames = 10_000;

    // Internal so the bounding can be asserted without reaching into the map.
    internal int TrackedUsernameCount => _windows.Count;

    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_systemIssueOptions.DetectorsEnabled)
        {
            logger.LogInformation(
                "Detector {DetectorId} disabled via {Section}:DetectorsEnabled.",
                DetectorIdValue, SystemIssueOptions.SectionName);
            return Task.CompletedTask;
        }
        _subscription = busWatcher.Subscribe(HandleAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    // Public for tests.
    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(message.Topic, AuthEventTopic.TopicName, StringComparison.Ordinal))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(message.Payload)) return;

        try
        {
            using var doc = JsonDocument.Parse(message.Payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            var eventType = ReadString(root, "eventType");
            if (!string.Equals(eventType, AuthEventTypes.LoginFailed, StringComparison.Ordinal))
            {
                return;
            }

            var username = ReadUsername(root);
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            var (count, windowStart) = RecordFailure(username);
            if (count < _authOptions.Threshold)
            {
                return;
            }

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Auth,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: $"auth:repeated_failures:{username.ToLowerInvariant()}",
                Title: $"Repeated auth failures for '{username}' ({count} in {(int)_authOptions.Window.TotalMinutes} min)",
                Summary: $"{count} auth.login.failed events for {username} since {windowStart:O}.",
                RelatedEntityKind: "username",
                RelatedEntityId: username,
                FactsJson: JsonSerializer.Serialize(new
                {
                    username,
                    failuresInWindow = count,
                    windowMinutes = (int)_authOptions.Window.TotalMinutes,
                    threshold = _authOptions.Threshold,
                    windowStart
                })), cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "RepeatedAuthFailureDetector could not parse auth event payload.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "RepeatedAuthFailureDetector failed to record an issue for an auth.login.failed message.");
        }
    }

    // Adds a failure timestamp to the username's window, evicts old entries,
    // and returns (count-after-eviction, windowStart). Internal so tests can
    // verify the windowing without touching the bus.
    internal (int Count, DateTimeOffset WindowStart) RecordFailure(string username)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - _authOptions.Window;

        var window = _windows.GetOrAdd(username, _ => new FailureWindow());
        int count;
        lock (window)
        {
            while (window.Failures.Count > 0 && window.Failures.Peek() < cutoff)
            {
                window.Failures.Dequeue();
            }
            window.Failures.Enqueue(now);
            count = window.Failures.Count;
        }

        Sweep(cutoff);
        return (count, cutoff);
    }

    // Drops usernames whose window has emptied. O(n) over tracked usernames,
    // which the ceiling keeps small, and only on a login *failure* — not on
    // any hot success path.
    private void Sweep(DateTimeOffset cutoff)
    {
        foreach (var (key, tracked) in _windows)
        {
            bool empty;
            lock (tracked)
            {
                while (tracked.Failures.Count > 0 && tracked.Failures.Peek() < cutoff)
                {
                    tracked.Failures.Dequeue();
                }
                empty = tracked.Failures.Count == 0;
            }
            if (empty)
            {
                // Racy against a concurrent Enqueue on the same username: the
                // loser re-adds a fresh window on its next failure, which is
                // the same state either way.
                _windows.TryRemove(key, out _);
            }
        }

        if (_windows.Count <= MaxTrackedUsernames) return;

        // Arrivals outpaced the sweep (a burst inside one window). Shed the
        // oldest-observed entries rather than letting the map grow without
        // bound; a shed username simply starts counting again.
        foreach (var key in _windows
                     .OrderBy(pair => pair.Value.FirstSeenUtc)
                     .Take(_windows.Count - MaxTrackedUsernames)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _windows.TryRemove(key, out _);
        }
    }

    private static string? ReadUsername(JsonElement root)
    {
        if (root.TryGetProperty("resource", out var resource) && resource.ValueKind == JsonValueKind.Object)
        {
            var username = ReadString(resource, "username");
            if (!string.IsNullOrWhiteSpace(username)) return username;
        }
        return null;
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            _ => null
        };
    }

    private sealed class FailureWindow
    {
        public Queue<DateTimeOffset> Failures { get; } = new();

        // Only used to pick shed victims when the ceiling is hit.
        public DateTimeOffset FirstSeenUtc { get; } = DateTimeOffset.UtcNow;
    }
}

public sealed class RepeatedAuthFailureDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:RepeatedAuthFailure";

    // Open an issue when this many auth.login.failed events arrive for the
    // same username inside the rolling Window. 5 in 5 minutes catches
    // password-spray bursts without flagging "user typed wrong password
    // twice while logging in".
    public int Threshold { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}
