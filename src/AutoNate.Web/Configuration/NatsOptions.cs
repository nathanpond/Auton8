namespace AutoNate.Web.Configuration;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    // NATS server URL (e.g. nats://localhost:4222). When empty, the app skips
    // JetStream provisioning at startup — useful for tests or for deployments
    // where streams are managed externally.
    public string Url { get; set; } = string.Empty;
}
