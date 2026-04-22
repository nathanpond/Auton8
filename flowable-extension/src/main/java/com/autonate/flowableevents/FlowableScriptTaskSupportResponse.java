package com.autonate.flowableevents;

import java.util.List;

record FlowableScriptTaskSupportResponse(
    boolean javaScriptSupported,
    List<String> engineNames
) {
}
