import { api } from "@/api/client";

// "editor" → full read/write Y.Doc connection. "viewer" → Hocuspocus opens
// the socket as readOnly and rejects any write message. .NET decides the
// role from the live Page.Edit grant; the SPA uses it to render read-only
// editor chrome up front.
export type YjsRole = "editor" | "viewer";

// Short-lived single-use ticket the SPA hands to HocuspocusProvider via the
// `token` field. Hocuspocus's onAuthenticate hook hands the same ticket
// back to .NET (POST /internal/yjs-auth) which validates the HMAC, consumes
// the jti, and re-runs ContentAuthorizer before letting the connection in.
export interface YjsTicket {
  ticket: string;
  wsUrl: string;
  expiresInSeconds: number;
  role: YjsRole;
}

export async function fetchTicket(documentName: string): Promise<YjsTicket> {
  const { data } = await api.post<YjsTicket>("/api/yjs/ticket", { documentName });
  return data;
}
