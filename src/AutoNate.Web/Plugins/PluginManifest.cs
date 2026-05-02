using System.Text.Json.Serialization;

namespace AutoNate.Web.Plugins;

// Shape of plugin.json at the root of a plugin zip / extracted plugin folder.
// Manifest validation happens at upload time; loader trusts the persisted row.
public sealed record class PluginManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("entryAssembly")]
    public required string EntryAssembly { get; init; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; init; }

    // Per-page-template presentation metadata used when ingesting
    // PageTemplates/*.template files into public.page_templates. Keys match
    // each template file's stem (e.g. "AuditLog" for AuditLog.template). All
    // metadata is optional — entries the manifest doesn't describe still get
    // a row, just without a category/description hint for the picker.
    [JsonPropertyName("templates")]
    public Dictionary<string, PluginManifestTemplate>? Templates { get; init; }
}

public sealed record class PluginManifestTemplate
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }
}
