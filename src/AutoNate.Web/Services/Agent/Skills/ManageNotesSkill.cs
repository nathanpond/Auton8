using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 3 notes-write skill. Mirrors the lookup/manage split established by
// ManageRecordsSkill: every tool follows the ConfirmGate dry-run/commit
// envelope and every commit routes through IContentAuthorizer for the
// parent-Edit check the corresponding ContentPageEndpoints / NotebookEndpoints
// / ProjectEndpoints enforce on REST writes.
//
// Page body edits on already-existing pages are intentionally OUT of scope:
// /api/content/pages PATCH rejects bodyJsonb writes (YjsManagedContentGuard)
// because the body lives in the Hocuspocus / Yjs collab session. The agent
// can still create pages from markdown end-to-end (this skill, before any
// editor opens) and can mutate an open page via NotesPage's page-action
// channel (replace_blocks_from_markdown, append_blocks_from_markdown — added
// in the Phase 3 NotesPage page-context provider).
public sealed class ManageNotesSkill : IAgentSkill
{
    public string Name => "manage-notes";

    public string Description =>
        "Create projects, notebooks, and pages (from markdown); rename / move / archive existing nodes. Page body edits on existing pages go through the NotesPage page-action handler — this skill only handles creation and metadata.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageNotesSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "create_page_from_markdown",
                Description: "Create a new page in a notebook from a markdown body. ALWAYS call with confirmed=false first; commit with confirmed=true after explicit user approval. Markdown is rendered to BlockNote blocks (paragraphs, headings, lists, code, quotes, links) — see MarkdownToBlockNoteConverter for the supported subset.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "notebookId": { "type": "string", "description": "Notebook GUID that will own the new page." },
                        "parentPageId": { "type": ["string", "null"], "description": "Optional parent page GUID to nest under." },
                        "title": { "type": "string", "description": "Page title." },
                        "markdown": { "type": "string", "description": "Markdown body for the new page." },
                        "sortOrder": { "type": ["integer", "null"], "description": "Optional position among siblings." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["notebookId", "title", "markdown"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreatePageAsync),

            new AgentTool(
                Name: "update_page",
                Description: "Patch a page's metadata: title, parent, sort order, archived flag. Body content is Yjs-managed and rejected; use the NotesPage page-action channel to edit body content.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "pageId": { "type": "string", "description": "Page GUID." },
                        "title": { "type": ["string", "null"], "description": "New title; omit to keep current." },
                        "parentPageId": { "type": ["string", "null"], "description": "New parent page GUID, or null to move to notebook root." },
                        "sortOrder": { "type": ["integer", "null"], "description": "New position." },
                        "isArchived": { "type": ["boolean", "null"], "description": "Archive (true) or restore (false)." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["pageId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdatePageAsync),

            new AgentTool(
                Name: "create_notebook",
                Description: "Create a new notebook inside a cabinet.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "cabinetId": { "type": "string", "description": "Cabinet GUID." },
                        "name": { "type": "string", "description": "Notebook name." },
                        "description": { "type": ["string", "null"] },
                        "icon": { "type": ["string", "null"] },
                        "sortOrder": { "type": ["integer", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["cabinetId", "name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateNotebookAsync),

            new AgentTool(
                Name: "create_project",
                Description: "Create a new project. Any authenticated user can create one; the caller becomes Owner in the same transaction.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string", "description": "Project name." },
                        "description": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateProjectAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Notes write protocol: " +
        "(1) Use lookup-notes to find the parent notebook / project / page id before creating. " +
        "(2) For page bodies, prefer create_page_from_markdown — emit clean markdown (headings, lists, code blocks, links) and the converter will render it as BlockNote blocks. " +
        "(3) Existing pages are Yjs-managed; editing the BODY of an open page must go through apply_page_action (the NotesPage handler). update_page only mutates metadata. " +
        "(4) Always confirm with the user before re-calling with confirmed=true.";

    // ---- create_page_from_markdown ---------------------------------------

    private static async Task<JsonElement> InvokeCreatePageAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "create_page";

        if (!TryReadGuid(args, "notebookId", out var notebookId))
        {
            return ConfirmGate.Rejected(action, "notebookId is required and must be a GUID.");
        }
        Guid? parentPageId = TryReadGuid(args, "parentPageId", out var pp) ? pp : null;
        var title = ReadString(args, "title");
        var markdown = ReadString(args, "markdown");
        if (string.IsNullOrWhiteSpace(title))
        {
            return ConfirmGate.Rejected(action, "title is required.");
        }
        if (markdown is null)
        {
            return ConfirmGate.Rejected(action, "markdown is required (empty string is allowed for a blank page).");
        }
        int? sortOrder = args.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number
            ? so.GetInt32()
            : null;

        // Authorization parity with ContentPageEndpoints.MapPost: Edit on the
        // parent notebook is required; if parentPageId is supplied, Edit on
        // that page is also required.
        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var notebookDecision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Notebook, notebookId, Actions.Edit, ct);
        if (!notebookDecision.IsAllowed)
        {
            return ConfirmGate.Rejected(action, $"Edit permission required on notebook {notebookId}.");
        }
        if (parentPageId is { } parent)
        {
            var parentDecision = await authorizer.AuthorizeAsync(
                context.Session.User, ContentKinds.Page, parent, Actions.Edit, ct);
            if (!parentDecision.IsAllowed)
            {
                return ConfirmGate.Rejected(action, $"Edit permission required on parent page {parent}.");
            }
        }

        // Cheap preview the model narrates: title + word-ish count + a snippet.
        var previewSnippet = markdown.Length > 200 ? markdown.Substring(0, 200) + "…" : markdown;
        var bodyByteEstimate = System.Text.Encoding.UTF8.GetByteCount(markdown);
        var preview = new
        {
            notebookId,
            parentPageId,
            title = title.Trim(),
            markdownByteCount = bodyByteEstimate,
            markdownSnippet = previewSnippet,
            sortOrder
        };

        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("note_create_proposal", action, preview);
        }

        // Commit: convert markdown to BlockNote JSON, insert page + initial
        // version row + closure-ancestors row inside one transaction.
        var converter = context.Services.GetRequiredService<IMarkdownToBlockNoteConverter>();
        var bodyJson = converter.Convert(markdown);
        var bodyJsonb = bodyJson.GetRawText();

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = context.Services.GetRequiredService<IContentTreeService>();
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var notebookExists = await db.Notebooks.AsNoTracking().AnyAsync(n => n.Id == notebookId, ct);
        if (!notebookExists)
        {
            return ConfirmGate.Failed("note_create_failed", action, $"Notebook {notebookId} not found.");
        }
        if (parentPageId is { } parentCheck)
        {
            var parentInNotebook = await db.Pages.AsNoTracking()
                .AnyAsync(p => p.Id == parentCheck && p.NotebookId == notebookId, ct);
            if (!parentInNotebook)
            {
                return ConfirmGate.Failed("note_create_failed", action, $"Parent page {parentCheck} is not in notebook {notebookId}.");
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var actorId = context.Session.UserId;
        var page = new Page
        {
            Id = Guid.NewGuid(),
            NotebookId = notebookId,
            ParentPageId = parentPageId,
            Title = title.Trim(),
            BodyJsonb = bodyJsonb,
            CurrentVersionNumber = 2,
            SortOrder = sortOrder ?? 0,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.Pages.Add(page);
        db.PageVersions.Add(new PageVersion
        {
            Id = Guid.NewGuid(),
            PageId = page.Id,
            VersionNumber = 1,
            Title = page.Title,
            BodyJsonb = page.BodyJsonb,
            Kind = ContentVersionKinds.Manual,
            Note = "initial (chatbot)",
            CreatedAtUtc = now,
            CreatedBy = actorId
        });
        await db.SaveChangesAsync(ct);
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Page, page.Id, ct);
        await tx.CommitAsync(ct);

        await auditPublisher.PublishAsync(
            ContentEventTopic.TopicName,
            ContentEventTypes.PageCreated,
            ContentResourceKinds.Page,
            resource: new { id = page.Id, notebookId = page.NotebookId, parentPageId = page.ParentPageId, title = page.Title },
            details: new { source = "chatbot" },
            ct);

        return ConfirmGate.Committed("note_create_committed", action, new
        {
            id = page.Id,
            notebookId = page.NotebookId,
            parentPageId = page.ParentPageId,
            title = page.Title,
            currentVersionNumber = page.CurrentVersionNumber,
            linkPath = $"/notes/{page.Id}"
        });
    }

    // ---- update_page (metadata only) -------------------------------------

    private static async Task<JsonElement> InvokeUpdatePageAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "update_page";
        if (!TryReadGuid(args, "pageId", out var pageId))
        {
            return ConfirmGate.Rejected(action, "pageId is required and must be a GUID.");
        }

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Page, pageId, Actions.Edit, ct);
        if (!decision.IsAllowed)
        {
            return ConfirmGate.Rejected(action, $"Edit permission required on page {pageId}.");
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null)
        {
            return ConfirmGate.Rejected(action, $"Page {pageId} not found.");
        }

        // Materialize the patch into a typed shape so the proposal envelope and
        // the commit branch use the same field set.
        string? newTitle = ReadString(args, "title")?.Trim();
        bool moveProvided = args.TryGetProperty("parentPageId", out var ppElem);
        Guid? newParentPageId = ppElem.ValueKind == JsonValueKind.String
            && Guid.TryParse(ppElem.GetString(), out var pp) ? pp : (Guid?)null;
        bool moveToRoot = moveProvided && ppElem.ValueKind == JsonValueKind.Null;
        int? newSortOrder = args.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number
            ? so.GetInt32() : null;
        bool? newArchived = args.TryGetProperty("isArchived", out var ia)
            && (ia.ValueKind == JsonValueKind.True || ia.ValueKind == JsonValueKind.False)
            ? ia.GetBoolean() : null;

        var fieldChanges = new List<object>();
        if (newTitle is not null && page.Title != newTitle)
        {
            fieldChanges.Add(new { key = "title", before = page.Title, after = newTitle });
        }
        if (moveProvided && (moveToRoot ? page.ParentPageId is not null : newParentPageId != page.ParentPageId))
        {
            fieldChanges.Add(new
            {
                key = "parentPageId",
                before = page.ParentPageId,
                after = moveToRoot ? (Guid?)null : newParentPageId
            });
        }
        if (newSortOrder is { } so2 && page.SortOrder != so2)
        {
            fieldChanges.Add(new { key = "sortOrder", before = page.SortOrder, after = so2 });
        }
        if (newArchived is { } a && page.IsArchived != a)
        {
            fieldChanges.Add(new { key = "isArchived", before = page.IsArchived, after = a });
        }

        if (fieldChanges.Count == 0)
        {
            return ConfirmGate.Rejected(action, "No metadata fields differ — nothing to change.");
        }

        var preview = new { pageId, title = page.Title, fieldChanges };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("note_update_proposal", action, preview);
        }

        // Commit. Move triggers a closure rebuild (mirrors ContentPageEndpoints).
        var treeService = context.Services.GetRequiredService<IContentTreeService>();
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();

        var actorId = context.Session.UserId;
        var wasMoved = moveProvided
            && (moveToRoot ? page.ParentPageId is not null : newParentPageId != page.ParentPageId);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (newTitle is not null) page.Title = newTitle;
        if (moveProvided) page.ParentPageId = moveToRoot ? null : newParentPageId;
        if (newSortOrder is { } so3) page.SortOrder = so3;
        string? archiveEventType = null;
        if (newArchived is { } a2 && page.IsArchived != a2)
        {
            page.IsArchived = a2;
            archiveEventType = a2 ? ContentEventTypes.PageArchived : ContentEventTypes.PageRestored;
        }
        page.UpdatedAtUtc = DateTime.UtcNow;
        page.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);
        if (wasMoved)
        {
            await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Page, page.Id, ct);
        }
        await tx.CommitAsync(ct);

        await auditPublisher.PublishAsync(
            ContentEventTopic.TopicName,
            archiveEventType ?? ContentEventTypes.PageUpdated,
            ContentResourceKinds.Page,
            resource: new { id = page.Id, title = page.Title },
            details: new { source = "chatbot", fieldChanges },
            ct);

        return ConfirmGate.Committed("note_update_committed", action, new
        {
            id = page.Id,
            title = page.Title,
            parentPageId = page.ParentPageId,
            sortOrder = page.SortOrder,
            isArchived = page.IsArchived
        });
    }

    // ---- create_notebook -------------------------------------------------

    private static async Task<JsonElement> InvokeCreateNotebookAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "create_notebook";
        if (!TryReadGuid(args, "cabinetId", out var cabinetId))
        {
            return ConfirmGate.Rejected(action, "cabinetId is required and must be a GUID.");
        }
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return ConfirmGate.Rejected(action, "name is required.");
        }
        var description = ReadString(args, "description");
        var icon = ReadString(args, "icon");
        int? sortOrder = args.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number
            ? so.GetInt32() : null;

        var authorizer = context.Services.GetRequiredService<IContentAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            context.Session.User, ContentKinds.Cabinet, cabinetId, Actions.Edit, ct);
        if (!decision.IsAllowed)
        {
            return ConfirmGate.Rejected(action, $"Edit permission required on cabinet {cabinetId}.");
        }

        var preview = new { cabinetId, name = name.Trim(), description, icon, sortOrder };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("notebook_create_proposal", action, preview);
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = context.Services.GetRequiredService<IContentTreeService>();
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();
        var actorId = context.Session.UserId;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cabinetExists = await db.Cabinets.AsNoTracking().AnyAsync(c => c.Id == cabinetId, ct);
        if (!cabinetExists)
        {
            return ConfirmGate.Failed("notebook_create_failed", action, $"Cabinet {cabinetId} not found.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var notebook = new Notebook
        {
            Id = Guid.NewGuid(),
            CabinetId = cabinetId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(),
            SortOrder = sortOrder ?? 0,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync(ct);
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Notebook, notebook.Id, ct);
        await tx.CommitAsync(ct);

        await auditPublisher.PublishAsync(
            ContentEventTopic.TopicName,
            ContentEventTypes.NotebookCreated,
            ContentResourceKinds.Notebook,
            resource: new { id = notebook.Id, cabinetId = notebook.CabinetId, name = notebook.Name },
            details: new { source = "chatbot" },
            ct);

        return ConfirmGate.Committed("notebook_create_committed", action, new
        {
            id = notebook.Id,
            cabinetId = notebook.CabinetId,
            name = notebook.Name
        });
    }

    // ---- create_project --------------------------------------------------

    private static async Task<JsonElement> InvokeCreateProjectAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        const string action = "create_project";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return ConfirmGate.Rejected(action, "name is required.");
        }
        var description = ReadString(args, "description");

        var preview = new { name = name.Trim(), description };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("project_create_proposal", action, preview);
        }

        var dbFactory = context.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var memberships = context.Services.GetRequiredService<IProjectMembershipService>();
        var treeService = context.Services.GetRequiredService<IContentTreeService>();
        var auditPublisher = context.Services.GetRequiredService<IAuditEventPublisher>();
        var actorId = context.Session.UserId;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DeletionsLocked = false,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.Projects.Add(project);
        await memberships.AddOwnerOnCreateAsync(db, project.Id, actorId, now, ct);
        await db.SaveChangesAsync(ct);
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, ct);
        await tx.CommitAsync(ct);

        await auditPublisher.PublishAsync(
            ContentEventTopic.TopicName,
            ContentEventTypes.ProjectCreated,
            ContentResourceKinds.Project,
            resource: new { id = project.Id, name = project.Name },
            details: new { source = "chatbot" },
            ct);

        return ConfirmGate.Committed("project_create_committed", action, new
        {
            id = project.Id,
            name = project.Name
        });
    }

    // ---- helpers ---------------------------------------------------------

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return Guid.TryParse(v.GetString(), out id);
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
