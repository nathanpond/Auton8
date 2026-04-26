using System.Text.Json;

namespace AutoNate.Web.Models.Authorization;

public sealed record class EntityEdge
{
    public Guid Id { get; init; }

    public string EdgeKind { get; init; } = string.Empty;

    public string FromKind { get; init; } = string.Empty;

    public string FromId { get; init; } = string.Empty;

    public string ToKind { get; init; } = string.Empty;

    public string ToId { get; init; } = string.Empty;

    public JsonElement Data { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }
}
