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

    // #223. Where the ENGINE should call back to, when that differs from the
    // engine's own configuration.
    //
    // Unset in production, and nothing is stamped — every diagram falls through
    // to the callback URL Flowable is configured with. It exists for the E2E
    // suite, where Flowable's configured URL reaches the app in the autonate-web
    // container while the tests drive their own app against a different database:
    // a behaviour invoked by a workflow the test published executed somewhere that
    // workflow did not exist, so no behaviour could be verified end to end.
    //
    // Stamped onto the DEPLOYED copy only, like #112's expansion and #113's
    // version pinning; the stored diagram never carries it.
    public string? CallbackBaseUrlOverride { get; set; }
}
