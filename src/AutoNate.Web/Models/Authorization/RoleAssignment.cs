using System.Text.Json;

namespace AutoNate.Web.Models.Authorization;

public sealed record class RoleAssignment
{
    public Guid Id { get; init; }

    public Guid RoleId { get; init; }

    public string PrincipalKind { get; init; } = string.Empty;

    public string PrincipalId { get; init; } = string.Empty;

    public string? ScopeString { get; init; }

    public JsonElement? ScopeAst { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }
}
