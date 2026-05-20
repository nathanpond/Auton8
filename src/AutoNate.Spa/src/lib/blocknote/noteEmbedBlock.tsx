import { useEffect, useMemo, useState } from "react";
import { BlockNoteEditor, type PartialBlock } from "@blocknote/core";
import { createReactBlockSpec } from "@blocknote/react";
import { useLocation } from "react-router-dom";
import { Anchor, Box, Group, Stack, Text } from "@mantine/core";
import { useNotes } from "@/hooks/useContent";
import type { NoteDto, NoteKind } from "@/api/content";
import { NOTE_KIND_META, notesTheme } from "@/pages/notes/notesTheme";
import { EmbedDepthContext, useEmbedDepth } from "./PageEditorContext";
import { usePageEditorSignal } from "./pageEditorSignal";

// Block id: keep short, never user-facing. Used by the slash command's
// insertBlocks call and as the type discriminator in the page's stored
// JSON.
export const NOTE_EMBED_BLOCK_TYPE = "noteEmbed" as const;

// noteEmbed is a leaf block that references another note on the same
// page. propSchema is intentionally tiny — the actual content is fetched
// at render time from useNotes(pageId), so a stale id still renders a
// graceful "linked note no longer exists" card instead of duplicating
// content that might drift from the source.
export const noteEmbedBlock = createReactBlockSpec(
  {
    type: NOTE_EMBED_BLOCK_TYPE,
    // Tiny propSchema on purpose — the actual rendered content is fetched
    // at render time from useNotes(pageId), so a stale id renders a
    // graceful "linked note no longer exists" card instead of duplicating
    // content that might drift from the source. NO `textAlignment` /
    // `backgroundColor` props: those defaults are inline-formatting
    // affordances that don't apply to a leaf embed block.
    propSchema: {
      noteId: { default: "" }
    },
    content: "none"
  },
  {
    // Mirror the built-in Divider block's meta. Defaults for content="none"
    // blocks are `isolating: true` + `defining: true`, and that combination
    // was causing ProseMirror to delete the noteEmbed on the next
    // transaction after insert — Yjs broadcast the delete and the embed
    // visually disappeared at the next auto-save. Setting both to false
    // matches Divider, which is the only built-in content="none" block
    // and works fine.
    meta: {
      isolating: false,
      defining: false
    },
    render: NoteEmbedRender
  }
);

function NoteEmbedRender({
  block,
  editor
}: {
  block: { props: { noteId: string } };
  // BlockNote passes the host editor instance — that's the reliable key
  // we use to read the per-editor page/editable signal. Doesn't rely
  // on React context inheritance through Tiptap's NodeView, which is
  // brittle depending on the render strategy in any given Tiptap version.
  editor: BlockNoteEditor<any, any, any>;
}) {
  const signal = usePageEditorSignal(editor);
  const pageId = signal?.pageId ?? null;
  const isEditable = signal?.editable ?? false;
  const depth = useEmbedDepth();
  const notesQuery = useNotes(pageId);
  const note = useMemo(() => {
    if (!block.props.noteId) return null;
    return notesQuery.data?.find((n) => n.id === block.props.noteId) ?? null;
  }, [notesQuery.data, block.props.noteId]);

  if (!block.props.noteId) {
    return <PlaceholderCard text="Note embed has no linked note." />;
  }

  // Signal isn't registered yet (race between editor mount and the
  // YjsEditor effect that registers it). Show the placeholder card
  // template — once the signal arrives, the embed re-renders.
  if (!signal) {
    return <PlaceholderCard text="Loading embedded note…" />;
  }

  if (notesQuery.isLoading) {
    return <PlaceholderCard text="Loading embedded note…" />;
  }

  if (!note) {
    return (
      <PlaceholderCard
        text="Linked note no longer exists."
        tone="warning"
      />
    );
  }

  // Edit mode AND view-mode-but-nested: render the bordered placeholder
  // card. In edit mode the card is the authoring affordance. Nested
  // (depth > 0) renders skip the heavyweight content to break recursion
  // and prevent fan-out from collapsing the viewport.
  if (isEditable || depth > 0) {
    return <EmbedPlaceholderCard note={note} />;
  }

  // View mode, top-level: render the actual note content. Increment the
  // depth context so any nested noteEmbed inside the rendered richtext
  // bottoms out at the placeholder card on its next recursion.
  return (
    <EmbedDepthContext.Provider value={depth + 1}>
      <EmbedContent note={note} />
    </EmbedDepthContext.Provider>
  );
}

function EmbedPlaceholderCard({ note }: { note: NoteDto }) {
  const meta = NOTE_KIND_META[note.noteKind as NoteKind];
  const title = note.title?.trim() || "Untitled note";
  const location = useLocation();
  // Notes URL pattern is /notes/{pageLocator}[/{pageNoteIndex}]. We're
  // rendering inside a page editor at /notes/{pageLocator} (possibly
  // with a trailing note segment) — strip any existing note segment and
  // append this note's pageNoteIndex.
  const href = useMemo(() => {
    const segments = location.pathname.split("/").filter(Boolean);
    const notesIdx = segments.indexOf("notes");
    if (notesIdx === -1 || notesIdx + 1 >= segments.length) return null;
    const pageLocator = segments[notesIdx + 1];
    return `/notes/${pageLocator}/${note.pageNoteIndex}`;
  }, [location.pathname, note.pageNoteIndex]);

  const body = (
    <Box
      style={{
        border: `1px solid ${notesTheme.border}`,
        borderLeft: `4px solid ${meta?.color ?? notesTheme.primary}`,
        borderRadius: 6,
        padding: "10px 14px",
        background: notesTheme.hover,
        marginBlock: 4,
        cursor: href ? "pointer" : "default",
        transition: "background 120ms"
      }}
    >
      <Group gap={10} wrap="nowrap" align="center">
        <i
          className={`fa ${meta?.icon ?? "fa-file-lines"}`}
          style={{ color: meta?.color ?? notesTheme.primary, fontSize: 16 }}
        />
        <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
          <Text size="sm" fw={600} truncate>
            {title}
          </Text>
          <Text size="xs" c="dimmed">
            Embedded note · {meta?.label ?? note.noteKind}
          </Text>
        </Stack>
      </Group>
    </Box>
  );

  if (!href) return body;
  return (
    <Anchor
      href={href}
      target="_blank"
      rel="noreferrer"
      underline="never"
      style={{ display: "block", color: "inherit" }}
    >
      {body}
    </Anchor>
  );
}

function EmbedContent({ note }: { note: NoteDto }) {
  if (note.noteKind === "richtext") {
    return <RichtextEmbed note={note} />;
  }
  // drawing + diagram — both render from the previewSvg snapshot.
  return <SvgEmbed note={note} />;
}

// Renders the richtext note's blocks as static HTML via a headless
// BlockNoteEditor — no DOM mount, no nested BlockNoteView. Mounting a
// full BlockNoteView inside another editor's NodeView render is unstable
// (separate Tiptap editors interacting through the same ProseMirror
// machinery), and the static HTML path is sufficient for a read-only
// view-mode preview.
function RichtextEmbed({ note }: { note: NoteDto }) {
  // Re-render on contentJsonb change. The note's content may not be
  // materialized yet when the page first opens (Hocuspocus debounces
  // its store, and the .NET-side `notes.content_jsonb` mirror is updated
  // via the webhook AFTER that). Once a fresh value arrives in
  // useNotes() the embed re-renders here with the new HTML.
  const [html, setHtml] = useState<string | null>(null);
  const [hasContent, setHasContent] = useState(false);

  useEffect(() => {
    const blocks = parseBlocks(note.contentJsonb);
    if (!blocks || blocks.length === 0) {
      setHasContent(false);
      setHtml(null);
      return;
    }
    setHasContent(true);
    let cancelled = false;
    // BlockNoteEditor.create() can read schema lazily and on first
    // access may try to touch DOM bits. Wrap in try/catch and bail to
    // a placeholder rather than crashing the page render if BlockNote
    // throws on a content shape we don't expect.
    try {
      const headless = BlockNoteEditor.create();
      const out = headless.blocksToFullHTML(blocks);
      if (!cancelled) setHtml(out);
    } catch (err) {
      console.warn("[noteEmbed] richtext render failed:", err);
      if (!cancelled) setHtml(null);
    }
    return () => {
      cancelled = true;
    };
  }, [note.contentJsonb]);

  if (!hasContent) {
    return (
      <PlaceholderCard
        text="This note has no content yet. Open the note and add some text — the embed will update once the change syncs."
        tone="muted"
      />
    );
  }

  if (html === null) {
    return <PlaceholderCard text="Rendering preview…" tone="muted" />;
  }

  return (
    <Box
      style={{
        width: "100%",
        marginBlock: 8
      }}
      // BlockNote's HTML output is well-formed and excludes script tags
      // by construction. The source content is authored by the same
      // user(s) who can edit this page, so the trust boundary is the
      // same as for normal page content — no additional sanitizer
      // needed.
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}

function SvgEmbed({ note }: { note: NoteDto }) {
  if (!note.previewSvg) {
    return (
      <PlaceholderCard
        text={`Open the ${NOTE_KIND_META[note.noteKind].label.toLowerCase()} note to render a preview.`}
        tone="muted"
      />
    );
  }
  // data: URI form — img-mode SVG cannot execute scripts, which removes
  // the foreignObject/inline-script XSS surface that draw.io exports
  // sometimes carry. Sanitize-by-rendering-mode is the right default.
  const src = `data:image/svg+xml;utf8,${encodeURIComponent(note.previewSvg)}`;
  return (
    <Box style={{ width: "100%", marginBlock: 8 }}>
      <img
        src={src}
        alt={note.title ?? "Embedded note preview"}
        style={{
          width: "100%",
          height: "auto",
          display: "block"
        }}
      />
    </Box>
  );
}

function PlaceholderCard({
  text,
  tone = "muted"
}: {
  text: string;
  tone?: "muted" | "warning";
}) {
  return (
    <Box
      style={{
        border: `1px dashed ${tone === "warning" ? notesTheme.warning : notesTheme.border}`,
        borderRadius: 6,
        padding: "10px 14px",
        background: notesTheme.hover,
        marginBlock: 4
      }}
    >
      <Text size="sm" c={tone === "warning" ? "orange" : "dimmed"}>
        {text}
      </Text>
    </Box>
  );
}

// Tolerant parser for richtext note content. Mirrors PageOverview's
// parseInitialContent so a malformed payload renders an empty editor
// instead of throwing inside the block render.
function parseBlocks(raw: string | null | undefined): PartialBlock[] | undefined {
  if (!raw) return undefined;
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) return parsed as PartialBlock[];
    return undefined;
  } catch {
    return undefined;
  }
}
