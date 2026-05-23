using System.Security.Claims;
using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Query;

// Single public entry point for AQL. The SPA endpoint calls ExecuteAsync
// with hardCap = 1000 (the default safety cap). Future export/analytics
// callers can pass null for an uncapped result.
public interface IAqlExecutor
{
    Task<QueryResult> ExecuteAsync(
        string queryText,
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken);
}

public sealed class AqlExecutor : IAqlExecutor
{
    private readonly IQueryEntityRegistry _registry;

    public AqlExecutor(IQueryEntityRegistry registry)
    {
        _registry = registry;
    }

    public async Task<QueryResult> ExecuteAsync(
        string queryText,
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new AqlValidationException("Query is empty.");
        }

        var ast = AqlParser.Parse(queryText);
        var validator = new AqlValidator(_registry);
        var prepared = await validator.ValidateAsync(ast, hardCap, cancellationToken);
        return await prepared.ExecuteAsync(actor, hardCap, cancellationToken);
    }
}
