import type { onAuthenticatePayload } from "@hocuspocus/server";

export interface AuthConfig {
  autonateBaseUrl: string;
  sharedSecret: string;
}

export interface AutoNateUser {
  id: string;
  name: string;
}

// Hocuspocus's onAuthenticate hook fires once per WebSocket connect. We
// don't decide authorization here — we ask .NET. Tickets are HMAC-signed
// by .NET, jti-tracked for single use, and validated against the live
// ContentAuthorizer state. Hocuspocus is the sync edge; .NET is the
// source of truth.
//
// Returning user context propagates into the webhook payload via the
// context object Hocuspocus passes to onStoreDocument.
export function createAuthHook(config: AuthConfig) {
  return async function onAuthenticate(
    payload: onAuthenticatePayload
  ): Promise<{ user: AutoNateUser }> {
    const response = await fetch(`${config.autonateBaseUrl}/internal/yjs-auth`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-AutoNate-Internal-Token": config.sharedSecret
      },
      body: JSON.stringify({
        token: payload.token,
        documentName: payload.documentName
      })
    });

    if (!response.ok) {
      // Hocuspocus translates a throw inside onAuthenticate into a clean
      // connection-refused. We don't leak the upstream status here; the
      // browser only ever sees "could not connect."
      const bodyText = await response.text().catch(() => "");
      console.error(
        `[auth] yjs-auth rejected for ${payload.documentName}: HTTP ${response.status}. body=${bodyText}`
      );
      throw new Error(
        `Yjs auth rejected (status ${response.status}) for ${payload.documentName}.`
      );
    }

    const data = (await response.json()) as {
      userId: string;
      displayName: string;
      role: string;
    };

    // Hocuspocus protocol: setting `connectionConfig.readOnly = true`
    // here (before returning) causes the server to reject every WRITE
    // message from this socket while still letting it sync state. .NET
    // decides the role from live Page.Edit; we just enforce.
    if (data.role === "viewer") {
      payload.connectionConfig.readOnly = true;
    }

    return {
      user: { id: data.userId, name: data.displayName }
    };
  };
}
