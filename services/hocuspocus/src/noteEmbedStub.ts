import { createBlockSpec } from "@blocknote/core";

// Server-side stub for the SPA's `noteEmbed` page block. The sidecar
// never renders content — its job is purely to materialize Y.Docs to
// JSON — but the schema MUST still recognize the node type, because
// y-prosemirror's `yXmlFragmentToProseMirrorRootNode` silently deletes
// any Y.XmlElement whose `nodeName` isn't in the ProseMirror schema
// (see y-prosemirror's `createNodeFromYElement`: an unknown node name
// makes `schema.node(...)` throw, which y-prosemirror handles by
// `el._item.delete(transaction)` — and that delete then propagates
// back to every connected client as a "noteEmbed disappeared" update).
//
// The render function isn't called during materialization but
// createBlockSpec requires it. Return an empty span so it's a valid
// BlockImplementation even if it somehow does get called (e.g. via
// blocksToFullHTML on the server).
export const noteEmbedServerSpec = createBlockSpec(
  {
    type: "noteEmbed",
    propSchema: {
      noteId: { default: "" }
    },
    content: "none"
  },
  {
    meta: { isolating: false, defining: false },
    render: () => ({
      dom: (globalThis as { document?: Document }).document?.createElement("span")
        ?? ({ tagName: "SPAN" } as unknown as HTMLElement)
    })
  }
);
