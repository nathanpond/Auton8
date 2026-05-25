using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only notes-hierarchy diagnostics (Project → Cabinet → Notebook → Page → Note).
// Every list query filters through IContentAuthorizer.GetAllowedIdsAsync so the
// agent never sees entities the calling principal isn't entitled to see; per-id
// reads use AuthorizeAsync. Notes themselves are gated through their parent
// page per design D10 (see NoteEndpoints).
public sealed class LookupNotesSkill : IAgentSkill
{
    public string Name => "lookup-notes";

    public string Description =>
        "Browse the notes hierarchy: projects, cabinets, notebooks, pages, and notes. Read-only.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupNotesSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_projects",
                Description: "List projects the user can view. Optional free-text filter on name/description.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Optional free text matched against name and description." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100, "description": "Max rows. Defaults to 25." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListProjectsAsync),

            new AgentTool(
                Name: "get_project",
                Description: "Fetch a single project by id.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string", "description": "Project GUID." }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetProjectAsync),

            new AgentTool(
                Name: "list_cabinets",
                Description: "List cabinets the user can view. Filter by projectId to scope to one project.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "projectId": { "type": "string", "description": "Optional project GUID to restrict the list." },
                        "query": { "type": "string", "description": "Optional free text matched against name and description." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListCabinetsAsync),

            new AgentTool(
                Name: "list_notebooks",
                Description: "List notebooks the user can view. Filter by cabinetId to scope to one cabinet.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "cabinetId": { "type": "string", "description": "Optional cabinet GUID to restrict the list." },
                        "query": { "type": "string", "description": "Optional free text matched against name and description." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListNotebooksAsync),

            new AgentTool(
                Name: "list_pages",
                Description: "List pages in a notebook (tree-shaped). Pages outside the notebook are not returned.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "notebookId": { "type": "string", "description": "Notebook GUID whose pages to list." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 200 }
                      },
                      "required": ["notebookId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListPagesAsync),

            new AgentTool(
                Name: "get_page",
                Description: "Fetch a single page by id. Returns metadata only — call list_notes_on_page to see attached notes.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string", "description": "Page GUID." }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetPageAsync),

            new AgentTool(
                Name: "list_notes_on_page",
                Description: "List notes attached to a page. Authorization is inherited from the page.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "pageId": { "type": "string", "description": "Page GUID." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "required": ["pageId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListNotesAsync),

            new AgentTool(
                Name: "find_page",
                Description: "Search pages across notebooks by title. Returns up to 25 matches the user can view.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Free text matched against page title." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 50 }
                      },
                      "required": ["query"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeFindPageAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Notes hierarchy is Project → Cabinet → Notebook → Page → Note. Start with list_projects or find_page to locate the user's content, then drill down. Note: pages are the permissionable boundary — notes inherit access from their parent page.";

    private static async Task<JsonElement> InvokeListProjectsAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var take = ReadTake(args, 25, 100);
        var query = ReadString(args, "query");

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var access = await authorizer.GetAllowedIdsAsync(
            context.Session.User, ContentKinds.Project, Actions.View, ct);

        var projects = db.Projects.AsNoTracking().AsQueryable();
        if (!access.Unrestricted)
        {
            var ids = access.AllowedIds;
            projects = projects.Where(p => ids.Contains(p.Id));
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            projects = projects.Where(p =>
                EF.Functions.ILike(p.Name, "%" + needle + "%") ||
                (p.Description != null && EF.Functions.ILike(p.Description, "%" + needle + "%")));
        }

        var items = await projects
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Take(take)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                isArchived = p.IsArchived,
                updatedAtUtc = p.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "projects",
            source = "AutoNateDbContext.Projects",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetProjectAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
        {
            return Error("get_project", "id is required and must be a GUID.");
        }

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Project, id, Actions.View, ct);
        if (!decision.IsAllowed)
        {
            return Error("get_project", $"Project '{id}' not visible to current user.");
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                isArchived = p.IsArchived,
                deletionsLocked = p.DeletionsLocked,
                createdAtUtc = p.CreatedAtUtc,
                updatedAtUtc = p.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(ct);
        if (project is null)
        {
            return Error("get_project", $"No project with id '{id}'.");
        }
        return JsonSerializer.SerializeToElement(new
        {
            kind = "project",
            source = "AutoNateDbContext.Projects",
            data = project
        });
    }

    private static async Task<JsonElement> InvokeListCabinetsAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var take = ReadTake(args, 50, 100);
        var query = ReadString(args, "query");
        Guid? projectId = TryReadGuid(args, "projectId", out var pid) ? pid : null;

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var access = await authorizer.GetAllowedIdsAsync(
            context.Session.User, ContentKinds.Cabinet, Actions.View, ct);

        var cabinets = db.Cabinets.AsNoTracking().AsQueryable();
        if (projectId is { } pidv) cabinets = cabinets.Where(c => c.ProjectId == pidv);
        if (!access.Unrestricted)
        {
            var ids = access.AllowedIds;
            cabinets = cabinets.Where(c => ids.Contains(c.Id));
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            cabinets = cabinets.Where(c =>
                EF.Functions.ILike(c.Name, "%" + needle + "%") ||
                (c.Description != null && EF.Functions.ILike(c.Description, "%" + needle + "%")));
        }

        var items = await cabinets
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Take(take)
            .Select(c => new
            {
                id = c.Id,
                projectId = c.ProjectId,
                name = c.Name,
                description = c.Description,
                isArchived = c.IsArchived,
                updatedAtUtc = c.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "cabinets",
            source = "AutoNateDbContext.Cabinets",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeListNotebooksAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var take = ReadTake(args, 50, 100);
        var query = ReadString(args, "query");
        Guid? cabinetId = TryReadGuid(args, "cabinetId", out var cid) ? cid : null;

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var access = await authorizer.GetAllowedIdsAsync(
            context.Session.User, ContentKinds.Notebook, Actions.View, ct);

        var notebooks = db.Notebooks.AsNoTracking().AsQueryable();
        if (cabinetId is { } cidv) notebooks = notebooks.Where(n => n.CabinetId == cidv);
        if (!access.Unrestricted)
        {
            var ids = access.AllowedIds;
            notebooks = notebooks.Where(n => ids.Contains(n.Id));
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            notebooks = notebooks.Where(n =>
                EF.Functions.ILike(n.Name, "%" + needle + "%") ||
                (n.Description != null && EF.Functions.ILike(n.Description, "%" + needle + "%")));
        }

        var items = await notebooks
            .OrderBy(n => n.SortOrder).ThenBy(n => n.Name)
            .Take(take)
            .Select(n => new
            {
                id = n.Id,
                cabinetId = n.CabinetId,
                name = n.Name,
                description = n.Description,
                isArchived = n.IsArchived,
                updatedAtUtc = n.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "notebooks",
            source = "AutoNateDbContext.Notebooks",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeListPagesAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!TryReadGuid(args, "notebookId", out var notebookId))
        {
            return Error("list_pages", "notebookId is required and must be a GUID.");
        }
        var take = ReadTake(args, 50, 200);

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var notebookDecision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Notebook, notebookId, Actions.View, ct);
        if (!notebookDecision.IsAllowed)
        {
            return Error("list_pages", $"Notebook '{notebookId}' not visible to current user.");
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var access = await authorizer.GetAllowedIdsAsync(
            context.Session.User, ContentKinds.Page, Actions.View, ct);

        var pages = db.Pages.AsNoTracking().Where(p => p.NotebookId == notebookId);
        if (!access.Unrestricted)
        {
            var ids = access.AllowedIds;
            pages = pages.Where(p => ids.Contains(p.Id));
        }

        var items = await pages
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .Take(take)
            .Select(p => new
            {
                id = p.Id,
                notebookId = p.NotebookId,
                parentPageId = p.ParentPageId,
                title = p.Title,
                sortOrder = p.SortOrder,
                isArchived = p.IsArchived,
                currentVersionNumber = p.CurrentVersionNumber,
                updatedAtUtc = p.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "pages",
            source = "AutoNateDbContext.Pages",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetPageAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
        {
            return Error("get_page", "id is required and must be a GUID.");
        }

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Page, id, Actions.View, ct);
        if (!decision.IsAllowed)
        {
            return Error("get_page", $"Page '{id}' not visible to current user.");
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                id = p.Id,
                notebookId = p.NotebookId,
                parentPageId = p.ParentPageId,
                title = p.Title,
                sortOrder = p.SortOrder,
                isArchived = p.IsArchived,
                currentVersionNumber = p.CurrentVersionNumber,
                createdAtUtc = p.CreatedAtUtc,
                updatedAtUtc = p.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(ct);
        if (page is null)
        {
            return Error("get_page", $"No page with id '{id}'.");
        }
        return JsonSerializer.SerializeToElement(new
        {
            kind = "page",
            source = "AutoNateDbContext.Pages",
            data = page
        });
    }

    private static async Task<JsonElement> InvokeListNotesAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!TryReadGuid(args, "pageId", out var pageId))
        {
            return Error("list_notes_on_page", "pageId is required and must be a GUID.");
        }
        var take = ReadTake(args, 50, 100);

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Page, pageId, Actions.View, ct);
        if (!decision.IsAllowed)
        {
            return Error("list_notes_on_page", $"Page '{pageId}' not visible to current user.");
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.PageId == pageId)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedAtUtc)
            .Take(take)
            .Select(n => new
            {
                id = n.Id,
                pageId = n.PageId,
                noteKind = n.NoteKind,
                title = n.Title,
                isArchived = n.IsArchived,
                currentVersionNumber = n.CurrentVersionNumber,
                updatedAtUtc = n.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "notes",
            source = "AutoNateDbContext.Notes",
            data = notes
        });
    }

    private static async Task<JsonElement> InvokeFindPageAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var query = ReadString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("find_page", "query is required.");
        }
        var take = ReadTake(args, 25, 50);

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var access = await authorizer.GetAllowedIdsAsync(
            context.Session.User, ContentKinds.Page, Actions.View, ct);

        var pages = db.Pages.AsNoTracking().AsQueryable();
        if (!access.Unrestricted)
        {
            var ids = access.AllowedIds;
            pages = pages.Where(p => ids.Contains(p.Id));
        }
        var needle = query.Trim();
        pages = pages.Where(p => EF.Functions.ILike(p.Title, "%" + needle + "%"));

        var items = await pages
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Take(take)
            .Select(p => new
            {
                id = p.Id,
                notebookId = p.NotebookId,
                parentPageId = p.ParentPageId,
                title = p.Title,
                isArchived = p.IsArchived,
                updatedAtUtc = p.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "page_search_results",
            source = "AutoNateDbContext.Pages",
            data = items
        });
    }

    private static int ReadTake(JsonElement args, int defaultValue, int max) =>
        args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, max)
            : defaultValue;

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return Guid.TryParse(v.GetString(), out id);
    }

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
