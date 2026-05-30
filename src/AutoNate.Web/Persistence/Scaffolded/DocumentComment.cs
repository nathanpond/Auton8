using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// Threaded comment anchored to a text range inside a Document body.
//
// The body's range markers (commentRangeStart / commentRangeEnd ProseMirror
// nodes) live INSIDE the Yjs Y.Doc — they sync in real time via Hocuspocus
// like any other body content. Comment METADATA — author, body, replies,
// resolved status — lives here in Postgres, fetched via REST, because:
//   * Per-comment permission gating (Commenter role gets to add comments
//     but not edit body) is much easier with a real authz boundary
//   * Comments need to be queryable for audit, search, future RAG
//   * Resolved/reopened lifecycle is naturally REST-shaped
//
// docx-editor's `Comment.id` is a `number` (matches OOXML's `w:comment id`),
// not a Guid. We carry both: `Id` is canonical (used in URLs, audit, FKs);
// `Number` is the per-document integer we hand to docx-editor so its
// commentRangeStart/End markers and our metadata pair up. Numbers are
// allocated by the editor on add; the (DocumentId, Number) pair is unique.
public partial class DocumentComment
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    // Per-document integer matching the OOXML w:comment id docx-editor
    // writes into the body's range markers. Unique within a single
    // document; conflicts on this column reject the insert.
    public int Number { get; set; }

    // Self-reference for replies. NULL = top-level (thread root).
    public Guid? ParentCommentId { get; set; }

    // Identifies the conversation thread. Equals Id for the root comment;
    // every reply carries the root's Id so a single index scan returns the
    // whole thread.
    public Guid ThreadId { get; set; }

    public Guid AuthorId { get; set; }

    // Plain-text comment body. docx-editor expects a Paragraph[] tree;
    // the SPA wraps the text in a single paragraph on read. v1 accepts
    // the fidelity trade — most comments are short prose. Richer
    // formatting (mentions, links, bold) can land in a later polish.
    public string BodyText { get; set; } = null!;

    // Resolution lifecycle. Resolved comments stay in the list (the
    // docx-editor sidebar shows them collapsed) until explicitly deleted.
    // Re-opening clears both fields.
    public DateTime? ResolvedAtUtc { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
