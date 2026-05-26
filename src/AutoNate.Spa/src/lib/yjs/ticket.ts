import { api } from "@/api/client";

// "editor" → full read/write Y.Doc connection.
// "commenter" → Hocuspocus opens readOnly; the SPA puts docx-editor in
//   mode='viewing' so the body is locked but the comments sidebar (which
//   talks to REST, not Yjs) stays interactive. Only emitted for the
//   `documents:` prefix (Phase 4); pages/notes still flip editor↔viewer.
// "viewer" → readOnly everywhere; no body edits, no comments.
//
// .NET decides the role from the live Document/Page grants; the SPA uses
// it to render read-only chrome up front.
export type YjsRole = "editor" | "commenter" | "viewer";

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
