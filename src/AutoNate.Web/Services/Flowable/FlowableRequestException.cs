using System.Net;

namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// A non-success response from Flowable, carrying the status it answered with (#226).
/// </summary>
/// <remarks>
/// <para>
/// Every Flowable failure used to become a bare <see cref="InvalidOperationException"/>,
/// so a caller error the engine had already classified correctly — a 409 for a
/// variable that exists, a 400 for "Converter can only convert booleans" —
/// reached the client as a **500 with a stack trace**. The message said what to
/// fix; the status code said the server broke, and a 500 is what pages someone.
/// </para>
/// <para>
/// Derives from <see cref="InvalidOperationException"/> deliberately. Callers
/// that already catch that keep working unchanged, so this adds information
/// without becoming a migration.
/// </para>
/// <para>
/// One thing it is NOT source-compatible with: xUnit's
/// <c>Assert.ThrowsAsync&lt;T&gt;</c> demands an exact type match, so every
/// assertion expecting the base type from a non-2xx response had to name this
/// one instead. That is a test-only change and, on the way, a stronger
/// assertion — those tests can now pin the status that was carried.
/// </para>
/// </remarks>
public sealed class FlowableRequestException(
    HttpStatusCode statusCode, string operation, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string Operation { get; } = operation;

    /// <summary>
    /// Whether Flowable blamed the caller. Only these are worth passing through;
    /// a 5xx from Flowable really is a server fault on our side of the call.
    /// </summary>
    public bool IsCallerError => (int)StatusCode >= 400 && (int)StatusCode < 500;
}
