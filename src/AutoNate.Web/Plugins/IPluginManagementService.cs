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
    // Replace an existing plugin's files with a new upload. Preserves the
    // plugin's id, schema code, DB role, and plg_<code> data; updates the
    // manifest fields (name/version/entryAssembly/entryType). If the plugin
    // was enabled before the call, it is re-enabled after the file swap so
    // migrations run and Configure() re-registers menus/templates.
    Task<PluginUploadOutcome> UpdateAsync(Guid id, Stream zipStream, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> EnableAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> DisableAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<PluginActionOutcome> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
}
