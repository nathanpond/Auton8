namespace AutoNate.Web.Persistence.Scaffolded;

// User-authored transformer or analyzer (Phase 6 of the Data Stores plan).
// The actual code runs in `services/executor/` (Node.js sidecar) under a
// V8 isolate for JS or Pyodide WASM for Python by default. `IsUnsafe`
// flips the runtime to host-side CPython (pandas/numpy) and gates the
// row behind the `transformer:executeunsafe` permission.
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

    // True = run in host CPython (pandas/numpy available, no sandbox).
    // Gated by `executeunsafe`. The SPA shows a "Trusted" badge when set
    // and forces re-approval on every code edit.
    public bool IsUnsafe { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
