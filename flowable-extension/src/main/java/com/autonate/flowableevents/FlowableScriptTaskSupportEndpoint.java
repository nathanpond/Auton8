package com.autonate.flowableevents;

import org.springframework.boot.actuate.endpoint.annotation.Endpoint;
import org.springframework.boot.actuate.endpoint.annotation.ReadOperation;

@Endpoint(id = "scriptTaskSupport")
final class FlowableScriptTaskSupportEndpoint {

    private final FlowableScriptTaskSupportService scriptTaskSupportService;

    FlowableScriptTaskSupportEndpoint(FlowableScriptTaskSupportService scriptTaskSupportService) {
        this.scriptTaskSupportService = scriptTaskSupportService;
    }

    @ReadOperation
    FlowableScriptTaskSupportResponse describeSupport() {
        return scriptTaskSupportService.describeSupport();
    }
}
