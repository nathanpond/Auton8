namespace AutoNate.Web.Storage;

// Single source of truth for runtime, mutable, on-disk state. Every persistent
// path the app writes to should live under one of these roots so a single
// container volume mount (./mounts/data:/app/data) covers all of it.
//
// Public vs private is enforced by middleware, not by code that calls into
// IDataPaths: only PublicRoot is bound to the static-files middleware (mounted
// at DataOptions.PublicUrlPrefix); the others can never leak via static
// serving even if a caller writes user input to them.
public interface IDataPaths
{
    string Root { get; }
    string PublicRoot { get; }
    string PluginsRoot { get; }
    string UploadsRoot { get; }
    string RepositoriesRoot { get; }
    string TempRoot { get; }

    // Per docs/plans/2026-05-30-data-stores-implementation.md. Each FileType
    // DataStore owns a subdirectory keyed by its id under here. Same volume
    // semantics as the rest of the IDataPaths roots — never served via
    // middleware; access goes through DataStoreEndpoints.
    string DatastoresRoot { get; }
}
