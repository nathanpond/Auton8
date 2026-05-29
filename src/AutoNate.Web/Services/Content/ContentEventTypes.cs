namespace AutoNate.Web.Services.Content;

// Topic + event-type names for the content.events bus topic. Covers the
// project/cabinet/notebook/page/note CRUD lifecycle, project membership
// changes, the deletions-lock toggle, page-version history, and page
// attachment uploads. Note bytes and tiptap/Excalidraw/Draw.io payloads
// never appear in event payloads — only structural identifiers and audit-
// relevant scalars (counts, file names, content types, sha256 prefixes).
public static class ContentEventTopic
{
    public const string TopicRoot = "content";
    public const string TopicName = "content.events";
}

public static class ContentResourceKinds
{
    public const string Project = "project";
    public const string ProjectMember = "project.member";
    public const string Cabinet = "cabinet";
    public const string Notebook = "notebook";
    public const string Page = "page";
    public const string PageVersion = "page.version";
    public const string PageAttachment = "page.attachment";
    public const string Note = "note";
    public const string NoteVersion = "note.version";
    public const string Comment = "comment";
    public const string Folder = "folder";
    public const string Document = "document";
    public const string DocumentVersion = "document.version";
    public const string DocumentBinding = "document.binding";
}

public static class ContentEventTypes
{
    // Project lifecycle
    public const string ProjectCreated = "content.project.created";
    public const string ProjectUpdated = "content.project.updated";
    public const string ProjectArchived = "content.project.archived";
    public const string ProjectRestored = "content.project.restored";
    public const string ProjectDeleted = "content.project.deleted";
    public const string ProjectDeletionsLockToggled = "content.project.deletions_lock.toggled";
    public const string ProjectListViewed = "content.project.list.viewed";
    public const string ProjectViewed = "content.project.viewed";

    // Membership
    public const string ProjectMemberAdded = "content.project.member.added";
    public const string ProjectMemberRoleChanged = "content.project.member.role.changed";
    public const string ProjectMemberRemoved = "content.project.member.removed";
    public const string ProjectMemberListViewed = "content.project.member.list.viewed";

    // Cabinet
    public const string CabinetCreated = "content.cabinet.created";
    public const string CabinetUpdated = "content.cabinet.updated";
    public const string CabinetMoved = "content.cabinet.moved";
    public const string CabinetArchived = "content.cabinet.archived";
    public const string CabinetRestored = "content.cabinet.restored";
    public const string CabinetDeleted = "content.cabinet.deleted";
    public const string CabinetListViewed = "content.cabinet.list.viewed";
    public const string CabinetViewed = "content.cabinet.viewed";

    // Notebook
    public const string NotebookCreated = "content.notebook.created";
    public const string NotebookUpdated = "content.notebook.updated";
    public const string NotebookMoved = "content.notebook.moved";
    public const string NotebookArchived = "content.notebook.archived";
    public const string NotebookRestored = "content.notebook.restored";
    public const string NotebookDeleted = "content.notebook.deleted";
    public const string NotebookListViewed = "content.notebook.list.viewed";
    public const string NotebookViewed = "content.notebook.viewed";

    // Page
    public const string PageCreated = "content.page.created";
    public const string PageUpdated = "content.page.updated";
    public const string PageMoved = "content.page.moved";
    public const string PageArchived = "content.page.archived";
    public const string PageRestored = "content.page.restored";
    public const string PageDeleted = "content.page.deleted";
    public const string PageCopied = "content.page.copied";
    public const string PageTreeViewed = "content.page.tree.viewed";
    public const string PageViewed = "content.page.viewed";
    public const string PageFavorited = "content.page.favorited";
    public const string PageUnfavorited = "content.page.unfavorited";

    // Page versions
    public const string PageVersionCreated = "content.page.version.created";
    public const string PageVersionRestored = "content.page.version.restored";
    public const string PageVersionDeleted = "content.page.version.deleted";
    public const string PageVersionListViewed = "content.page.version.list.viewed";
    public const string PageVersionViewed = "content.page.version.viewed";

    // Page attachments
    public const string PageAttachmentUploaded = "content.page.attachment.uploaded";
    public const string PageAttachmentRenamed = "content.page.attachment.renamed";
    public const string PageAttachmentDeleted = "content.page.attachment.deleted";
    public const string PageAttachmentDownloaded = "content.page.attachment.downloaded";
    public const string PageAttachmentListViewed = "content.page.attachment.list.viewed";

    // Note
    public const string NoteCreated = "content.note.created";
    public const string NoteUpdated = "content.note.updated";
    public const string NoteDeleted = "content.note.deleted";
    public const string NoteMoved = "content.note.moved";
    public const string NoteCopied = "content.note.copied";
    public const string NoteListViewed = "content.note.list.viewed";
    public const string NoteViewed = "content.note.viewed";

    // Note versions
    public const string NoteVersionCreated = "content.note.version.created";
    public const string NoteVersionRestored = "content.note.version.restored";
    public const string NoteVersionDeleted = "content.note.version.deleted";
    public const string NoteVersionListViewed = "content.note.version.list.viewed";
    public const string NoteVersionViewed = "content.note.version.viewed";

    // Comments (BlockNote thread lifecycle inside a page body).
    // Body edits inherit PageUpdated; these capture the comment-specific
    // user actions surfaced by the SPA after the corresponding Y.Map
    // write succeeds.
    public const string CommentCreated = "content.comment.created";
    public const string CommentReplied = "content.comment.replied";
    public const string CommentResolved = "content.comment.resolved";
    public const string CommentReopened = "content.comment.reopened";
    public const string CommentDeleted = "content.comment.deleted";
    public const string DocumentCommentListViewed = "content.document.comment.list.viewed";

    // Document bindings (Phase 5). Created/Refreshed/Deleted fire on the
    // single-binding endpoints; RefreshedAll fires once per /refresh-all
    // call with the total binding count in details.
    public const string DocumentBindingCreated = "content.document.binding.created";
    public const string DocumentBindingUpdated = "content.document.binding.updated";
    public const string DocumentBindingRefreshed = "content.document.binding.refreshed";
    public const string DocumentBindingDeleted = "content.document.binding.deleted";
    public const string DocumentBindingsRefreshedAll = "content.document.bindings.refreshed_all";
    public const string DocumentBindingListViewed = "content.document.binding.list.viewed";

    // Folder (Documents subsystem — Phase 1). Mirrors the Cabinet lifecycle
    // shape: created/updated/moved/archived/restored/deleted + list/view.
    public const string FolderCreated = "content.folder.created";
    public const string FolderUpdated = "content.folder.updated";
    public const string FolderMoved = "content.folder.moved";
    public const string FolderArchived = "content.folder.archived";
    public const string FolderRestored = "content.folder.restored";
    public const string FolderDeleted = "content.folder.deleted";
    public const string FolderListViewed = "content.folder.list.viewed";
    public const string FolderViewed = "content.folder.viewed";

    // Document (Documents subsystem — Phase 2). Mirrors the Page lifecycle
    // shape (which is the closest analogue: content with body + versions).
    // Templates fire the same events with details.kind='template'; callers
    // don't need a separate event type per document kind.
    public const string DocumentCreated = "content.document.created";
    public const string DocumentUpdated = "content.document.updated";
    public const string DocumentMoved = "content.document.moved";
    public const string DocumentArchived = "content.document.archived";
    public const string DocumentRestored = "content.document.restored";
    public const string DocumentDeleted = "content.document.deleted";
    public const string DocumentListViewed = "content.document.list.viewed";
    public const string DocumentViewed = "content.document.viewed";

    // Document versions
    public const string DocumentVersionCreated = "content.document.version.created";
    public const string DocumentVersionRestored = "content.document.version.restored";
    public const string DocumentVersionDeleted = "content.document.version.deleted";
    public const string DocumentVersionListViewed = "content.document.version.list.viewed";
    public const string DocumentVersionViewed = "content.document.version.viewed";
}
