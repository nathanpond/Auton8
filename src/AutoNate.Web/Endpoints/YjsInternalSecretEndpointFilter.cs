using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AutoNate.Web.Services.Yjs;

namespace AutoNate.Web.Endpoints;

// Gates /internal/yjs-* routes on a shared-secret header. Same pattern as
// SharedSecretEndpointFilter (workflow-behavior callback), kept as a parallel
// class so the two secrets can rotate independently — Yjs and workflow
// behaviors are different trust boundaries.
//
// Constant-time comparison; treat missing-header, missing-config, and
// mismatch all as 401 so a timing oracle can't distinguish "unconfigured"
// from "wrong secret."
public sealed class YjsInternalSecretEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "X-AutoNate-Internal-Token";

    private readonly IOptionsMonitor<YjsServerOptions> _options;
    private readonly ILogger<YjsInternalSecretEndpointFilter> _log;

    public YjsInternalSecretEndpointFilter(
        IOptionsMonitor<YjsServerOptions> options,
        ILogger<YjsInternalSecretEndpointFilter> log)
    {
        _options = options;
        _log = log;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = _options.CurrentValue.InternalSharedSecret;
        if (string.IsNullOrEmpty(configured))
        {
            _log.LogWarning("Yjs internal callback received but no shared secret is configured.");
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
