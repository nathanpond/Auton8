using System.Text.Json;

namespace AutoNate.Plugins.Abstractions;

// Snapshot of a Flowable execution at the moment a service task fires.
// Constructed on the Java side from DelegateExecution and posted as JSON;
// AutoNate.Web deserializes it before invoking the matching behavior.
//
// `Variables` is a JsonElement map rather than `object?` so behaviors
// explicitly choose how to decode each value (`GetInt64`, `GetString`,
// `Deserialize<T>`). Java-side variable types that don't round-trip
// safely through JSON (Java `serializable` byte streams, untyped POJOs)
// are filtered out at the Flowable bridge with a warning log.
public sealed record BehaviorContext(
    string ProcessInstanceId,
    string ExecutionId,
    string ProcessDefinitionKey,
    string? ProcessName,
    string ActivityId,
    string? BusinessKey,
    string CorrelationId,
    IReadOnlyDictionary<string, JsonElement> Variables);
