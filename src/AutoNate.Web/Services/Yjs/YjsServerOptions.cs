namespace AutoNate.Web.Services.Yjs;

// Configuration for the Yjs / Hocuspocus integration. Bound to the
// "YjsServer" section. The shared secret protects three things:
//   - Hocuspocus authenticating its callbacks to .NET
//     (X-AutoNate-Internal-Token, mirrors the workflow-behavior pattern)
//   - .NET signing tickets handed to the browser
//     (HMAC-SHA256 over the ticket payload)
//   - Hocuspocus signing outbound webhook payloads to .NET
//     (X-AutoNate-Yjs-Signature: sha256=<hex>)
//
// Production deployments must set this out-of-band. Development falls back
// to a fixed dev string so the dev loop "just works."
public sealed class YjsServerOptions
{
    public const string SectionName = "YjsServer";

    public string? InternalSharedSecret { get; set; }

    // WebSocket URL the browser passes to HocuspocusProvider. Provided
    // back to clients via /api/yjs/ticket so the SPA doesn't hardcode it.
    public string HocuspocusWsUrl { get; set; } = "ws://localhost:1234";

    // Ticket lifetime in seconds. Short on purpose — tickets are
    // single-use too, so a 60-second window plus jti-based replay
    // protection is the threat-model story.
    public int TicketTtlSeconds { get; set; } = 60;
}
