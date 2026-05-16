import { useEffect, useMemo, useRef, useSyncExternalStore } from "react";
import * as Y from "yjs";
import type { NoteDto, NoteKind } from "@/api/content";
import {
  useYjsDocument,
  type YjsConnectionStatus,
  type YjsDocumentHandle
} from "./useYjsDocument";
import type { YjsRole } from "./ticket";

// Tag local-origin transactions so we can distinguish self-writes from
// remote ones when observing. Currently the observer treats both the
// same — we recompute the full list either way — but the marker is
// here in case a future caller needs the distinction.
const LOCAL_ORIGIN = Symbol("yjs-notes-list-local");

// Subset of NoteDto we replicate into the Y.Map. Picked to be exactly
// what the tab strip + ordering needs; if a future UI surface needs
// more fields, add them here AND in the .toYMap writer below.
export interface PageNoteMetadata {
  id: string;
  pageId: string;
  noteKind: NoteKind;
  title: string | null;
  sortOrder: number;
  pageNoteIndex: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
}

export interface UseYjsNotesListResult {
  handle: YjsDocumentHandle | null;
  status: YjsConnectionStatus;
  role: YjsRole;
  // Sorted by sortOrder ascending, ties broken by createdAtUtc — mirrors
  // the existing server-side ordering so the tab strip's appearance
  // doesn't change.
  notes: PageNoteMetadata[];
  // Set of noteIds present in the Y.Map at the moment we first observed
  // it. The NotesPage uses this to distinguish "auto-show this new note
  // because somebody just created it" from "this note already existed."
  initialNoteIds: Set<string>;
  // Mirror a NoteDto into the Y.Map. No-op when role is viewer or the
  // handle isn't ready. Caller should invoke AFTER the REST write
  // succeeds — dual-write keeps the .NET source of truth authoritative.
  upsertNote: (note: NoteDto) => void;
  // Mirror a delete. Same dual-write timing — REST first, then this.
  removeNote: (noteId: string) => void;
}

// Lives at `pagemeta:<pageId>`. One Y.Doc per page, populated with a
// single Y.Map keyed by noteId. Sidecar materializer returns null for
// this prefix so no .NET webhook fires; the Y.Doc is purely a live
// view atop the `notes` Postgres table.
export function useYjsNotesList(
  pageId: string | null,
  seedNotes: NoteDto[] | undefined
): UseYjsNotesListResult {
  const { handle, status, role } = useYjsDocument(
    pageId ? `pagemeta:${pageId}` : null
  );

  const notesMap = useMemo(
    () => (handle ? handle.doc.getMap<Y.Map<unknown>>("notes") : null),
    [handle]
  );

  // ── subscribe to Y.Map changes via useSyncExternalStore ───────────
  // The store snapshot is the materialized sorted array. We cache it in
  // a ref so React's required-cached-snapshot rule is satisfied — only
  // recompute when the observer fires.
  const snapshotRef = useRef<PageNoteMetadata[]>([]);

  const subscribe = useMemo(() => {
    return (cb: () => void) => {
      if (!notesMap) return () => {};
      const onChange = () => {
        snapshotRef.current = materialize(notesMap);
        cb();
      };
      // Initial snapshot (the Y.Map already has content if cold-loaded
      // from yjs_documents).
      snapshotRef.current = materialize(notesMap);
      notesMap.observeDeep(onChange);
      return () => notesMap.unobserveDeep(onChange);
    };
  }, [notesMap]);

  const notes = useSyncExternalStore(
    subscribe,
    () => snapshotRef.current,
    () => snapshotRef.current
  );

  // ── initial noteIds snapshot ──────────────────────────────────────
  // Captured once when notesMap first becomes non-null. Auto-show
  // logic in NotesPage compares incoming Y.Map keys against this set.
  const initialNoteIdsRef = useRef<Set<string>>(new Set());
  const initialCapturedRef = useRef<boolean>(false);
  useEffect(() => {
    if (!notesMap || initialCapturedRef.current) return;
    initialNoteIdsRef.current = new Set(notesMap.keys());
    initialCapturedRef.current = true;
  }, [notesMap]);

  // ── one-shot phantom-entry GC ─────────────────────────────────────
  // The seed effect below is intentionally additive — a stale REST
  // refetch must not delete real notes that are mid-flight. But a
  // pagemeta Y.Map can drift from the `notes` table over time:
  //   - dropped Y.Map write after a SPA crash mid-delete
  //   - a note row deleted by an admin tool bypassing the SPA
  //   - manual DB intervention
  // Once per session, after the first definitive REST result arrives,
  // editors reconcile by removing Y.Map entries whose ids aren't in
  // the REST list. After that pass, every subsequent seed stays
  // additive so concurrent creates aren't ghost-deleted.
  const gcRanRef = useRef<boolean>(false);
  useEffect(() => {
    if (!handle || !notesMap || !seedNotes || role !== "editor") return;
    if (gcRanRef.current) return;
    gcRanRef.current = true;
    const restIds = new Set(seedNotes.map((n) => n.id));
    handle.doc.transact(() => {
      for (const id of Array.from(notesMap.keys())) {
        if (!restIds.has(id)) notesMap.delete(id);
      }
    }, LOCAL_ORIGIN);
  }, [handle, notesMap, seedNotes, role]);

  // ── seed / reconcile from REST ────────────────────────────────────
  // Editors mirror the REST notes list into the Y.Map. For each REST
  // entry:
  //   - missing from Y.Map → insert
  //   - present and Y.Map's updatedAtUtc is OLDER than REST's → replace
  //   - present and Y.Map's updatedAtUtc is newer-or-equal → skip
  //
  // The "newer wins" behavior is what makes title renames (and other
  // metadata edits — sortOrder, isArchived, etc.) propagate across
  // users without per-editor dual-write code. Flow:
  //   1. User A renames a note via the editor → REST PATCH → 200 OK
  //   2. useUpdateNote's onSuccess invalidates `notesKey(pageId)`
  //   3. React-Query refetches → seedNotes prop here updates with
  //      newer updatedAtUtc for the renamed note
  //   4. This effect re-runs, detects newer REST timestamp, writes the
  //      updated metadata to Y.Map
  //   5. Y.Map observer fires on all connected clients → notesYjs.notes
  //      recomputes everywhere → tab labels update
  //
  // Deletions are NOT inferred from REST absence (a stale REST snapshot
  // would phantom-delete real notes). NotesPage's onConfirm-delete
  // calls removeNote() explicitly. Concurrent seeds dedupe via Y.Map
  // keys; Yjs's CRDT resolves concurrent writes deterministically.
  // Viewers skip the effect — they can't write — and rely on editors
  // to keep the Y.Map current.
  useEffect(() => {
    if (!handle || !notesMap || !seedNotes || role !== "editor") return;
    const doc = handle.doc;
    doc.transact(() => {
      for (const n of seedNotes) {
        const existing = notesMap.get(n.id);
        if (!existing) {
          notesMap.set(n.id, dtoToYMap(n));
          continue;
        }
        const existingUpdated = existing.get("updatedAtUtc");
        if (typeof existingUpdated !== "string" || n.updatedAtUtc > existingUpdated) {
          notesMap.set(n.id, dtoToYMap(n));
        }
      }
    }, LOCAL_ORIGIN);
  }, [handle, notesMap, seedNotes, role]);

  // ── stable mutators ───────────────────────────────────────────────
  const upsertNote = useMemo(
    () => (note: NoteDto) => {
      if (!handle || !notesMap || role !== "editor") return;
      handle.doc.transact(() => {
        notesMap.set(note.id, dtoToYMap(note));
      }, LOCAL_ORIGIN);
    },
    [handle, notesMap, role]
  );

  const removeNote = useMemo(
    () => (noteId: string) => {
      if (!handle || !notesMap || role !== "editor") return;
      handle.doc.transact(() => {
        notesMap.delete(noteId);
      }, LOCAL_ORIGIN);
    },
    [handle, notesMap, role]
  );

  return {
    handle,
    status,
    role,
    notes,
    initialNoteIds: initialNoteIdsRef.current,
    upsertNote,
    removeNote
  };
}

function dtoToYMap(note: NoteDto): Y.Map<unknown> {
  const map = new Y.Map<unknown>();
  map.set("id", note.id);
  map.set("pageId", note.pageId);
  map.set("noteKind", note.noteKind);
  map.set("title", note.title);
  map.set("sortOrder", note.sortOrder);
  map.set("pageNoteIndex", note.pageNoteIndex);
  map.set("isArchived", note.isArchived);
  map.set("createdAtUtc", note.createdAtUtc);
  map.set("updatedAtUtc", note.updatedAtUtc);
  map.set("createdBy", note.createdBy);
  map.set("updatedBy", note.updatedBy);
  return map;
}

function yMapToMetadata(y: Y.Map<unknown>): PageNoteMetadata | null {
  const id = y.get("id");
  const pageId = y.get("pageId");
  const noteKind = y.get("noteKind");
  if (typeof id !== "string" || typeof pageId !== "string" || typeof noteKind !== "string") {
    return null;
  }
  return {
    id,
    pageId,
    noteKind: noteKind as NoteKind,
    title: (y.get("title") as string | null | undefined) ?? null,
    sortOrder: (y.get("sortOrder") as number | undefined) ?? 0,
    pageNoteIndex: (y.get("pageNoteIndex") as number | undefined) ?? 0,
    isArchived: (y.get("isArchived") as boolean | undefined) ?? false,
    createdAtUtc: (y.get("createdAtUtc") as string | undefined) ?? "",
    updatedAtUtc: (y.get("updatedAtUtc") as string | undefined) ?? "",
    createdBy: (y.get("createdBy") as string | undefined) ?? "",
    updatedBy: (y.get("updatedBy") as string | undefined) ?? ""
  };
}

function materialize(notesMap: Y.Map<Y.Map<unknown>>): PageNoteMetadata[] {
  const out: PageNoteMetadata[] = [];
  notesMap.forEach((y) => {
    const m = yMapToMetadata(y);
    if (m) out.push(m);
  });
  // Same ordering the .NET endpoint uses (NoteEndpoints.cs:30):
  //   ORDER BY sort_order ASC, created_at_utc ASC
  out.sort((a, b) => {
    if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder;
    return a.createdAtUtc < b.createdAtUtc ? -1 : a.createdAtUtc > b.createdAtUtc ? 1 : 0;
  });
  return out;
}
