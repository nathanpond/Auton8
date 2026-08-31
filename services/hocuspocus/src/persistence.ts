import type { Extension } from "@hocuspocus/server";
import * as Y from "yjs";
import pg from "pg";
import { trySeedFromBodyMirror } from "./materializers.js";

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

  // node-postgres emits 'error' on the Pool when a backend error or a network
  // partition kills an *idle* client. An EventEmitter 'error' with no listener
  // is rethrown as an uncaughtException, which index.ts survives but only by
  // abandoning whatever hook was mid-flight — and it repeats on every idle
  // client death. Logging it keeps the pool's own reconnect behaviour and
  // turns a process-level event into a line in the log (#74).
  pool.on("error", (err) => {
    console.error("[persistence] idle client error:", err);
  });

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
        if (result.rowCount === 0) {
          // First open of this doc — see if the `pages.body_jsonb` /
          // `notes.content_jsonb` mirror has pre-existing block content
          // (e.g. a chatbot-created page whose markdown was already
          // rendered to BlockNote JSON at create time). If so, hydrate
          // the Y.Doc from the mirror and persist the encoded state so
          // subsequent loads are O(1) and the autosave debounce doesn't
          // need to fire first.
          let seeded = false;
          try {
            seeded = await trySeedFromBodyMirror(pool, data.documentName, data.document);
          } catch (seedErr) {
            console.error(
              `[persistence] seed-from-mirror(${data.documentName}) failed:`,
              seedErr
            );
          }
          if (seeded) {
            try {
              const state = Buffer.from(Y.encodeStateAsUpdate(data.document));
              await pool.query(
                `INSERT INTO yjs_documents (name, data, updated_at_utc)
                 VALUES ($1, $2, NOW())
                 ON CONFLICT (name) DO NOTHING`,
                [data.documentName, state]
              );
            } catch (writeErr) {
              // Persisting the seed is an optimization, not a correctness
              // requirement — if it fails, the autosave path will still
              // store on the first edit. Log and move on.
              console.error(
                `[persistence] persisting seeded state for ${data.documentName} failed:`,
                writeErr
              );
            }
          }
          return null;
        }
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
