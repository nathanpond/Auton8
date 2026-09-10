namespace AutoNate.Web.Configuration;

public sealed class FlowableOptions
{
    public const string SectionName = "Flowable";

    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Prefix stamped onto deployment names, so test deployments are identifiable
    /// by name (#257).
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Unset in production**, where a deployment is named after its process key
    /// exactly as before.
    /// </para>
    /// <para>
    /// It exists because #214's cleanup guard was "the suite's own naming
    /// convention, not a heuristic" — and the convention it named
    /// (<c>e2e-</c>) was one nothing produced, so the sweep removed nothing for
    /// the whole of M4 while the engine reached 1,306 deployments. The repair
    /// shipped in #257 swept by AGE instead, which removed the orphans and also
    /// removed the guard: it deletes a developer's own three-hour-old work, while
    /// two doc comments still promised it could not.
    /// </para>
    /// <para>
    /// Giving the suite a prefix it really emits restores the original property
    /// rather than trading it away — the sweep can go back to matching only what
    /// the suite deployed, and a developer's `autonate`, `car` or `account`
    /// deployment is untouched at any age.
    /// </para>
    /// </remarks>
    public string? DeploymentNamePrefix { get; set; }
}

public sealed class DaprOptions
{
    public const string SectionName = "Dapr";

    public string AppId { get; set; } = string.Empty;

    public string HttpEndpoint { get; set; } = string.Empty;

    public string GrpcEndpoint { get; set; } = string.Empty;

    public string PlacementHostAddress { get; set; } = string.Empty;

    public string SchedulerHostAddress { get; set; } = string.Empty;

    public string StateStoreName { get; set; } = string.Empty;

    public string PubSubName { get; set; } = string.Empty;
}
