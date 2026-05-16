import * as Y from "yjs";
import { ServerBlockNoteEditor } from "@blocknote/server-util";

// Each materializer reads a Yjs document and produces the JSON string that
// .NET will store in `body_jsonb` (pages) or `content_jsonb` (notes). The
// snapshot is a derived view of the live Y.Doc — clients still talk to
// Hocuspocus directly for live edits; the mirror exists for read paths
// (HistoryModal, future search/PDF export, etc).
//
// Per-prefix routing lives in `selectMaterializer` below. webhook.ts
// dispatches on the document-name prefix and calls the matching one.

export type Materializer = (doc: Y.Doc) => Promise<string>;

const serverBlockNoteEditor = ServerBlockNoteEditor.create();

// BlockNote-backed pages and richtext notes share the same materialization:
// Yjs XmlFragment → BlockNote blocks (server-side via @blocknote/server-util).
const blockNoteMaterializer: Materializer = async (doc) => {
  const blocks = await serverBlockNoteEditor.yDocToBlocks(doc);
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
    case "pagemeta":
      // Live notes-list metadata for a page. Source of truth lives in
      // the `notes` table; pagemeta is a Yjs sync channel only. No DB
      // mirror → returning null tells the webhook caller to skip.
      return null;
    default:
      return null;
  }
}
