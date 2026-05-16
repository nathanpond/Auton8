import type { Extension } from "@hocuspocus/server";
import * as Y from "yjs";
import pg from "pg";

// Postgres-backed persistence for Y.Doc binary state. Matches the
// `yjs_documents` table created by AutoNate's DatabaseSchemaInitializer —
// same Postgres cluster, no separate schema. We're the only writer to
// this table; .NET ignores it during normal operation.
//
// Plugged into the Hocuspocus extensions list. Hocuspocus calls
// onLoadDocument once per first-collaborator-on-doc (cold start) and
// onStoreDocument debounced after each change (default 2s).
export function createPostgresPersistence(config: pg.PoolConfig): Extension & {
  shutdown(): Promise<void>;
} {
  const pool = new pg.Pool(config);

  // Smoke-test the connection up front so a misconfigured POSTGRES_*
  // shows in the boot log instead of silently failing on first write.
  pool.query("SELECT 1").catch((err) => {
    console.error("[persistence] FAILED to connect to Postgres:", err);
  });

  return {
    async onLoadDocument(data) {
      try {
        const result = await pool.query<{ data: Buffer }>(
          "SELECT data FROM yjs_documents WHERE name = $1",
          [data.documentName]
        );
        if (result.rowCount === 0) return null;
        const buf = result.rows[0].data;
        // Copy the BYTEA bytes into a fresh Uint8Array. Node's pg driver
        // can return Buffers backed by pooled allocations; passing those
        // directly to Y.applyUpdate has been known to misbehave when the
        // pool reuses the underlying memory.
        Y.applyUpdate(data.document, Uint8Array.from(buf));
        return null;
      } catch (err) {
        console.error(
          `[persistence] onLoadDocument(${data.documentName}) threw:`,
          err
        );
        // Re-throw so Hocuspocus refuses the connection rather than
        // serving an empty doc that would overwrite the saved state.
        throw err;
      }
    },

    async onStoreDocument(data) {
      try {
        const state = Buffer.from(Y.encodeStateAsUpdate(data.document));
        await pool.query(
          `INSERT INTO yjs_documents (name, data, updated_at_utc)
           VALUES ($1, $2, NOW())
           ON CONFLICT (name) DO UPDATE
             SET data = EXCLUDED.data, updated_at_utc = NOW()`,
          [data.documentName, state]
        );
      } catch (err) {
        console.error(
          `[persistence] onStoreDocument(${data.documentName}) threw:`,
          err
        );
        throw err;
      }
    },

    async shutdown() {
      await pool.end();
    }
  };
}
