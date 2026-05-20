import { BlockNoteSchema, defaultBlockSpecs } from "@blocknote/core";
import { noteEmbedBlock, NOTE_EMBED_BLOCK_TYPE } from "./noteEmbedBlock";

// Schema used by the page editor only. Richtext notes keep BlockNote's
// default schema (see useBlockNoteWithYjs) — the noteEmbed block is
// meaningful only inside a page, where same-page notes can be referenced.
//
// Derived from the document name prefix (`page:` vs `note:`) inside
// useBlockNoteWithYjs to keep the schema selection rooted in a single
// source of truth: there is no separate flag a caller can forget to
// flip. If a richtext note ever became a page-style editor in the
// future, the doc-name prefix is the place to gate that.
export const pageBlockNoteSchema = BlockNoteSchema.create({
  blockSpecs: {
    ...defaultBlockSpecs,
    // createReactBlockSpec returns a factory `(opts?) => BlockSpec`; the
    // schema map wants the resolved spec, so invoke it once at module
    // load. The block takes no options.
    [NOTE_EMBED_BLOCK_TYPE]: noteEmbedBlock()
  }
});

export { NOTE_EMBED_BLOCK_TYPE };
