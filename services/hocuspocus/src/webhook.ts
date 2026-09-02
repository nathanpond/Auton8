import { createHmac } from "node:crypto";
import type { Extension } from "@hocuspocus/server";
import type { AutoNateUser } from "./auth.js";
import { selectMaterializer } from "./materializers.js";

// Cross-service calls to the .NET host get an explicit budget; undici's
// default is 300 s, which is indistinguishable from a hang (archived-75).
const AUTONATE_FETCH_TIMEOUT_MS = 5_000;

export interface WebhookConfig {
  autonateBaseUrl: string;
  sharedSecret: string;
}

interface ContextWithUser {
  user?: AutoNateUser;
}

// Posts snapshot updates to .NET after every (debounced) save. The payload
// carries a materialized JSON string in `bodyJsonb` — its shape depends on
// the document's prefix (BlockNote blocks, Excalidraw scene, drawio XML);
// see materializers.ts. .NET stores the string verbatim without parsing
// it, so the sidecar owns the wire format end-to-end.
//
// Two HMACs are at play here:
//   - X-AutoNate-Internal-Token: the static shared secret that proves
//     the caller is the Hocuspocus sidecar (not a random WAN client).
//     Identical to the workflow-behavior callback pattern.
//   - X-AutoNate-Yjs-Signature: sha256(body, sharedSecret) — proves the
//     body wasn't tampered with in flight. Required because the body
//     carries the document snapshot that .NET writes verbatim.
export function createWebhookExtension(config: WebhookConfig): Extension {
  async function post(event: "change" | "disconnect", body: object): Promise<void> {
    const raw = JSON.stringify(body);
    const signature = createHmac("sha256", config.sharedSecret)
      .update(raw)
      .digest("hex");
    const response = await fetch(`${config.autonateBaseUrl}/internal/yjs-webhook`, {
      method: "POST",
      // Same budget as the auth hook — a wedged host must not pin this
      // request for undici's 300 s default (archived-75).
      signal: AbortSignal.timeout(AUTONATE_FETCH_TIMEOUT_MS),
      headers: {
        "Content-Type": "application/json",
        "X-AutoNate-Internal-Token": config.sharedSecret,
        "X-AutoNate-Yjs-Signature": `sha256=${signature}`
      },
      body: raw
    });
    if (!response.ok) {
      // Bubble up so Hocuspocus logs the failure. We don't retry here —
      // the next onStoreDocument fires on the next edit, which acts as a
      // natural retry. For a sustained outage we accept snapshot staleness;
      // the Y.Doc remains the source of truth.
      const bodyText = await response.text().catch(() => "");
      console.error(
        `[webhook] ${event} for ${(body as { documentName?: string }).documentName} failed: HTTP ${response.status}. body=${bodyText}`
      );
      throw new Error(
        `Yjs webhook for ${event} ${(body as { documentName?: string }).documentName} ` +
        `failed: HTTP ${response.status}`
      );
    }
  }

  return {
    async onStoreDocument(data) {
      const materializer = selectMaterializer(data.documentName);
      if (!materializer) {
        console.warn(
          `[webhook] onStoreDocument: no materializer for ${data.documentName}; skipping snapshot.`
        );
        return;
      }
      const bodyJsonb = await materializer(data.document);
      // onStoreDocument carries `lastContext` (the context from the last
      // change), not `context` — that's what the @hocuspocus/server types
      // expose. The auth hook populates user.id from the validated ticket.
      const ctx = (data.lastContext ?? {}) as ContextWithUser;
      await post("change", {
        event: "change",
        documentName: data.documentName,
        userId: ctx.user?.id ?? null,
        bodyJsonb
      });
    },

    async onDisconnect(data) {
      const ctx = (data.context ?? {}) as ContextWithUser;
      // No body for disconnect — .NET only logs it in Phase 1. Future
      // phases (comments / presence) will hang real work off this event.
      //
      // Swallow failures: @hocuspocus/server does not catch errors thrown
      // from onDisconnect, so a rejected fetch (typical when .NET shuts
      // down mid-session) becomes an unhandled rejection and kills the
      // Node process. There's no document state to lose on disconnect.
      try {
        await post("disconnect", {
          event: "disconnect",
          documentName: data.documentName,
          userId: ctx.user?.id ?? null,
          bodyJsonb: null
        });
      } catch (err) {
        console.error(
          `[webhook] onDisconnect for ${data.documentName} failed; ignoring:`,
          err instanceof Error ? err.message : err
        );
      }
    }
  };
}
