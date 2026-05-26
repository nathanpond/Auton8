import * as Y from "yjs";
import { ServerBlockNoteEditor } from "@blocknote/server-util";
import { BlockNoteSchema, defaultBlockSpecs, type PartialBlock } from "@blocknote/core";
// y-prosemirror's `yDocToProsemirrorJSON` walks a Y.XmlFragment and emits
// a ProseMirror Node JSON tree without requiring a schema on this side —
// exactly what we need for the documents prefix where the SPA owns the
// TipTap schema and the sidecar just snapshots the materialized JSON for
// .NET's body_jsonb mirror.
import { yDocToProsemirrorJSON } from "y-prosemirror";
import { noteEmbedServerSpec } from "./noteEmbedStub.js";
import type pg from "pg";

// Each materializer reads a Yjs document and produces the JSON string that
// .NET will store in `body_jsonb` (pages) or `content_jsonb` (notes). The
// snapshot is a derived view of the live Y.Doc — clients still talk to
// Hocuspocus directly for live edits; the mirror exists for read paths
// (HistoryModal, future search/PDF export, etc).
//
// Per-prefix routing lives in `selectMaterializer` below. webhook.ts
// dispatches on the document-name prefix and calls the matching one.

export type Materializer = (doc: Y.Doc) => Promise<string>;

// Critical: register `noteEmbed` in the server-side schema. Without it,
// y-prosemirror's read path (yXmlFragmentToProseMirrorRootNode) throws
// on the unknown node type and self-heals by DELETING the element from
// the Y.Doc — that delete is then broadcast to every connected client,
// making the embed visually disappear ~2s after every page edit (the
// Hocuspocus debounce interval that triggers materialization). The
// stub block has no real render — the materializer only reads the doc
// structure — but the schema MUST recognize the type.
const serverSchema = BlockNoteSchema.create({
  blockSpecs: {
    ...defaultBlockSpecs,
    noteEmbed: noteEmbedServerSpec()
  }
});

const serverBlockNoteEditor = ServerBlockNoteEditor.create({ schema: serverSchema });

// BlockNote-backed pages and richtext notes share the same materialization:
// Yjs XmlFragment → BlockNote blocks (server-side via @blocknote/server-util).
//
// Fragment name MUST match the SPA's `useBlockNoteWithYjs` (which writes
// to `doc.getXmlFragment("document-store")`). Server-util's `yDocToBlocks`
// defaults the second arg to "prosemirror" — passing it explicitly here
// keeps the SPA-side and server-side reads aligned. Without this, the
// materializer reads an empty fragment, serializes `[]`, and every
// richtext note / page snapshot lands in Postgres as an empty array
// regardless of what the user typed.
const blockNoteMaterializer: Materializer = async (doc) => {
  const blocks = await serverBlockNoteEditor.yDocToBlocks(doc, "document-store");
  return JSON.stringify(blocks);
};

// Excalidraw scenes are stored split across two Yjs containers (see
// `useYjsExcalidraw` on the SPA side):
//   - doc.getArray("elements") — one Y.Map per Excalidraw element
//   - doc.getMap("appState")   — slimmed appState (theme, grid, etc.)
// We reconstruct the wire shape NapkinEditor.parseScene expects:
//   { type: "excalidraw", version: 2, source: "autonate", elements, appState }
const napkinMaterializer: Materializer = async (doc) => {
  const elementsArray = doc.getArray<Y.Map<unknown>>("elements");
  const appStateMap = doc.getMap<unknown>("appState");

  const elements = elementsArray.toArray().map((yMap) => yMapToPojo(yMap));
  const appState = yMapToPojo(appStateMap);

  return JSON.stringify({
    type: "excalidraw",
    version: 2,
    source: "autonate",
    elements,
    appState
  });
};

// Documents (Phase 3+) use TipTap on the SPA side, which writes to a Yjs
// XmlFragment named "default" by default via the @tiptap/extension-
// collaboration extension. We render to a ProseMirror Node JSON tree
// (the same shape TipTap's `editor.getJSON()` produces) so the .NET
// mirror is human-readable AND directly re-hydratable by the editor when
// a cold-load needs to seed the Y.Doc from the body mirror.
//
// Fragment name MUST match what the SPA-side Collaboration extension
// uses — TipTap's default field name is "default"; we keep that here so
// the SPA's editor mount can stay at default config.
const documentMaterializer: Materializer = async (doc) => {
  const json = yDocToProsemirrorJSON(doc, "default");
  return JSON.stringify(json);
};

// draw.io diagrams use Y.Text holding the full mxfile XML — the editor
// emits whole-XML autosaves rather than character-level ops, so Y.Text is
// effectively a string container with eventual-consistency replace
// semantics. Wire shape mirrors DiagramEditor.parseXml's expectation:
//   { type: "drawio", version: 1, xml: "<mxfile>..." }
const diagramMaterializer: Materializer = async (doc) => {
  const xml = doc.getText("xml").toString();
  return JSON.stringify({
    type: "drawio",
    version: 1,
    xml
  });
};

function yMapToPojo(map: Y.Map<unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  map.forEach((value, key) => {
    out[key] = value;
  });
  return out;
}

// First-load seeding: when Hocuspocus opens a `page:` or `note:` Y.Doc for
// the first time (no row in `yjs_documents`) and the corresponding
// `pages.body_jsonb` / `notes.content_jsonb` mirror already has BlockNote
// blocks (typically because the page was created via the chatbot's
// `create_page_from_markdown` tool or the REST POST with a populated
// bodyJsonb), hydrate the Y.Doc from that mirror so the editor opens with
// content instead of a blank canvas. Returns true if seeding ran (caller
// should persist the seeded state to `yjs_documents`), false otherwise.
//
// Only `page:<uuid>` and `note:<uuid>` (richtext notes) are seeded — the
// drawing/diagram note kinds have different content shapes and aren't on
// the chatbot's create path.
export async function trySeedFromBodyMirror(
  pool: pg.Pool,
  documentName: string,
  targetDoc: Y.Doc
): Promise<boolean> {
  const sep = documentName.indexOf(":");
  if (sep <= 0) return false;
  const prefix = documentName.slice(0, sep);
  const id = documentName.slice(sep + 1);
  if (!/^[0-9a-f-]{36}$/i.test(id)) return false;

  let bodyJson: string | null = null;
  if (prefix === "page") {
    const r = await pool.query<{ body_jsonb: string | null }>(
      "SELECT body_jsonb::text AS body_jsonb FROM pages WHERE id = $1",
      [id]
    );
    if (r.rowCount === 1) bodyJson = r.rows[0].body_jsonb;
  } else if (prefix === "note") {
    const r = await pool.query<{ content_jsonb: string | null; note_kind: string }>(
      "SELECT content_jsonb::text AS content_jsonb, note_kind FROM notes WHERE id = $1",
      [id]
    );
    if (r.rowCount === 1 && r.rows[0].note_kind === "richtext") {
      bodyJson = r.rows[0].content_jsonb;
    }
  } else {
    return false;
  }
  if (!bodyJson) return false;

  let blocks: PartialBlock[];
  try {
    const parsed = JSON.parse(bodyJson);
    if (!Array.isArray(parsed) || parsed.length === 0) return false;
    blocks = parsed as PartialBlock[];
  } catch {
    return false;
  }

  // blocksToYDoc returns a fresh Y.Doc; we encode it as an update and apply
  // it to the target doc that Hocuspocus handed us. Fragment name MUST
  // match the SPA-side `useBlockNoteWithYjs` ("document-store").
  const seedDoc = serverBlockNoteEditor.blocksToYDoc(blocks, "document-store");
  Y.applyUpdate(targetDoc, Y.encodeStateAsUpdate(seedDoc));
  return true;
}

// Picks the right materializer for a given document name. Unknown
// prefixes return null so the webhook handler can warn-and-skip rather
// than blindly POSTing an empty/wrong payload to .NET.
export function selectMaterializer(documentName: string): Materializer | null {
  const sep = documentName.indexOf(":");
  if (sep <= 0) return null;
  const prefix = documentName.slice(0, sep);
  switch (prefix) {
    case "page":
    case "note":
      return blockNoteMaterializer;
    case "napkin":
      return napkinMaterializer;
    case "diagram":
      return diagramMaterializer;
    case "documents":
      return documentMaterializer;
    case "pagemeta":
      // Live notes-list metadata for a page. Source of truth lives in
      // the `notes` table; pagemeta is a Yjs sync channel only. No DB
      // mirror → returning null tells the webhook caller to skip.
      return null;
    default:
      return null;
  }
}
