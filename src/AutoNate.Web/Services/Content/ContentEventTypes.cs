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
}
