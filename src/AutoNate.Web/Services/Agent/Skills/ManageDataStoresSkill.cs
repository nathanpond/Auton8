using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Confirm-gated mutations for the data-store domain — create / update / delete
// stores plus folder + file metadata operations on FileType stores. CSV ingest
// is deliberately excluded: it needs multipart file upload that doesn't
// translate to a JSON tool call. Users still ingest CSVs via the SPA's
// detail page; the chatbot can guide them there.
public sealed class ManageDataStoresSkill : IAgentSkill
{
    public string Name => "manage-data-stores";

    public string Description =>
        "Create / update / delete data stores and manage folders and files inside FileType stores.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageDataStoresSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "create_data_store",
                Description: "Create a new data store. `kind` is 'FileType' (folders + files) or 'SqlType' (CSV-ingested tables). Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "description": { "type": ["string", "null"] },
                        "kind": { "type": "string", "enum": ["FileType", "SqlType"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name", "kind"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateAsync),

            new AgentTool(
                Name: "update_data_store",
                Description: "Rename or re-describe an existing data store. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": ["string", "null"] },
                        "description": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateAsync),

            new AgentTool(
                Name: "delete_data_store",
                Description: "Delete a data store. For SqlType stores this drops the per-store schema and role; for FileType stores the file metadata rows go too (the on-disk bytes are swept by background cleanup). Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteAsync),

            new AgentTool(
                Name: "create_data_store_folder",
                Description: "Create an empty folder in a FileType data store. Folder paths are POSIX-style with a leading '/'. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folderPath": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "folderPath"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateFolderAsync),

            new AgentTool(
                Name: "delete_data_store_folder",
                Description: "Delete a folder and every file under it in a FileType data store. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "folderPath": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "folderPath"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteFolderAsync),

            new AgentTool(
                Name: "delete_data_store_file",
                Description: "Delete a single file from a FileType data store by its file id. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteFileAsync),

            new AgentTool(
                Name: "rename_or_move_data_store_file",
                Description: "Rename or move a single file. At least one of newFolderPath or newFilename must be set. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dataStoreId": { "type": "string" },
                        "fileId": { "type": "string" },
                        "newFolderPath": { "type": ["string", "null"] },
                        "newFilename": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dataStoreId", "fileId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRenameFileAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Data store mutations are confirm-gated. CSV ingest (POST /tables) needs a binary upload — direct the user to the SPA detail page rather than attempting it from chat. FileType folder paths are POSIX-style with a leading '/' (e.g. '/raw/2026/'). Use lookup-data-stores tools first to find ids.";

    private static async Task<JsonElement> InvokeCreateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_data_store";
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        var kindRaw = ReadString(args, "kind");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        if (string.IsNullOrWhiteSpace(kindRaw)) return ConfirmGate.Rejected(action, "kind is required.");
        if (!Enum.TryParse<DataStoreKind>(kindRaw, ignoreCase: true, out var kind))
            return ConfirmGate.Rejected(action, $"Unknown data store kind '{kindRaw}'. Use 'FileType' or 'SqlType'.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Create, new EntityRef(EntityKinds.DataStore, string.Empty), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, "DataStore:Create permission required.");

        var preview = new { name = name.Trim(), description, kind = kind.ToString() };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_create_proposal", action, preview);

        if (kind == DataStoreKind.SqlType)
        {
            var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
            if (!provisioner.IsEnabled)
                return ConfirmGate.Failed("data_store_create_failed", action, "SqlType stores require the datastores Postgres to be configured.");
        }

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        try
        {
            var row = await store.CreateAsync(
                new CreateDataStoreInput(name.Trim(), description?.Trim(), kind),
                ctx.Session.UserId, ct);
            if (kind == DataStoreKind.SqlType)
            {
                var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
                await provisioner.ProvisionAsync(row.Id, ct);
            }
            return ConfirmGate.Committed("data_store_create_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                kind = ((DataStoreKind)row.Kind).ToString()
            });
        }
        catch (DataStoreNameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_create_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "update_data_store";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        if (name is null && description is null)
            return ConfirmGate.Rejected(action, "At least one of name or description must be set.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Data store {id} not found.");

        var preview = new { id, before = new { existing.Name, existing.Description }, patch = new { name, description } };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_update_proposal", action, preview);

        try
        {
            var row = await store.UpdateAsync(
                id, new UpdateDataStoreInput(name?.Trim(), description?.Trim()), ctx.Session.UserId, ct);
            return ConfirmGate.Committed("data_store_update_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description
            });
        }
        catch (DataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_update_failed", action, $"Data store {id} not found.");
        }
        catch (DataStoreNameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_update_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Delete, new EntityRef(EntityKinds.DataStore, id.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Delete permission denied on {id}.");

        var store = ctx.Services.GetRequiredService<IDataStoreStore>();
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Data store {id} not found.");

        var preview = new
        {
            id,
            existing.Name,
            kind = ((DataStoreKind)existing.Kind).ToString(),
            warning = "Irreversible. SqlType deletes drop the per-store schema and every ingested table; FileType deletes drop every file metadata row."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_delete_proposal", action, preview);

        var provisioner = ctx.Services.GetRequiredService<SqlDataStoreProvisioner>();
        await provisioner.DeprovisionAsync(id, ct);
        var deleted = await store.DeleteAsync(id, ct);
        if (!deleted)
            return ConfirmGate.Failed("data_store_delete_failed", action, $"Data store {id} not found.");
        return ConfirmGate.Committed("data_store_delete_committed", action, new { id, name = existing.Name });
    }

    private static async Task<JsonElement> InvokeCreateFolderAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_data_store_folder";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        var folderPath = ReadString(args, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
            return ConfirmGate.Rejected(action, "folderPath is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, folderPath };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_folder_create_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            await files.CreateFolderAsync(dataStoreId, folderPath, ct);
            return ConfirmGate.Committed("data_store_folder_create_committed", action, preview);
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_folder_create_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_folder_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteFolderAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store_folder";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        var folderPath = ReadString(args, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
            return ConfirmGate.Rejected(action, "folderPath is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, folderPath, warning = "Deletes every file under this prefix. Irreversible." };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_folder_delete_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var deletedCount = await files.DeleteFolderAsync(dataStoreId, folderPath, ct);
            return ConfirmGate.Committed("data_store_folder_delete_committed", action, new { dataStoreId, folderPath, filesDeleted = deletedCount });
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (InvalidOperationException ex)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_folder_delete_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_data_store_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return ConfirmGate.Rejected(action, "fileId is required.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, fileId, warning = "Irreversible." };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_file_delete_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var deleted = await files.DeleteFileAsync(dataStoreId, fileId, ct);
            return ConfirmGate.Committed("data_store_file_delete_committed", action, new
            {
                dataStoreId,
                fileId = deleted.Id,
                folderPath = deleted.FolderPath,
                filename = deleted.Filename
            });
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_delete_failed", action, $"File {fileId} not found in data store {dataStoreId}.");
        }
    }

    private static async Task<JsonElement> InvokeRenameFileAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "rename_or_move_data_store_file";
        if (!TryReadGuid(args, "dataStoreId", out var dataStoreId))
            return ConfirmGate.Rejected(action, "dataStoreId is required.");
        if (!TryReadGuid(args, "fileId", out var fileId))
            return ConfirmGate.Rejected(action, "fileId is required.");
        var newFolderPath = ReadString(args, "newFolderPath");
        var newFilename = ReadString(args, "newFilename");
        if (newFolderPath is null && newFilename is null)
            return ConfirmGate.Rejected(action, "At least one of newFolderPath or newFilename must be set.");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.DataStore, dataStoreId.ToString()), ct);
        if (!decision.IsAllowed)
            return ConfirmGate.Rejected(action, $"DataStore:Edit permission denied on {dataStoreId}.");

        var preview = new { dataStoreId, fileId, newFolderPath, newFilename };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("data_store_file_rename_proposal", action, preview);

        var files = ctx.Services.GetRequiredService<IFileDataStoreService>();
        try
        {
            var (prevFolder, prevFilename, entity) = await files.RenameOrMoveFileAsync(
                dataStoreId, fileId, newFolderPath, newFilename, ctx.Session.UserId, ct);
            return ConfirmGate.Committed("data_store_file_rename_committed", action, new
            {
                dataStoreId,
                fileId = entity.Id,
                previousFolderPath = prevFolder,
                previousFilename = prevFilename,
                folderPath = entity.FolderPath,
                filename = entity.Filename
            });
        }
        catch (FileDataStoreNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, $"Data store {dataStoreId} is not a FileType store or does not exist.");
        }
        catch (FileDataStoreFileNotFoundException)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, $"File {fileId} not found in data store {dataStoreId}.");
        }
        catch (FileDataStoreFilenameConflictException ex)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("data_store_file_rename_failed", action, ex.Message);
        }
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out id);
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
