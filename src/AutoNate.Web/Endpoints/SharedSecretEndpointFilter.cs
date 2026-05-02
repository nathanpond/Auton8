using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AutoNate.Web.Services.Workflow.Behaviors;

namespace AutoNate.Web.Endpoints;

// Endpoint filter that gates a route on a shared-secret header. Used by the
// workflow-behavior callback endpoint, which is invoked by the Flowable
// JavaDelegate from inside the JVM — there is no authenticated browser
// session to ride on, and the request is intra-cluster.
//
// The header is matched in constant time so a timing oracle can't pull the
// secret one byte at a time. A missing header / mismatch / unconfigured
// secret all produce 401; treating "unconfigured" as 500 would leak which
// of the two is broken.
//
// v1: secret in config. v2 path: rotate via Dapr-managed mTLS.
public sealed class SharedSecretEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "X-AutoNate-Internal-Token";

    private readonly IOptionsMonitor<WorkflowBehaviorOptions> _options;
    private readonly ILogger<SharedSecretEndpointFilter> _log;

    public SharedSecretEndpointFilter(
        IOptionsMonitor<WorkflowBehaviorOptions> options,
        ILogger<SharedSecretEndpointFilter> log)
    {
        _options = options;
        _log = log;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = _options.CurrentValue.CallbackSharedSecret;
        if (string.IsNullOrEmpty(configured))
        {
            _log.LogWarning("Workflow-behavior callback received but no shared secret is configured.");
            return Results.Unauthorized();
        }

        var supplied = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied))
        {
            return Results.Unauthorized();
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        if (configuredBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
