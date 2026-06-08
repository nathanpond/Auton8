using System.Globalization;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Endpoints;

// Data Stores endpoints (docs/plans/2026-05-30-data-stores-implementation.md).
// Two address surfaces hang off /api/datastores/{id}/ once a store exists:
//   - File-type stores: /files, /folders
//   - SQL-type stores: /tables (preview + ingest)
// Endpoints rely on the FileDataStoreService / CsvIngestor to validate the
// store kind matches the address (throw FileDataStoreNotFoundException with
// 404 on mismatch).
public static class DataStoreEndpoints
{
    public static IEndpointRouteBuilder MapDataStoreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datastores").RequireAuthorization();

        // Per-store View grants drive what shows up here — users with View on
        // /datastore/<id> see that store, users with /datastore/* see all of
        // them, users with no datastore grants get an empty list. The shape
        // matches the RecordType / Role / Group list endpoints so the SPA
        // doesn't need a special-case "you can see this but not open it"
        // affordance. AuthorizedInHandler (vs RequireKindPermission) skips
        // the binary List gate entirely: an actor with no grants gets back
        // []; an actor with a single per-store grant gets back one row.
        group.MapGet("/", async (
            HttpContext http,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var query = db.DataStores.AsNoTracking().OrderBy(d => d.Name);
            var visible = await authorizer.FilterQueryAsync(
                db, http.User, EntityKinds.DataStore, Actions.View, query, ct);
            var rows = await visible.ToListAsync(ct);
            await auditPublisher.PublishAsync(
                DataStoreEventTopic.TopicName,
                DataStoreEventTypes.ListViewed,
                DataStoreResourceKinds.DataStore,
                resource: null,
                details: new { resultCount = rows.Count },
                ct);
            return Results.Ok(rows);
        }).AuthorizedInHandler("filters via FilterQueryAsync(DataStore, View); empty grants -> empty list");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDataStoreStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                DataStoreEventTopic.TopicName,
                DataStoreEventTypes.Viewed,
                DataStoreResourceKinds.DataStore,
                resource: new { id = row.Id, name = row.Name },
                details: null,
                ct);
            return Results.Ok(row);
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        group.MapPost("/", async (
            CreateDataStoreRequest request,
            HttpContext http,
            IDataStoreStore store,
            SqlDataStoreProvisioner provisioner,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (!Enum.TryParse<DataStoreKind>(request.Kind, ignoreCase: true, out var kind))
            {
                return Results.BadRequest(new { reason = $"Unknown data store kind '{request.Kind}'." });
            }
            if (kind == DataStoreKind.SqlType && !provisioner.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var row = await store.CreateAsync(
                    new CreateDataStoreInput(request.Name, request.Description, kind),
                    actorId, ct);
                if (kind == DataStoreKind.SqlType)
                {
                    // Best-effort schema + role provisioning. Failure leaves
                    // the metadata row in place; the operator can retry by
                    // deleting and recreating the row, or by hitting an
                    // explicit reprovision endpoint added later.
                    await provisioner.ProvisionAsync(row.Id, ct);
                }
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.Created,
                    DataStoreResourceKinds.DataStore,
                    resource: new { id = row.Id, name = row.Name },
                    details: new { kind = kind.ToString() },
                    ct);
                return Results.Created($"/api/datastores/{row.Id}", row);
            }
            catch (DataStoreNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.DataStore, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDataStoreRequest request,
            HttpContext http,
            IDataStoreStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                // Snapshot the previous name for the audit event before we
                // overwrite it — admins legitimately rename stores and the
                // log needs to show "old → new" so the renamed row can be
                // traced through prior events keyed on the old name.
                var previous = await store.GetAsync(id, ct);
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDataStoreInput(request.Name, request.Description),
                    actorId, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.Updated,
                    DataStoreResourceKinds.DataStore,
                    resource: new { id = row.Id, name = row.Name },
                    details: new { previousName = previous?.Name },
                    ct);
                return Results.Ok(row);
            }
            catch (DataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DataStoreNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDataStoreStore store,
            SqlDataStoreProvisioner provisioner,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Read the row before deleting so the audit event can carry the
            // store name. The provisioner/delete chain leaves no rollback
            // path if the audit publish fails, but the noop publisher is
            // resilient and the Dapr publisher swallows + logs on failure.
            var existing = await store.GetAsync(id, ct);
            // Sweep the per-datastore schema/role before deleting the row so
            // the cluster reflects the final state if either step fails.
            await provisioner.DeprovisionAsync(id, ct);
            var deleted = await store.DeleteAsync(id, ct);
            if (!deleted) return Results.NotFound();
            await auditPublisher.PublishAsync(
                DataStoreEventTopic.TopicName,
                DataStoreEventTypes.Deleted,
                DataStoreResourceKinds.DataStore,
                resource: new { id, name = existing?.Name },
                details: null,
                ct);
            return Results.NoContent();
        }).RequirePermission(EntityKinds.DataStore, Actions.Delete);

        // --- File-type sub-surface -------------------------------------------------

        group.MapGet("/{id:guid}/files", async (
            Guid id, string? folder, IFileDataStoreService files, CancellationToken ct) =>
        {
            try
            {
                var listing = await files.ListAsync(id, folder ?? "/", ct);
                return Results.Ok(listing);
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        group.MapPost("/{id:guid}/files", async (
            Guid id,
            HttpContext http,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Results.BadRequest(new { reason = "Multipart form required." });
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var form = await http.Request.ReadFormAsync(ct);
            var folder = form["folder"].FirstOrDefault() ?? "/";
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { reason = "A 'file' form-file is required." });
            try
            {
                await using var stream = file.OpenReadStream();
                var entity = await files.UploadAsync(
                    id, folder, file.FileName, file.ContentType, stream, actorId, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FileUploaded,
                    DataStoreResourceKinds.File,
                    resource: new
                    {
                        id = entity.Id,
                        datastoreId = id,
                        folderPath = entity.FolderPath,
                        filename = entity.Filename
                    },
                    details: new
                    {
                        sizeBytes = entity.SizeBytes,
                        contentType = entity.ContentType
                    },
                    ct);
                return Results.Created($"/api/datastores/{id}/files/{entity.Id}", entity);
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFilenameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        group.MapGet("/{id:guid}/files/{fileId:guid}", async (
            Guid id,
            Guid fileId,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var (metadata, content) = await files.DownloadAsync(id, fileId, ct);
                // Publish before streaming so the audit lands even if the
                // client disconnects mid-stream. From an exfiltration-audit
                // standpoint, the access decision is what matters; whether
                // every byte actually reached the client is secondary.
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FileDownloaded,
                    DataStoreResourceKinds.File,
                    resource: new
                    {
                        id = metadata.Id,
                        datastoreId = id,
                        folderPath = metadata.FolderPath,
                        filename = metadata.Filename
                    },
                    details: new
                    {
                        sizeBytes = metadata.SizeBytes,
                        contentType = metadata.ContentType
                    },
                    ct);
                return Results.File(
                    content,
                    contentType: metadata.ContentType ?? "application/octet-stream",
                    fileDownloadName: metadata.Filename);
            }
            catch (FileDataStoreFileNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        group.MapDelete("/{id:guid}/files/{fileId:guid}", async (
            Guid id,
            Guid fileId,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var deleted = await files.DeleteFileAsync(id, fileId, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FileDeleted,
                    DataStoreResourceKinds.File,
                    resource: new
                    {
                        id = deleted.Id,
                        datastoreId = id,
                        folderPath = deleted.FolderPath,
                        filename = deleted.Filename
                    },
                    details: new { sizeBytes = deleted.SizeBytes },
                    ct);
                return Results.NoContent();
            }
            catch (FileDataStoreFileNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit);

        group.MapPost("/{id:guid}/folders", async (
            Guid id,
            FolderRequest request,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            try
            {
                await files.CreateFolderAsync(id, request.FolderPath, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FolderCreated,
                    DataStoreResourceKinds.Folder,
                    resource: new { datastoreId = id, path = request.FolderPath },
                    details: null,
                    ct);
                return Results.NoContent();
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // Minimal API refuses to bind a body on a DELETE without an
        // explicit annotation. Take the folder path as a `path` query
        // parameter instead — `?path=/foo/bar` is the canonical shape.
        group.MapDelete("/{id:guid}/folders", async (
            Guid id,
            string path,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { reason = "path is required." });
            try
            {
                var filesDeleted = await files.DeleteFolderAsync(id, path, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FolderDeleted,
                    DataStoreResourceKinds.Folder,
                    resource: new { datastoreId = id, path },
                    details: new { filesDeleted },
                    ct);
                return Results.NoContent();
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // Rename and/or move a single file. Both fields are optional; at
        // least one must be set. Same-folder + same-name is a no-op 200.
        group.MapPatch("/{id:guid}/files/{fileId:guid}", async (
            Guid id,
            Guid fileId,
            RenameFileRequest request,
            HttpContext http,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var (prevFolder, prevFilename, entity) = await files.RenameOrMoveFileAsync(
                    id, fileId, request.NewFolderPath, request.NewFilename, actorId, ct);
                // Classify by folder change: any folder delta is a move (the
                // filename may have changed in the same call), no folder
                // delta is a rename. The details payload disambiguates.
                var folderChanged = !string.Equals(prevFolder, entity.FolderPath, StringComparison.Ordinal);
                var filenameChanged = !string.Equals(prevFilename, entity.Filename, StringComparison.Ordinal);
                if (folderChanged || filenameChanged)
                {
                    await auditPublisher.PublishAsync(
                        DataStoreEventTopic.TopicName,
                        folderChanged ? DataStoreEventTypes.FileMoved : DataStoreEventTypes.FileRenamed,
                        DataStoreResourceKinds.File,
                        resource: new
                        {
                            id = entity.Id,
                            datastoreId = id,
                            folderPath = entity.FolderPath,
                            filename = entity.Filename
                        },
                        details: new
                        {
                            previousFolderPath = prevFolder,
                            previousFilename = prevFilename
                        },
                        ct);
                }
                return Results.Ok(entity);
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFilenameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // Copy a single file. Allocates a fresh fileId; the SPA refreshes
        // the target folder to see the new entry rather than reading the
        // response body, but we still return the new metadata for clients
        // that want it.
        group.MapPost("/{id:guid}/files/{fileId:guid}/copy", async (
            Guid id,
            Guid fileId,
            CopyFileRequest request,
            HttpContext http,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var (source, copy) = await files.CopyFileAsync(
                    id, fileId, request.TargetFolderPath, request.NewFilename, actorId, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FileCopied,
                    DataStoreResourceKinds.File,
                    resource: new
                    {
                        id = copy.Id,
                        datastoreId = id,
                        folderPath = copy.FolderPath,
                        filename = copy.Filename
                    },
                    details: new
                    {
                        sourceFileId = source.Id,
                        sourceFolderPath = source.FolderPath,
                        sourceFilename = source.Filename,
                        sizeBytes = copy.SizeBytes
                    },
                    ct);
                return Results.Created(
                    $"/api/datastores/{id}/files/{copy.Id}", copy);
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFilenameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // Rename and/or move a folder. Rewrites folder_path on every file
        // under the source prefix. The SPA computes newPath itself for
        // both rename (same parent) and move (different parent) cases.
        group.MapPatch("/{id:guid}/folders", async (
            Guid id,
            RenameFolderRequest request,
            HttpContext http,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var filesAffected = await files.RenameOrMoveFolderAsync(id, request.Path, request.NewPath, actorId, ct);
                // Classify by parent change: same parent = rename, different
                // parent = move. The SPA uses the same PATCH for both, so we
                // distinguish on the server based on path shape.
                var prevParent = ParentOf(request.Path);
                var newParent = ParentOf(request.NewPath);
                var folderChanged = !string.Equals(prevParent, newParent, StringComparison.Ordinal);
                if (filesAffected > 0)
                {
                    await auditPublisher.PublishAsync(
                        DataStoreEventTopic.TopicName,
                        folderChanged ? DataStoreEventTypes.FolderMoved : DataStoreEventTypes.FolderRenamed,
                        DataStoreResourceKinds.Folder,
                        resource: new { datastoreId = id, path = request.NewPath },
                        details: new
                        {
                            previousPath = request.Path,
                            filesAffected
                        },
                        ct);
                }
                return Results.NoContent();
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFilenameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // Recursive folder copy. Every file under sourcePath gets a fresh
        // id and a fresh on-disk byte copy at the corresponding location
        // under targetPath.
        group.MapPost("/{id:guid}/folders/copy", async (
            Guid id,
            CopyFolderRequest request,
            HttpContext http,
            IFileDataStoreService files,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var filesCopied = await files.CopyFolderAsync(id, request.SourcePath, request.TargetPath, actorId, ct);
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    DataStoreEventTypes.FolderCopied,
                    DataStoreResourceKinds.Folder,
                    resource: new { datastoreId = id, path = request.TargetPath },
                    details: new
                    {
                        sourcePath = request.SourcePath,
                        filesCopied
                    },
                    ct);
                return Results.NoContent();
            }
            catch (FileDataStoreNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileDataStoreFilenameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        // --- SQL-type sub-surface --------------------------------------------------

        // List the tables that have been ingested into a SQL DataStore.
        // Returns the metadata + the inferred column schema inline so the
        // Datasets SPA can populate a per-table picker AND fill the dataset's
        // column list with one click — both of which were the central
        // ergonomic blockers called out by the data-feature UI audit.
        // FileType stores return an empty list rather than 4xx so the SPA
        // can call this unconditionally and present "no tables" in the UI.
        group.MapGet("/{id:guid}/tables", async (
            Guid id, AutoNateDbContext db, CancellationToken ct) =>
        {
            var rows = await db.DataStoreTables
                .Where(t => t.DataStoreId == id)
                .OrderBy(t => t.TableName)
                .ToListAsync(ct);
            var dtos = new List<DataStoreTableDto>(rows.Count);
            foreach (var row in rows)
            {
                List<CsvColumn> columns;
                try
                {
                    columns = JsonSerializer.Deserialize<List<CsvColumn>>(row.ColumnSchemaJson)
                        ?? new List<CsvColumn>();
                }
                catch (JsonException)
                {
                    // A hand-edited or corrupted row shouldn't take the
                    // whole list down — surface what we can and continue.
                    columns = new List<CsvColumn>();
                }
                dtos.Add(new DataStoreTableDto(
                    row.Id, row.SchemaName, row.TableName, columns, row.RowCount));
            }
            return Results.Ok(dtos);
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        // Top-N sample of an already-ingested table. Powers the "what's in
        // this datastore?" preview on the DataStore detail page so an admin
        // can see column headers + a few rows without first having to
        // define a Dataset over the table. SELECT-all against the per-store
        // schema, hard-clamped to MaxPreviewRows so a bad ?limit value can't
        // pull a million-row table over the wire.
        group.MapGet("/{id:guid}/tables/{tableId:guid}/preview", async (
            Guid id,
            Guid tableId,
            int? limit,
            AutoNateDbContext db,
            IDatastoresConnectionFactory connectionFactory,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!connectionFactory.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            var row = await db.DataStoreTables
                .AsNoTracking()
                .Where(t => t.Id == tableId && t.DataStoreId == id)
                .SingleOrDefaultAsync(ct);
            if (row is null) return Results.NotFound();

            const int MaxPreviewRows = 200;
            const int DefaultPreviewRows = 30;
            var cap = limit is null or <= 0
                ? DefaultPreviewRows
                : Math.Min(limit.Value, MaxPreviewRows);

            // SchemaName + TableName came from CsvIngestor's sanitiser
            // (alphanumeric/underscore only). Quote-escape defensively anyway
            // so a future migration that loosens the sanitiser doesn't open
            // an injection channel here.
            var quotedSchema = QuoteIdentifier(row.SchemaName);
            var quotedTable = QuoteIdentifier(row.TableName);
            var sql = $"SELECT * FROM {quotedSchema}.{quotedTable} LIMIT {cap}";

            try
            {
                await using var conn = await connectionFactory.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                var columns = new List<DataStoreTablePreviewColumn>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(new DataStoreTablePreviewColumn(
                        reader.GetName(i),
                        reader.GetDataTypeName(i)));
                }

                var rows = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync(ct))
                {
                    var dict = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        // Project every value through ToString so System.Text.Json never
                        // has to grapple with an Npgsql-specific type (NpgsqlRange,
                        // NpgsqlInterval, JsonDocument, byte[], etc.). The preview is
                        // a read-only UI sample, so the loss of fidelity vs. a typed
                        // payload doesn't matter and the simpler shape side-steps a
                        // whole class of "unsupported type" 500s.
                        dict[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct)
                            ? null
                            : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    }
                    rows.Add(dict);
                }

                return Results.Ok(new DataStoreTablePreviewDto(
                    row.SchemaName, row.TableName, columns, rows, row.RowCount));
            }
            catch (PostgresException ex)
            {
                var logger = loggerFactory.CreateLogger("AutoNate.Web.Endpoints.DataStoreEndpoints");
                logger.LogWarning(ex,
                    "Datastore table preview failed for {Schema}.{Table} (SqlState {SqlState}).",
                    row.SchemaName, row.TableName, ex.SqlState);
                return Results.Problem(
                    title: "Preview failed",
                    detail: $"Postgres {ex.SqlState}: {ex.MessageText}",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        group.MapPost("/{id:guid}/tables/preview", async (
            Guid id, HttpContext http, CsvIngestor ingestor, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Results.BadRequest(new { reason = "Multipart form required." });
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { reason = "A 'file' form-file is required." });
            try
            {
                await using var stream = file.OpenReadStream();
                var preview = await ingestor.PreviewAsync(stream, file.FileName, ct);
                return Results.Ok(preview);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        group.MapPost("/{id:guid}/tables", async (
            Guid id,
            HttpContext http,
            CsvIngestor ingestor,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Results.BadRequest(new { reason = "Multipart form required." });
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { reason = "A 'file' form-file is required." });
            var tableName = form["tableName"].FirstOrDefault() ?? Path.GetFileNameWithoutExtension(file.FileName);
            var columnsJson = form["columns"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(columnsJson))
                return Results.BadRequest(new { reason = "Columns JSON is required (use /preview to generate)." });
            List<CsvColumn>? columns;
            try
            {
                // JsonSerializerDefaults.Web = camelCase property matching, the
                // shape the SPA sends back from PreviewAsync's response
                // ({"name": "...", "postgresType": "..."}). With the default
                // (PascalCase) options both fields deserialize as null, and the
                // ingestor's SanitizeColumnName fallback renames every column
                // to col_1, col_2, ... silently.
                columns = JsonSerializer.Deserialize<List<CsvColumn>>(
                    columnsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { reason = "Columns JSON is invalid: " + ex.Message });
            }
            if (columns is null || columns.Count == 0)
                return Results.BadRequest(new { reason = "At least one column is required." });

            // Default opt-out: a fresh re-ingest of the same CSV without an
            // explicit mode returns 409 so the SPA can show the schema-diff
            // / bound-dataset impact warning and let the operator pick
            // append vs replace. Accepted values: "insert" (default),
            // "append", "replace".
            var modeRaw = form["mode"].FirstOrDefault();
            if (!TryParseIngestMode(modeRaw, out var mode))
            {
                return Results.BadRequest(new
                {
                    reason = $"Unknown ingest mode '{modeRaw}'. Use 'insert', 'append', or 'replace'."
                });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await ingestor.IngestAsync(
                    id, tableName, columns, stream, actorId, mode, ct);
                var eventType = result.Appended
                    ? DataStoreEventTypes.TableAppended
                    : result.Replaced
                        ? DataStoreEventTypes.TableReplaced
                        : DataStoreEventTypes.TableIngested;
                object details = result.Appended
                    ? new
                    {
                        rowsAppended = result.RowsInserted,
                        totalRowsAfter = (result.PreviousRowCount ?? 0) + result.RowsInserted,
                        previousRowCount = result.PreviousRowCount,
                        columnCount = columns.Count
                    }
                    : new
                    {
                        rowsInserted = result.RowsInserted,
                        columnCount = columns.Count,
                        previousRowCount = result.PreviousRowCount,
                        schemaChanged = result.SchemaChanged
                    };
                await auditPublisher.PublishAsync(
                    DataStoreEventTopic.TopicName,
                    eventType,
                    DataStoreResourceKinds.Table,
                    resource: new
                    {
                        id = result.TableId,
                        datastoreId = id,
                        schemaName = result.SchemaName,
                        tableName = result.TableName
                    },
                    details: details,
                    ct);
                return Results.Created($"/api/datastores/{id}/tables/{result.TableId}", result);
            }
            catch (DataStoreTableExistsException ex)
            {
                return Results.Conflict(new
                {
                    reason = ex.Message,
                    conflictKind = "exists",
                    existingTableId = ex.ExistingTableId,
                    sanitizedTableName = ex.SanitizedTableName,
                    existingRowCount = ex.ExistingRowCount,
                    existingColumns = ex.ExistingColumns
                });
            }
            catch (DataStoreTableSchemaMismatchException ex)
            {
                // Distinct conflictKind so the SPA can keep the operator in
                // the conflict view with Append disabled (rather than
                // bouncing them back to ready as a generic failure).
                return Results.Conflict(new
                {
                    reason = ex.Message,
                    conflictKind = "schemaMismatch",
                    existingTableId = ex.ExistingTableId,
                    sanitizedTableName = ex.SanitizedTableName,
                    existingRowCount = ex.ExistingRowCount,
                    existingColumns = ex.ExistingColumns,
                    incomingColumns = ex.IncomingColumns
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        return app;
    }

    // Parent of a POSIX-style path. "/" stays "/"; "/a" → "/"; "/a/b" → "/a".
    // Used to distinguish a folder rename (same parent) from a folder move
    // (different parent) for the audit event classification.
    private static string ParentOf(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path.Substring(0, idx);
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    // Parse the `mode` form field on POST /tables. Empty/null = Insert
    // (the default), matching the prior behavior where no mode field meant
    // "fail with 409 if the table already exists."
    private static bool TryParseIngestMode(string? raw, out CsvIngestMode mode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            mode = CsvIngestMode.Insert;
            return true;
        }
        switch (raw.Trim().ToLowerInvariant())
        {
            case "insert":
                mode = CsvIngestMode.Insert;
                return true;
            case "append":
                mode = CsvIngestMode.Append;
                return true;
            case "replace":
                mode = CsvIngestMode.Replace;
                return true;
            default:
                mode = CsvIngestMode.Insert;
                return false;
        }
    }
}

public sealed record class CreateDataStoreRequest(string Name, string? Description, string Kind);

public sealed record class UpdateDataStoreRequest(string? Name, string? Description);

public sealed record class FolderRequest(string FolderPath);

public sealed record class RenameFileRequest(string? NewFolderPath, string? NewFilename);

public sealed record class CopyFileRequest(string TargetFolderPath, string? NewFilename);

public sealed record class RenameFolderRequest(string Path, string NewPath);

public sealed record class CopyFolderRequest(string SourcePath, string TargetPath);

public sealed record class DataStoreTableDto(
    Guid Id,
    string SchemaName,
    string TableName,
    IReadOnlyList<CsvColumn> Columns,
    long RowCount);

public sealed record class DataStoreTablePreviewColumn(string Name, string PostgresType);

public sealed record class DataStoreTablePreviewDto(
    string SchemaName,
    string TableName,
    IReadOnlyList<DataStoreTablePreviewColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long TotalRowCount);
