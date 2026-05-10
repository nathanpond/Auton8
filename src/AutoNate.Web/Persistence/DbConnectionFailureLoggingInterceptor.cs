using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AutoNate.Web.Persistence;

// Diagnostic interceptor that logs the full exception when a DbConnection
// open/close fails. EF Core's built-in 20004 logger (`Microsoft.EntityFrameworkCore
// .Database.Connection` at Error) emits only the lead "An error occurred using
// the connection" line and drops the inner exception in our current logging
// setup, which makes "sporadic transient drops" indistinguishable from real
// faults. This interceptor fills the gap by logging exception type + message +
// inner-exception chain at Warning level, so the next 20004 actually tells us
// whether it's pool-stale (`NpgsqlException: Connection idle ...`), TCP
// (`IOException: Broken pipe`), Postgres-side (`PostgresException: 57P01
// terminating connection due to administrator command`), or something else.
public sealed class DbConnectionFailureLoggingInterceptor(
    ILogger<DbConnectionFailureLoggingInterceptor> logger) : DbConnectionInterceptor
{
    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Log(eventData);
        return Task.CompletedTask;
    }

    public override void ConnectionFailed(
        DbConnection connection,
        ConnectionErrorEventData eventData)
        => Log(eventData);

    private void Log(ConnectionErrorEventData eventData)
    {
        var ex = eventData.Exception;

        // Cancellations during connect-and-authenticate aren't real failures
        // — they're the SPA's HttpContext.RequestAborted firing mid-handshake
        // when a tab closes/navigates/re-fetches. Nothing to act on, and they
        // burst when the user re-renders a page. Skip them.
        if (ex is OperationCanceledException) return;

        logger.LogWarning(ex,
            "DB connection failed. Type={ExceptionType} Message={Message} Inner={InnerType}/{InnerMessage}",
            ex?.GetType().FullName ?? "(none)",
            ex?.Message ?? "(none)",
            ex?.InnerException?.GetType().FullName ?? "(none)",
            ex?.InnerException?.Message ?? "(none)");
    }
}
