using System.Text.Json;
using Microsoft.Playwright;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Thin wrapper around a signed-in <see cref="IAPIRequestContext"/> for
/// creating prerequisite data fast (much faster than driving the UI). Use it
/// when a test's goal is to verify a UI behavior on top of seeded data, not to
/// verify the create flow itself. Each helper returns a strongly-typed result
/// parsed from the endpoint response.
///
/// Methods are added incrementally as test phases need them — the per-phase
/// build-out lives in the comprehensive plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>).
/// </summary>
public sealed class ApiSeeder
{
    private readonly IAPIRequestContext _request;

    public ApiSeeder(IAPIRequestContext request) => _request = request;

    /// <summary>
    /// POST /api/record-types/ — creates a record type. Required by every
    /// records-related test as the precondition for creating records.
    /// </summary>
    public async Task<RecordTypeDto> CreateRecordTypeAsync(
        string shortCode,
        string name,
        string? description = null,
        string? icon = null,
        string? color = null)
    {
        var response = await _request.PostAsync("/api/record-types/", new APIRequestContextOptions
        {
            DataObject = new
            {
                shortCode,
                name,
                description,
                icon,
                color
            }
        });

        await EnsureSuccessAsync(response, "create record type");
        var json = await response.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/record-types/.");

        return new RecordTypeDto(
            Id: json.GetProperty("id").GetGuid(),
            ShortCode: json.GetProperty("shortCode").GetString()!,
            Name: json.GetProperty("name").GetString()!);
    }

    private static async Task EnsureSuccessAsync(IAPIResponse response, string action)
    {
        if (response.Ok) return;

        var body = await SafeReadBodyAsync(response);
        throw new InvalidOperationException(
            $"E2E API seeder failed to {action}: HTTP {response.Status} {response.StatusText}. " +
            $"Body: {body}");
    }

    private static async Task<string> SafeReadBodyAsync(IAPIResponse response)
    {
        try { return await response.TextAsync(); }
        catch { return "<unreadable>"; }
    }
}

/// <summary>
/// Minimal projection of <c>RecordTypeDto</c> from the SPA's perspective —
/// just the fields tests actually use. The full DTO has more shape (icon,
/// color, isSystem, …); add fields here when a test needs them.
/// </summary>
public sealed record RecordTypeDto(Guid Id, string ShortCode, string Name);
