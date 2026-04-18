using System.Text.Json;
using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Workflow;

public sealed class FileWorkflowDraftStore(IWebHostEnvironment environment) : IWorkflowDraftStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _draftDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "workflows");

    public async Task<WorkflowDraft?> GetMostRecentAsync(CancellationToken cancellationToken = default)
    {
        EnsureDraftDirectory();

        var files = Directory.EnumerateFiles(_draftDirectory, "*.json", SearchOption.TopDirectoryOnly);
        WorkflowDraft? mostRecentDraft = null;

        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            var draft = await JsonSerializer.DeserializeAsync<WorkflowDraft>(stream, SerializerOptions, cancellationToken);
            if (draft is null)
            {
                continue;
            }

            if (mostRecentDraft is null || draft.UpdatedAtUtc > mostRecentDraft.UpdatedAtUtc)
            {
                mostRecentDraft = draft;
            }
        }

        return mostRecentDraft;
    }

    public async Task<WorkflowDraft> SaveAsync(WorkflowDraft draft, CancellationToken cancellationToken = default)
    {
        EnsureDraftDirectory();

        var now = DateTimeOffset.UtcNow;
        var normalizedDraft = draft with
        {
            Id = draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id,
            CreatedAtUtc = draft.CreatedAtUtc == default ? now : draft.CreatedAtUtc,
            UpdatedAtUtc = now
        };

        var path = GetDraftPath(normalizedDraft.Id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, normalizedDraft, SerializerOptions, cancellationToken);

        return normalizedDraft;
    }

    private void EnsureDraftDirectory()
    {
        Directory.CreateDirectory(_draftDirectory);
    }

    private string GetDraftPath(Guid draftId)
    {
        return Path.Combine(_draftDirectory, $"{draftId:N}.json");
    }
}
