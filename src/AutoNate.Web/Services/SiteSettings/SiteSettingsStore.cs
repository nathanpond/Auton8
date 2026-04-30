using System.Text.Json;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using SiteSettingEntity = AutoNate.Web.Persistence.Scaffolded.SiteSetting;

namespace AutoNate.Web.Services.SiteSettings;

// Reads / writes site settings as JsonElement values. Callers that need a
// strongly-typed result use the convenience helpers (GetBoolAsync etc.) which
// fall back to the registry's declared default when the row is missing.
public interface ISiteSettingsStore
{
    Task<IReadOnlyDictionary<string, JsonElement>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default);

    Task ApplyUpdatesAsync(
        IReadOnlyDictionary<string, JsonElement> updates,
        Guid actorId,
        CancellationToken cancellationToken = default);
}

public sealed class EfCoreSiteSettingsStore(IDbContextFactory<AutoNateDbContext> dbContextFactory)
    : ISiteSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.SiteSettings.AsNoTracking().ToListAsync(cancellationToken);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        // Start with the registry defaults so every declared setting is
        // guaranteed to be present in the result, even if the row was never
        // written. Persisted rows then overlay on top.
        foreach (var definition in SiteSettingsRegistry.All)
        {
            result[definition.Key] = definition.DefaultValue.Clone();
        }
        foreach (var row in rows)
        {
            if (TryParseJsonElement(row.ValueJson, out var element))
            {
                result[row.Key] = element;
            }
        }
        return result;
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = SiteSettingsRegistry.Find(key)
            ?? throw new InvalidOperationException($"Unknown site setting '{key}'.");
        if (definition.Type != SettingType.Bool)
        {
            throw new InvalidOperationException($"Setting '{key}' is not a boolean.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await dbContext.SiteSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (row is not null && TryParseJsonElement(row.ValueJson, out var element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }
        return definition.DefaultValue.GetBoolean();
    }

    public async Task ApplyUpdatesAsync(
        IReadOnlyDictionary<string, JsonElement> updates,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        // Load every row up front and key in memory. The settings table is
        // bounded by the registry size (single-digit rows in practice), so
        // pulling everything sidesteps EF's `keys.Contains(s.Key)` translation
        // and is cheaper than the round-trip avoidance would otherwise gain.
        var rows = await dbContext.SiteSettings.ToListAsync(cancellationToken);
        var existingByKey = rows.ToDictionary(s => s.Key, StringComparer.Ordinal);

        foreach (var (key, value) in updates)
        {
            var json = JsonSerializer.Serialize(value, SerializerOptions);
            if (existingByKey.TryGetValue(key, out var row))
            {
                row.ValueJson = json;
                row.UpdatedAtUtc = now;
                row.UpdatedBy = actorId;
            }
            else
            {
                dbContext.SiteSettings.Add(new SiteSettingEntity
                {
                    Key = key,
                    ValueJson = json,
                    UpdatedAtUtc = now,
                    UpdatedBy = actorId
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool TryParseJsonElement(string json, out JsonElement element)
    {
        try
        {
            // Round-trip through a JsonDocument so the returned JsonElement
            // is detached from its (disposed) document and safe to keep.
            using var document = JsonDocument.Parse(json);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }
}
