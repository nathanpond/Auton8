using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace AutoNate.Web.Services.SystemIssues;

// Builds + records the SystemIssueDraft for a caught-and-rethrown exception
// from the HTTP middleware, the AppDomain trap, or the unobserved-task trap.
// Centralised so the three call sites agree on detector ids, fingerprint
// scope strings, severity defaults, and facts shape.
internal static class UnhandledExceptionRecorder
{
    public static Task RecordHttpAsync(
        ISystemIssueRecorder recorder,
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var topFrame = UnhandledExceptionFingerprint.ExtractTopFrame(exception.StackTrace);
        var facts = JsonSerializer.Serialize(new
        {
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            topFrame,
            method = httpContext.Request.Method,
            path = httpContext.Request.Path.Value,
            queryString = httpContext.Request.QueryString.HasValue
                ? httpContext.Request.QueryString.Value
                : null
        });

        var draft = new SystemIssueDraft(
            DetectorId: "unhandled_http_exception",
            Category: SystemIssueCategories.Unhandled,
            Severity: SystemIssueSeverities.Error,
            Fingerprint: UnhandledExceptionFingerprint.Compute("http", exception),
            Title: $"Unhandled {exception.GetType().Name} on {httpContext.Request.Method} {httpContext.Request.Path.Value}",
            Summary: exception.Message,
            FactsJson: facts);

        return recorder.RecordAsync(draft, cancellationToken);
    }

    public static Task RecordAppDomainAsync(
        ISystemIssueRecorder recorder,
        Exception exception,
        bool isTerminating,
        CancellationToken cancellationToken)
    {
        var topFrame = UnhandledExceptionFingerprint.ExtractTopFrame(exception.StackTrace);
        var facts = JsonSerializer.Serialize(new
        {
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            topFrame,
            isTerminating
        });

        // IsTerminating means the runtime is about to kill the process — the
        // operator wants to know about it before the next restart, so push to
        // critical. A non-terminating AppDomain unhandled (rare) stays at error.
        var severity = isTerminating
            ? SystemIssueSeverities.Critical
            : SystemIssueSeverities.Error;

        var draft = new SystemIssueDraft(
            DetectorId: "unhandled_appdomain_exception",
            Category: SystemIssueCategories.Unhandled,
            Severity: severity,
            Fingerprint: UnhandledExceptionFingerprint.Compute("appdomain", exception),
            Title: $"AppDomain unhandled {exception.GetType().Name}",
            Summary: exception.Message,
            FactsJson: facts);

        return recorder.RecordAsync(draft, cancellationToken);
    }

    public static Task RecordUnobservedTaskAsync(
        ISystemIssueRecorder recorder,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var topFrame = UnhandledExceptionFingerprint.ExtractTopFrame(exception.StackTrace);
        var facts = JsonSerializer.Serialize(new
        {
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            topFrame
        });

        var draft = new SystemIssueDraft(
            DetectorId: "unobserved_task_exception",
            Category: SystemIssueCategories.Unhandled,
            Severity: SystemIssueSeverities.Error,
            Fingerprint: UnhandledExceptionFingerprint.Compute("task", exception),
            Title: $"Unobserved task exception: {exception.GetType().Name}",
            Summary: exception.Message,
            FactsJson: facts);

        return recorder.RecordAsync(draft, cancellationToken);
    }
}
