package com.autonate.flowableevents;

import java.net.URI;
import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "autonate.flowable-events")
public class FlowableExecutionEventProperties {

    private URI daprPublishBaseUrl = URI.create("http://127.0.0.1:3500");

    private String pubsubName = "pubsub";

    private String topicRoot = "workflow.execution.events";

    private String sourceAppId = "flowable";

    // Direct (non-Dapr) HTTP target for the workflow-behavior callback. Set
    // to AutoNate.Web's externally reachable base URL (e.g.
    // http://host.docker.internal:5040 in dev). When unset the
    // AutoNateBehaviorDelegate refuses to execute, surfacing as a
    // FlowableException so the engine retries — better than silently
    // pointing at a wrong URL.
    private URI callbackBaseUrl;

    // Shared secret matching WorkflowBehaviors:CallbackSharedSecret on the
    // AutoNate.Web side. Both must be present and equal for the callback
    // endpoint to accept the request.
    private String callbackSharedSecret;

    private int behaviorTimeoutSeconds = 30;

    public URI getDaprPublishBaseUrl() {
        return daprPublishBaseUrl;
    }

    public void setDaprPublishBaseUrl(URI daprPublishBaseUrl) {
        this.daprPublishBaseUrl = daprPublishBaseUrl;
    }

    public String getPubsubName() {
        return pubsubName;
    }

    public void setPubsubName(String pubsubName) {
        this.pubsubName = pubsubName;
    }

    public String getTopicRoot() {
        return topicRoot;
    }

    public void setTopicRoot(String topicRoot) {
        this.topicRoot = topicRoot;
    }

    public String getSourceAppId() {
        return sourceAppId;
    }

    public void setSourceAppId(String sourceAppId) {
        this.sourceAppId = sourceAppId;
    }

    public URI getCallbackBaseUrl() {
        return callbackBaseUrl;
    }

    public void setCallbackBaseUrl(URI callbackBaseUrl) {
        this.callbackBaseUrl = callbackBaseUrl;
    }

    public String getCallbackSharedSecret() {
        return callbackSharedSecret;
    }

    public void setCallbackSharedSecret(String callbackSharedSecret) {
        this.callbackSharedSecret = callbackSharedSecret;
    }

    public int getBehaviorTimeoutSeconds() {
        return behaviorTimeoutSeconds;
    }

    public void setBehaviorTimeoutSeconds(int behaviorTimeoutSeconds) {
        this.behaviorTimeoutSeconds = behaviorTimeoutSeconds;
    }
}
