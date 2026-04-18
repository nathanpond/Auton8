namespace AutoNate.Web.Configuration;

public sealed class FlowableOptions
{
    public const string SectionName = "Flowable";

    public string BaseUrl { get; set; } = string.Empty;
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
