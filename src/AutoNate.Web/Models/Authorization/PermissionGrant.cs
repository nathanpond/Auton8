using System.Text.Json;

namespace AutoNate.Web.Models.Authorization;

public sealed record class PermissionGrant
{
    public Guid Id { get; init; }

    public string PrincipalKind { get; init; } = string.Empty;

    public string PrincipalId { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string SelectorString { get; init; } = string.Empty;

    public JsonElement SelectorAst { get; init; }

    public string Effect { get; init; } = "allow";

    public int Priority { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }
}
