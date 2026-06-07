namespace AutoNate.Web.Services.DataStores;

// Topic + event-type names for the datastore.events bus topic. Surfaces every
// administrative and content-bearing action on a DataStore — both file-type
// stores (upload, download, rename, move, copy, delete on files and folders)
// and SQL-type stores (CSV ingest creating new tables). The download event in
// particular is what an auditor needs to answer "who took these bytes out of
// the system, when, and from which IP?" — there is no other trace of that
// flow in the request log.
public static class DataStoreEventTopic
{
    public const string TopicRoot = "datastore";
    public const string TopicName = "datastore.events";
}

public static class DataStoreResourceKinds
{
    public const string DataStore = "datastore";
    public const string File = "datastore.file";
    public const string Folder = "datastore.folder";
    public const string Table = "datastore.table";
}

public static class DataStoreEventTypes
{
    // Store lifecycle
    public const string Created = "datastore.created";
    public const string Updated = "datastore.updated";
    public const string Deleted = "datastore.deleted";

    // Store view events
    public const string ListViewed = "datastore.list.viewed";
    public const string Viewed = "datastore.viewed";

    // File mutations
    public const string FileUploaded = "datastore.file.uploaded";
    public const string FileDeleted = "datastore.file.deleted";
    // Renamed = same folder, new filename. Moved = new folder (filename may
    // also change in the same call; the details payload disambiguates).
    public const string FileRenamed = "datastore.file.renamed";
    public const string FileMoved = "datastore.file.moved";
    public const string FileCopied = "datastore.file.copied";
    // Downloaded covers any HTTP GET that streamed bytes back to the caller.
    // Critical for audit — without this the only trace of file exfiltration
    // is the access log, which doesn't carry an actor id.
    public const string FileDownloaded = "datastore.file.downloaded";

    // Folder mutations
    public const string FolderCreated = "datastore.folder.created";
    public const string FolderDeleted = "datastore.folder.deleted";
    public const string FolderRenamed = "datastore.folder.renamed";
    public const string FolderMoved = "datastore.folder.moved";
    public const string FolderCopied = "datastore.folder.copied";

    // SQL-type sub-surface: a successful CSV ingest creates a new per-store
    // table and inserts rows into it. Skipping the preview endpoint — it
    // doesn't touch persistent state and would flood the log on every CSV
    // drag-into-the-modal.
    public const string TableIngested = "datastore.table.ingested";
}
