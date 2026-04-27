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
}
