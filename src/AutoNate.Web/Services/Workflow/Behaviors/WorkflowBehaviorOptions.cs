namespace AutoNate.Web.Services.Workflow.Behaviors;

// Configuration for the workflow-behavior callback endpoint. The Flowable
// JavaDelegate POSTs `/api/workflow-behaviors/{key}/execute` with this
// shared secret in `X-AutoNate-Internal-Token`; the SharedSecretEndpointFilter
// rejects mismatches with 401.
//
// Production deployments must populate CallbackSharedSecret out-of-band
// (env var, k8s secret) and the same value goes into the JVM's
// `autonate.flowable-events.callback-shared-secret` Spring property.
// Startup refuses to run in non-Development environments when this is
// blank, so a misconfigured deploy fails loudly instead of silently
// accepting unauthenticated calls.
public sealed class WorkflowBehaviorOptions
{
    public const string SectionName = "WorkflowBehaviors";

    public string? CallbackSharedSecret { get; set; }
}
