using System.Text.Json;

namespace AutoNate.Web.Services.Flowable.Cache;

// Source-side payload for the variable projection: a complete snapshot of
// every variable on one process instance. The projection deletes the
// instance's existing rows and re-inserts from this snapshot, so absence
// from the dictionary means "this variable was removed in Flowable."
public sealed record class FlowableInstanceVariables(
    string FlowableInstanceId,
    IReadOnlyDictionary<string, JsonElement> Variables);
