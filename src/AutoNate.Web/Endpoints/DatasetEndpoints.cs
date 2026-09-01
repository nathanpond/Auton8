using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Cached;
using AutoNate.Web.Services.Datasets.Files;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Dataset CRUD + manual refresh (Phase 2 of the Data Stores plan).
// Querying datasets is the AQL surface — `FROM Dataset("name")` — and is
// served by AqlExecuteEndpoint; nothing here returns row data directly.
public static class DatasetEndpoints
{
    public static IEndpointRouteBuilder MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasets").RequireAuthorization();

        group.MapGet("/", async (IDatasetStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.Dataset, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IDatasetStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.Dataset, Actions.View);

        group.MapPost("/", async (
            CreateDatasetRequest request,
            HttpContext http,
            IDatasetStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (!Enum.TryParse<DatasetMode>(request.Mode, ignoreCase: true, out var mode))
            {
                return Results.BadRequest(new { reason = $"Unknown dataset mode '{request.Mode}'." });
            }
            if (request.Columns is null || request.Columns.Count == 0)
            {
                return Results.BadRequest(new { reason = "At least one column is required." });
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateDatasetInput(
                        request.Name,
                        request.Description,
                        mode,
                        request.Columns,
                        request.SourceKind,
                        request.SourceId,
                        request.SourceTableName,
                        request.RefreshCron,
                        request.FileScopeKind,
                        request.FileScopePath,
                        request.ParserKind,
                        request.ParserOptionsJson),
                    actorId, ct);
                return Results.Created($"/api/datasets/{row.Id}", row);
            }
            catch (DatasetNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Dataset, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDatasetRequest request,
            HttpContext http,
            IDatasetStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDatasetInput(request.Name, request.Description, request.RefreshCron),
                    actorId, ct);
                return Results.Ok(row);
            }
            catch (DatasetNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DatasetNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Dataset, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id, IDatasetStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.Dataset, Actions.Delete);

        // Manual refresh: invokes the materializer synchronously. Scheduled
        // refresh runs from DatasetRefreshScheduler at one-minute granularity.
        group.MapPost("/{id:guid}/refresh", async (
            Guid id,
            ICachedDatasetMaterializer materializer,
            CancellationToken ct) =>
        {
            try
            {
                await materializer.RefreshAsync(id, ct);
                return Results.NoContent();
            }
            catch (DatasetNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Dataset, Actions.Refresh);

        // Files-datastore preview. Hands the parser the same bytes the
        // executor would read at query time so the SPA can populate the
        // new dataset's locked column schema before the create call. For
        // folder scopes, previews against the first non-".keep" file —
        // the dataset's column schema is enforced strictly across every
        // file in the folder at execute time, so previewing one is
        // representative.
        group.MapPost("/preview-file-source", async (
            PreviewFileSourceRequest request,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IFileDataStoreService fileService,
            DatasetFileParserRegistry parserRegistry,
            IAuthorizer authorizer,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            if (string.IsNullOrWhiteSpace(request.ParserKind))
                return Results.BadRequest(new { reason = "parserKind is required." });
            if (string.IsNullOrWhiteSpace(request.ScopeKind))
                return Results.BadRequest(new { reason = "scopeKind is required." });
            if (string.IsNullOrWhiteSpace(request.ScopePath))
                return Results.BadRequest(new { reason = "scopePath is required." });

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var datastoreKind = await db.DataStores.AsNoTracking()
                .Where(d => d.Id == request.DataStoreId)
                .Select(d => (short?)d.Kind)
                .SingleOrDefaultAsync(ct);
            if (datastoreKind is null)
                return Results.BadRequest(new { reason = $"Data store '{request.DataStoreId}' not found." });

            // Authorize the *datastore* being read, not just the dataset the
            // caller intends to create (#183).
            //
            // The route gate is RequireKindPermission(Dataset, Create), and
            // that was the only check. The handler then read an arbitrary file
            // out of an arbitrary store named in the request body, so a caller
            // with dataset:create and no datastore grants at all — one who
            // gets an empty list from GET /api/datastores and 403 from every
            // /api/datastores/{id}/… route — could still name any store and
            // read back its file's column names, and through type inference
            // the shape of its values. One feature's permission bypassed
            // another's.
            //
            // The same (DataStore, View) pair GET /api/datastores/{id}/files
            // declares, so the two agree by construction.
            var storeDecision = await authorizer.AuthorizeAsync(
                http.User,
                Actions.View,
                new EntityRef(EntityKinds.DataStore, request.DataStoreId.ToString()),
                ct);
            if (!storeDecision.IsAllowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            if ((DataStoreKind)datastoreKind.Value != DataStoreKind.FileType)
                return Results.BadRequest(new { reason = "Data store is not a Files-type store." });

            Guid? fileId;
            if (string.Equals(request.ScopeKind, DatasetFileScopeReader.ScopeFile, StringComparison.OrdinalIgnoreCase))
            {
                var (folder, filename) = SplitFilePath(request.ScopePath);
#pragma warning disable CA1304, CA1311
                fileId = await db.DataStoreFiles.AsNoTracking()
                    .Where(f => f.DataStoreId == request.DataStoreId
                                && f.FolderPath == folder
                                && f.Filename.ToLower() == filename.ToLower())
                    .Select(f => (Guid?)f.Id)
                    .SingleOrDefaultAsync(ct);
#pragma warning restore CA1304, CA1311
                if (fileId is null)
                    return Results.NotFound(new { reason = $"File '{request.ScopePath}' not found." });
            }
            else if (string.Equals(request.ScopeKind, DatasetFileScopeReader.ScopeFolder, StringComparison.OrdinalIgnoreCase))
            {
                var folder = NormalizeFolder(request.ScopePath);
                fileId = await db.DataStoreFiles.AsNoTracking()
                    .Where(f => f.DataStoreId == request.DataStoreId
                                && f.FolderPath == folder
                                && f.Filename != ".keep")
                    .OrderBy(f => f.Filename)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefaultAsync(ct);
                if (fileId is null)
                    return Results.NotFound(new { reason = $"Folder '{request.ScopePath}' has no files to sample." });
            }
            else
            {
                return Results.BadRequest(new { reason = $"Unknown scopeKind '{request.ScopeKind}'." });
            }

            IDatasetFileParser parser;
            try { parser = parserRegistry.Get(request.ParserKind); }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }

            try
            {
                var (_, stream) = await fileService.DownloadAsync(request.DataStoreId, fileId.Value, ct);
                await using (stream)
                {
                    var columns = await parser.PreviewAsync(stream, request.ParserOptions, ct);
                    return Results.Ok(new PreviewFileSourceResponse(columns));
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Dataset, Actions.Create)
          .DisableAntiforgery();

        return app;
    }

    private static (string Folder, string Filename) SplitFilePath(string path)
    {
        var normalized = path.StartsWith('/') ? path : "/" + path;
        var lastSlash = normalized.LastIndexOf('/');
        var folder = lastSlash == 0 ? "/" : normalized[..lastSlash];
        var filename = normalized[(lastSlash + 1)..];
        return (folder, filename);
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "/";
        var v = folder.StartsWith('/') ? folder : "/" + folder;
        if (v.Length > 1 && v.EndsWith('/')) v = v[..^1];
        return v;
    }
}

public sealed record class CreateDatasetRequest(
    string Name,
    string? Description,
    string Mode,
    IReadOnlyList<DatasetColumn> Columns,
    string SourceKind,
    Guid SourceId,
    string? SourceTableName,
    string? RefreshCron,
    string? FileScopeKind = null,
    string? FileScopePath = null,
    string? ParserKind = null,
    string? ParserOptionsJson = null);

public sealed record class UpdateDatasetRequest(
    string? Name,
    string? Description,
    string? RefreshCron);

public sealed record class PreviewFileSourceRequest(
    Guid DataStoreId,
    string ScopeKind,
    string ScopePath,
    string ParserKind,
    Dictionary<string, string>? ParserOptions);

public sealed record class PreviewFileSourceResponse(
    IReadOnlyList<DatasetColumn> Columns);
