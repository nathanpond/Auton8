namespace AutoNate.Web.Services.Transformers;

// Declarative config schema for a Transformer / Analyzer node. The
// pipeline editor's node drawer reads this to render per-builtin form
// fields instead of a freeform JSON Textarea — the central ergonomic
// blocker called out by audit fix archived-7. Plugin-contributed transformers
// can omit the schema and the editor falls back to the JSON view.
//
// Type vocabulary is intentionally narrow so the SPA can map each to a
// concrete Mantine control without per-kind plumbing:
//   - "text"     → <TextInput>
//   - "number"   → <NumberInput> (integer or float)
//   - "boolean"  → <Switch>
//   - "select"   → <NativeSelect> with `Options`
//   - "columns"  → <TextInput> (comma-separated column names; matches
//                  the runtime's DataFrameOps.SplitColumnList)
public sealed record class ConfigFieldSchema(
    string Name,
    string Label,
    string Type,
    bool Required,
    string? Description,
    string? DefaultValue,
    string? Placeholder,
    IReadOnlyList<string>? Options);

public sealed record class TransformerConfigSchema(
    string Key,
    string DisplayName,
    IReadOnlyList<ConfigFieldSchema> Fields);
