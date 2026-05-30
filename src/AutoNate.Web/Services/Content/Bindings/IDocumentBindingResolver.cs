using System.Security.Claims;

namespace AutoNate.Web.Services.Content.Bindings;

// One resolver per binding kind. The endpoint layer passes the raw
// config JSON + the calling principal; the resolver parses the config,
// executes the underlying query (records, AQL, etc.) under the caller's
// permissions, and returns the resolved value as JSON to be stamped
// into `document_bindings.last_resolved_value_jsonb`.
//
// Resolvers run with the calling user's permissions — this matters
// because per-row authorization filters can change what each user sees.
// The `LastResolvedByUserId` column records whose view produced the
// stored snapshot so reviewers know what they're looking at.
public interface IDocumentBindingResolver
{
    /// <summary>The kind discriminator this resolver handles.</summary>
    string Kind { get; }

    /// <summary>
    /// Resolve the binding under the calling principal's permissions.
    /// Returns the JSON snapshot to persist + the human-readable label
    /// suggestion. Throws DocumentBindingResolveException on a
    /// caller-visible failure (bad config, missing referenced row, no
    /// permission to read the underlying data) — the endpoint catches
    /// and converts to a 4xx with the message preserved.
    /// </summary>
    Task<DocumentBindingResolveResult> ResolveAsync(
        string configJsonb,
        ClaimsPrincipal actor,
        CancellationToken ct);
}

public sealed record DocumentBindingResolveResult(
    // Serialized JSON shape depends on Kind. record-field returns
    //   { text, type, rawValue }. aql-table returns the full QueryResult
    //   wire shape.
    string ResolvedValueJsonb,
    // Optional updated label suggestion (e.g. "Field: customer.name"
    // for record-field, "AQL: SELECT * FROM Records" for aql-table).
    // Endpoint stamps this onto the row if the row's existing label is
    // null; otherwise the user's label wins.
    string? SuggestedLabel);

/// <summary>
/// Thrown by resolvers for callers' failures (bad config, missing
/// referenced row, no permission). Endpoint layer maps to HTTP 4xx.
/// </summary>
public sealed class DocumentBindingResolveException : Exception
{
    public int StatusCode { get; }

    public DocumentBindingResolveException(string message, int statusCode = 400)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
