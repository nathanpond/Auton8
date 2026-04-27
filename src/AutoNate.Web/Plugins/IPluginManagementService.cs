using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Plugins;

public sealed record PluginListItem(
    Guid Id,
    string Name,
    string Version,
    PluginStatus Status,
    DateTime UploadedAt,
    Guid UploadedBy,
    DateTime? LastEnabledAt,
    DateTime? LastDisabledAt,
    string? LastError);

public sealed record PluginUploadOutcome(
    bool Success,
    PluginListItem? Plugin,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record PluginActionOutcome(
    bool Success,
    PluginListItem? Plugin,
    string? ErrorCode,
    string? ErrorMessage);

public interface IPluginManagementService
{
    Task<IReadOnlyList<PluginListItem>> ListAsync(CancellationToken ct = default);
    Task<PluginListItem?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PluginUploadOutcome> UploadAsync(Stream zipStream, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> EnableAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> DisableAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
}
