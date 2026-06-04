using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using Microsoft.EntityFrameworkCore;

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

        group.MapGet("/", async (IDataStoreStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.DataStore, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IDataStoreStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.DataStore, Actions.View);

        group.MapPost("/", async (
            CreateDataStoreRequest request,
            HttpContext http,
            IDataStoreStore store,
            SqlDataStoreProvisioner provisioner,
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
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDataStoreInput(request.Name, request.Description),
                    actorId, ct);
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
            CancellationToken ct) =>
        {
            // Sweep the per-datastore schema/role before deleting the row so
            // the cluster reflects the final state if either step fails.
            await provisioner.DeprovisionAsync(id, ct);
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
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
            Guid id, Guid fileId, IFileDataStoreService files, CancellationToken ct) =>
        {
            try
            {
                var (metadata, content) = await files.DownloadAsync(id, fileId, ct);
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
            Guid id, Guid fileId, IFileDataStoreService files, CancellationToken ct) =>
        {
            try
            {
                await files.DeleteFileAsync(id, fileId, ct);
                return Results.NoContent();
            }
            catch (FileDataStoreFileNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit);

        group.MapPost("/{id:guid}/folders", async (
            Guid id, FolderRequest request, IFileDataStoreService files, CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            try
            {
                await files.CreateFolderAsync(id, request.FolderPath, ct);
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
            Guid id, string path, IFileDataStoreService files, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { reason = "path is required." });
            try
            {
                await files.DeleteFolderAsync(id, path, ct);
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
            Guid id, HttpContext http, CsvIngestor ingestor, CancellationToken ct) =>
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
                columns = JsonSerializer.Deserialize<List<CsvColumn>>(columnsJson);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { reason = "Columns JSON is invalid: " + ex.Message });
            }
            if (columns is null || columns.Count == 0)
                return Results.BadRequest(new { reason = "At least one column is required." });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await ingestor.IngestAsync(id, tableName, columns, stream, actorId, ct);
                return Results.Created($"/api/datastores/{id}/tables/{result.TableId}", result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataStore, Actions.Edit)
          .DisableAntiforgery();

        return app;
    }
}

public sealed record class CreateDataStoreRequest(string Name, string? Description, string Kind);

public sealed record class UpdateDataStoreRequest(string? Name, string? Description);

public sealed record class FolderRequest(string FolderPath);

public sealed record class DataStoreTableDto(
    Guid Id,
    string SchemaName,
    string TableName,
    IReadOnlyList<CsvColumn> Columns,
    long RowCount);
