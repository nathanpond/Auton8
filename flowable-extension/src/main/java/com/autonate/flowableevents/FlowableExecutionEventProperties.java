package com.autonate.flowableevents;

import java.net.URI;
import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "autonate.flowable-events")
public class FlowableExecutionEventProperties {

    private URI daprPublishBaseUrl = URI.create("http://127.0.0.1:3500");

    private String pubsubName = "pubsub";

    private String topicRoot = "workflow.execution";

    private String sourceAppId = "flowable";

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
}
