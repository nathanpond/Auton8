namespace AutoNate.Web.Persistence.Scaffolded;

// User-authored transformer or analyzer (Phase 6 of the Data Stores plan).
// The actual code runs in `services/executor/` (Node.js sidecar) under a
// V8 isolate for JS or Pyodide WASM for Python — always, with no opt-out.
//
// An `IsUnsafe` flag used to sit here, gated by a `transformer:executeunsafe`
// permission and described as flipping the runtime to host-side CPython. That
// runner was never built, so the flag was inert for its whole life. Removed in
// #190 rather than left as a permission protecting nothing.
public partial class CodeTransformer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    // "transformer" | "analyzer"
    public string Kind { get; set; } = "transformer";

    // "js" | "python"
    public string Language { get; set; } = "js";

    // The author's source code. JS expects a function called `transform`
    // that takes (inputs[], config) and returns rows; Python expects a
    // function with the same signature.
    public string Code { get; set; } = string.Empty;

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
