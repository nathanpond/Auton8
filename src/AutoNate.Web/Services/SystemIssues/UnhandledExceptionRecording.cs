using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.SystemIssues;

// Middleware that records unhandled HTTP exceptions as SystemIssues, then
// rethrows so the existing dev-exception-page (development) or default 500
// handler (production) still produces the response. Sits as the outermost
// user-registered middleware so it wraps the entire request pipeline.
//
// Why a try/catch middleware instead of an IExceptionHandler: the implicit
// dev exception page is a middleware itself, and we want recording to work
// in *both* dev and prod without changing how errors are rendered. Rethrowing
// after recording leaves the chosen renderer (dev page or default 500) in
// charge of the response.
public static class UnhandledExceptionRecording
{
    public static IApplicationBuilder UseUnhandledExceptionSystemIssues(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected mid-request (browser closed, navigation,
                // polling abort). Not an application bug — don't record, just
                // rethrow so the framework can finish unwinding the pipeline.
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    var recorder = context.RequestServices.GetRequiredService<ISystemIssueRecorder>();
                    // Intentionally NOT context.RequestAborted: when the request
                    // faults, the client often disconnects, which cancels the
                    // abort token. Threading that into the recorder cancels the
                    // DB write before the row is inserted — the very case we
                    // most want recorded. Decouple the recording from the
                    // request lifetime so the issue lands even if the caller
                    // walked away.
                    await UnhandledExceptionRecorder.RecordHttpAsync(
                        recorder,
                        context,
                        ex,
                        CancellationToken.None);
                }
                catch (Exception recordEx)
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(UnhandledExceptionRecording).FullName!);
                    logger.LogError(
                        recordEx,
                        "Failed to record unhandled HTTP exception as a system issue.");
                }
                throw;
            }
        });
    }
}
