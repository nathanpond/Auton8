package com.autonate.flowableevents;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.io.IOException;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

final class DaprWorkflowEventPublisher {

    private static final Logger Logger = LoggerFactory.getLogger(DaprWorkflowEventPublisher.class);

    private final HttpClient httpClient;
    private final ObjectMapper objectMapper;
    private final URI publishBaseUrl;
    private final String pubsubName;

    DaprWorkflowEventPublisher(FlowableExecutionEventProperties properties) {
        this(HttpClient.newHttpClient(), defaultObjectMapper(), properties);
    }

    DaprWorkflowEventPublisher(
        HttpClient httpClient,
        ObjectMapper objectMapper,
        FlowableExecutionEventProperties properties
    ) {
        this.httpClient = httpClient;
        this.objectMapper = objectMapper;
        this.publishBaseUrl = properties.getDaprPublishBaseUrl();
        this.pubsubName = properties.getPubsubName();
    }

    void publish(WorkflowExecutionEventEnvelope event) {
        var topic = URLEncoder.encode(event.topic(), StandardCharsets.UTF_8);
        var pubsub = URLEncoder.encode(pubsubName, StandardCharsets.UTF_8);
        var publishUri = publishBaseUrl.resolve("/v1.0/publish/" + pubsub + "/" + topic + "?metadata.rawPayload=true");

        try {
            var payload = objectMapper.writeValueAsBytes(event.payload());
            var request = HttpRequest.newBuilder(publishUri)
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofByteArray(payload))
                .build();

            var response = httpClient.send(request, HttpResponse.BodyHandlers.discarding());
            var statusCode = response.statusCode();
            if (statusCode != HttpURLConnection.HTTP_NO_CONTENT && statusCode != HttpURLConnection.HTTP_OK) {
                Logger.warn(
                    "Dapr publish returned HTTP {} for workflow event topic '{}'.",
                    statusCode,
                    event.topic()
                );
            }
        } catch (IOException exception) {
            Logger.warn("Failed to serialize workflow execution event for topic '{}'.", event.topic(), exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            Logger.warn("Interrupted while publishing workflow execution event for topic '{}'.", event.topic(), exception);
        } catch (RuntimeException exception) {
            Logger.warn("Failed to publish workflow execution event for topic '{}'.", event.topic(), exception);
        }
    }

    private static ObjectMapper defaultObjectMapper() {
        return new ObjectMapper().registerModule(new JavaTimeModule());
    }
}
