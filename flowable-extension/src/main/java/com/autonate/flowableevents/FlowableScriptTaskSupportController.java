package com.autonate.flowableevents;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
final class FlowableScriptTaskSupportController {

    private final FlowableScriptTaskSupportService scriptTaskSupportService;

    FlowableScriptTaskSupportController(FlowableScriptTaskSupportService scriptTaskSupportService) {
        this.scriptTaskSupportService = scriptTaskSupportService;
    }

    @GetMapping("/service/autonate/script-task-support")
    FlowableScriptTaskSupportResponse getScriptTaskSupport() {
        return scriptTaskSupportService.describeSupport();
    }
}
