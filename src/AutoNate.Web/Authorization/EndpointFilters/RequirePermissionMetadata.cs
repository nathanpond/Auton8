namespace AutoNate.Web.Authorization.EndpointFilters;

// Marker attached by RequirePermission / RequireKindPermission so the
// authorization gate-presence test can find every gated endpoint via
// EndpointDataSource without trying to introspect the runtime filter chain
// (which ASP.NET Core doesn't expose). Also surfaces the gate in audit
// tooling — `IsKindLevel == true` means the filter is the kind-level
// variant (no per-instance id resolution).
public sealed class RequirePermissionMetadata
{
    public RequirePermissionMetadata(string kind, string action, bool isKindLevel)
    {
        Kind = kind;
        Action = action;
        IsKindLevel = isKindLevel;
    }

    public string Kind { get; }

    public string Action { get; }

    public bool IsKindLevel { get; }
}
