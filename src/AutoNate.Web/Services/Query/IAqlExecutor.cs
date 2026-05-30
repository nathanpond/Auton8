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

    // Phase 3 share-link path. Takes the saved query's text, binds any
    // `:paramName` placeholders to caller-supplied values, then validates
    // and executes. Throws AqlParameterBindingException for unbound refs.
    Task<QueryResult> ExecuteBoundAsync(
        string queryText,
        IReadOnlyDictionary<string, string>? parameters,
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

    public async Task<QueryResult> ExecuteBoundAsync(
        string queryText,
        IReadOnlyDictionary<string, string>? parameters,
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new AqlValidationException("Query is empty.");
        }
        var ast = AqlParser.Parse(queryText);
        var bound = AqlParameterBinder.Bind(ast, parameters);
        var validator = new AqlValidator(_registry);
        var prepared = await validator.ValidateAsync(bound, hardCap, cancellationToken);
        return await prepared.ExecuteAsync(actor, hardCap, cancellationToken);
    }
}
