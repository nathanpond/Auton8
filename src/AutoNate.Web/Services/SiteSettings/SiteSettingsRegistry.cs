using System.Collections.ObjectModel;
using System.Text.Json;

namespace AutoNate.Web.Services.SiteSettings;

// Site settings are persisted as a sparse key→JSON map in the `site_settings`
// table. The registry is the typed schema for that table: every setting the
// admin can change is declared here once, and storage / endpoints / UI all
// derive their behavior from these definitions.
//
// Adding a new feature flag or option:
//   1. Append a SettingDefinition to All below (pick the right group).
//   2. (Optional) read the value through ISiteSettingsStore on the backend or
//      through the public /api/site-settings endpoint on the SPA.
//   3. The General/Features admin pages render any setting whose Group matches.
public enum SettingType
{
    Bool,
    String,
    Int
}

public enum SettingGroup
{
    General,
    Features
}

public sealed record class SettingDefinition(
    string Key,
    SettingType Type,
    SettingGroup Group,
    string Label,
    string Description,
    JsonElement DefaultValue,
    bool IsPublic);

public static class SiteSettingsKeys
{
    // Whether the bell icon and notification dropdown render in the site
    // header. Off hides the bell entirely (and the live websocket subscription
    // is skipped), but the underlying notifications API stays reachable.
    public const string NotificationsHeaderEnabled = "notifications.headerEnabled";
}

public static class SiteSettingsRegistry
{
    // Pre-parsed JSON literal for boolean true so we can hand back a real
    // JsonElement without each definition having to allocate a JsonDocument.
    private static readonly JsonElement TrueElement = JsonDocument.Parse("true").RootElement.Clone();

    public static readonly IReadOnlyList<SettingDefinition> All = new ReadOnlyCollection<SettingDefinition>(
    [
        new SettingDefinition(
            Key: SiteSettingsKeys.NotificationsHeaderEnabled,
            Type: SettingType.Bool,
            Group: SettingGroup.General,
            Label: "Show notifications in header",
            Description: "When enabled, the bell icon and notifications dropdown appear in the site header.",
            DefaultValue: TrueElement,
            IsPublic: true)
    ]);

    private static readonly Dictionary<string, SettingDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static SettingDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var def) ? def : null;

    public static IEnumerable<SettingDefinition> ForGroup(SettingGroup group) =>
        All.Where(d => d.Group == group);

    public static IEnumerable<SettingDefinition> Public => All.Where(d => d.IsPublic);

    // Validates and normalizes an incoming JSON value against the setting's
    // declared type. Throws SiteSettingsValidationException on mismatch so the
    // endpoint can surface a clean 400.
    public static JsonElement ValidateValue(SettingDefinition definition, JsonElement raw)
    {
        switch (definition.Type)
        {
            case SettingType.Bool:
                if (raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new SiteSettingsValidationException(
                        $"Setting '{definition.Key}' expects a boolean.");
                }
                return raw;
            case SettingType.String:
                if (raw.ValueKind != JsonValueKind.String)
                {
                    throw new SiteSettingsValidationException(
                        $"Setting '{definition.Key}' expects a string.");
                }
                return raw;
            case SettingType.Int:
                if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out _))
                {
                    throw new SiteSettingsValidationException(
                        $"Setting '{definition.Key}' expects an integer.");
                }
                return raw;
            default:
                throw new SiteSettingsValidationException(
                    $"Unsupported setting type for '{definition.Key}'.");
        }
    }
}

public sealed class SiteSettingsValidationException(string message) : Exception(message);
