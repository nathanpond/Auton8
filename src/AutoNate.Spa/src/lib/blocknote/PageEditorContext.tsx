import { createContext, useContext } from "react";

// Depth-tracking context for the noteEmbed block's recursion guard. The
// top-level page editor provides 0; each nested embed renders an inner
// provider with `depth + 1`. The block renderer refuses to render content
// when depth > 0 — preventing A→B→A loops and pathological fan-out from
// users daisy-chaining embeds.
//
// (Previously this file also exposed a PageEditorContext for the
// page id + editable signal — that's now in pageEditorSignal.ts because
// Tiptap's NodeView doesn't reliably inherit React context.)
export const EmbedDepthContext = createContext<number>(0);

export function useEmbedDepth(): number {
  return useContext(EmbedDepthContext);
}
