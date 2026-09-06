const BPMN_MENU_ENTRIES = [
  {
    id: "create.start-event",
    label: "Start event",
    description: "Blank process start",
    group: "Events",
    type: "bpmn:StartEvent",
    className: "bpmn-icon-start-event-none"
  },
  {
    id: "create.start-event-message",
    label: "Message start event",
    description: "Starts on message",
    group: "Events",
    type: "bpmn:StartEvent",
    className: "bpmn-icon-start-event-message",
    options: { eventDefinitionType: "bpmn:MessageEventDefinition" }
  },
  {
    id: "create.start-event-timer",
    label: "Timer start event",
    description: "Starts on timer",
    group: "Events",
    type: "bpmn:StartEvent",
    className: "bpmn-icon-start-event-timer",
    options: { eventDefinitionType: "bpmn:TimerEventDefinition" }
  },
  {
    id: "create.start-event-signal",
    label: "Signal start event",
    description: "Starts on signal",
    group: "Events",
    type: "bpmn:StartEvent",
    className: "bpmn-icon-start-event-signal",
    options: { eventDefinitionType: "bpmn:SignalEventDefinition" }
  },
  {
    id: "create.start-event-conditional",
    label: "Conditional start event",
    description: "Starts on condition",
    group: "Events",
    type: "bpmn:StartEvent",
    className: "bpmn-icon-start-event-condition",
    options: { eventDefinitionType: "bpmn:ConditionalEventDefinition" }
  },
  {
    id: "create.intermediate-catch-message",
    label: "Message intermediate catch event",
    description: "Waits for message",
    group: "Events",
    type: "bpmn:IntermediateCatchEvent",
    className: "bpmn-icon-intermediate-event-catch-message",
    options: { eventDefinitionType: "bpmn:MessageEventDefinition" }
  },
  {
    id: "create.intermediate-catch-timer",
    label: "Timer intermediate catch event",
    description: "Waits for timer",
    group: "Events",
    type: "bpmn:IntermediateCatchEvent",
    className: "bpmn-icon-intermediate-event-catch-timer",
    options: { eventDefinitionType: "bpmn:TimerEventDefinition" }
  },
  {
    id: "create.intermediate-catch-signal",
    label: "Signal intermediate catch event",
    description: "Waits for signal",
    group: "Events",
    type: "bpmn:IntermediateCatchEvent",
    className: "bpmn-icon-intermediate-event-catch-signal",
    options: { eventDefinitionType: "bpmn:SignalEventDefinition" }
  },
  {
    id: "create.intermediate-catch-conditional",
    label: "Conditional intermediate catch event",
    description: "Waits for condition",
    group: "Events",
    type: "bpmn:IntermediateCatchEvent",
    className: "bpmn-icon-intermediate-event-catch-condition",
    options: { eventDefinitionType: "bpmn:ConditionalEventDefinition" }
  },
  {
    id: "create.intermediate-throw-none",
    label: "Intermediate throw event",
    description: "Plain throw event",
    group: "Events",
    type: "bpmn:IntermediateThrowEvent",
    className: "bpmn-icon-intermediate-event-none"
  },
  {
    id: "create.intermediate-throw-message",
    label: "Message intermediate throw event",
    description: "Throws message",
    group: "Events",
    type: "bpmn:IntermediateThrowEvent",
    className: "bpmn-icon-intermediate-event-throw-message",
    options: { eventDefinitionType: "bpmn:MessageEventDefinition" }
  },
  {
    id: "create.intermediate-throw-signal",
    label: "Signal intermediate throw event",
    description: "Throws signal",
    group: "Events",
    type: "bpmn:IntermediateThrowEvent",
    className: "bpmn-icon-intermediate-event-throw-signal",
    options: { eventDefinitionType: "bpmn:SignalEventDefinition" }
  },
  {
    id: "create.intermediate-throw-escalation",
    label: "Escalation intermediate throw event",
    description: "Throws escalation",
    group: "Events",
    type: "bpmn:IntermediateThrowEvent",
    className: "bpmn-icon-intermediate-event-throw-escalation",
    options: { eventDefinitionType: "bpmn:EscalationEventDefinition" }
  },
  {
    id: "create.end-event",
    label: "End event",
    description: "Blank process end",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-none"
  },
  {
    id: "create.end-event-message",
    label: "Message end event",
    description: "Ends with message",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-message",
    options: { eventDefinitionType: "bpmn:MessageEventDefinition" }
  },
  {
    id: "create.end-event-signal",
    label: "Signal end event",
    description: "Ends with signal",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-signal",
    options: { eventDefinitionType: "bpmn:SignalEventDefinition" }
  },
  {
    id: "create.end-event-escalation",
    label: "Escalation end event",
    description: "Ends with escalation",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-escalation",
    options: { eventDefinitionType: "bpmn:EscalationEventDefinition" }
  },
  {
    id: "create.end-event-error",
    label: "Error end event",
    description: "Ends with error",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-error",
    options: { eventDefinitionType: "bpmn:ErrorEventDefinition" }
  },
  {
    id: "create.end-event-cancel",
    label: "Cancel end event",
    description: "Ends with cancel",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-cancel",
    options: { eventDefinitionType: "bpmn:CancelEventDefinition" }
  },
  {
    id: "create.end-event-compensation",
    label: "Compensation end event",
    description: "Ends with compensation",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-compensation",
    options: { eventDefinitionType: "bpmn:CompensateEventDefinition" }
  },
  {
    id: "create.end-event-terminate",
    label: "Terminate end event",
    description: "Terminates process",
    group: "Events",
    type: "bpmn:EndEvent",
    className: "bpmn-icon-end-event-terminate",
    options: { eventDefinitionType: "bpmn:TerminateEventDefinition" }
  },
  {
    id: "create.task",
    label: "Task",
    description: "Generic task",
    group: "Tasks",
    type: "bpmn:Task",
    className: "bpmn-icon-task"
  },
  {
    id: "create.user-task",
    label: "User task",
    description: "Human task",
    group: "Tasks",
    type: "bpmn:UserTask",
    className: "bpmn-icon-user-task"
  },
  {
    id: "create.service-task",
    label: "Service task",
    description: "Automated service work",
    group: "Tasks",
    type: "bpmn:ServiceTask",
    className: "bpmn-icon-service-task"
  },
  {
    id: "create.script-task",
    label: "Script task",
    description: "Scripted step",
    group: "Tasks",
    type: "bpmn:ScriptTask",
    className: "bpmn-icon-script-task"
  },
  {
    id: "create.business-rule-task",
    label: "Business rule task",
    description: "Decision/rules step",
    group: "Tasks",
    type: "bpmn:BusinessRuleTask",
    className: "bpmn-icon-business-rule-task"
  },
  {
    id: "create.manual-task",
    label: "Manual task",
    description: "Offline manual step",
    group: "Tasks",
    type: "bpmn:ManualTask",
    className: "bpmn-icon-manual-task"
  },
  {
    id: "create.receive-task",
    label: "Receive task",
    description: "Waits to receive",
    group: "Tasks",
    type: "bpmn:ReceiveTask",
    className: "bpmn-icon-receive-task"
  },
  {
    id: "create.send-task",
    label: "Send task",
    description: "Sends message/work",
    group: "Tasks",
    type: "bpmn:SendTask",
    className: "bpmn-icon-send-task"
  },
  {
    id: "create.exclusive-gateway",
    label: "Exclusive gateway",
    description: "XOR decision",
    group: "Gateways",
    type: "bpmn:ExclusiveGateway",
    className: "bpmn-icon-gateway-xor"
  },
  {
    id: "create.parallel-gateway",
    label: "Parallel gateway",
    description: "Parallel split/join",
    group: "Gateways",
    type: "bpmn:ParallelGateway",
    className: "bpmn-icon-gateway-parallel"
  },
  {
    id: "create.inclusive-gateway",
    label: "Inclusive gateway",
    description: "OR decision",
    group: "Gateways",
    type: "bpmn:InclusiveGateway",
    className: "bpmn-icon-gateway-or"
  },
  {
    id: "create.event-based-gateway",
    label: "Event-based gateway",
    description: "Waits on event outcome",
    group: "Gateways",
    type: "bpmn:EventBasedGateway",
    className: "bpmn-icon-gateway-eventbased"
  },
  {
    id: "create.complex-gateway",
    label: "Complex gateway",
    description: "Advanced routing",
    group: "Gateways",
    type: "bpmn:ComplexGateway",
    className: "bpmn-icon-gateway-complex"
  },
  {
    id: "create.sub-process",
    label: "Expanded sub-process",
    description: "Nested process",
    group: "Containers",
    type: "bpmn:SubProcess",
    className: "bpmn-icon-subprocess-expanded",
    options: { isExpanded: true }
  },
  {
    id: "create.event-sub-process",
    label: "Event sub-process",
    description: "Triggered by event",
    group: "Containers",
    type: "bpmn:SubProcess",
    className: "bpmn-icon-event-subprocess-expanded",
    options: { isExpanded: true, triggeredByEvent: true }
  },
  {
    id: "create.transaction",
    label: "Transaction",
    description: "Transactional sub-process",
    group: "Containers",
    type: "bpmn:Transaction",
    className: "bpmn-icon-transaction"
  },
  {
    id: "create.call-activity",
    label: "Call activity",
    description: "Invokes another process",
    group: "Containers",
    type: "bpmn:CallActivity",
    className: "bpmn-icon-call-activity"
  },
  {
    id: "create.participant",
    label: "Participant pool",
    description: "Pool/participant",
    group: "Collaboration",
    className: "bpmn-icon-participant",
    createFactory: "participant"
  },
  {
    id: "create.data-object",
    label: "Data object reference",
    description: "Data object",
    group: "Artifacts",
    type: "bpmn:DataObjectReference",
    className: "bpmn-icon-data-object"
  },
  {
    id: "create.data-store",
    label: "Data store reference",
    description: "Data store",
    group: "Artifacts",
    type: "bpmn:DataStoreReference",
    className: "bpmn-icon-data-store"
  },
  {
    id: "create.text-annotation",
    label: "Text annotation",
    description: "Diagram note",
    group: "Artifacts",
    type: "bpmn:TextAnnotation",
    className: "bpmn-icon-text-annotation",
    appendable: false
  },
  {
    id: "create.group",
    label: "Group",
    description: "Visual grouping",
    group: "Artifacts",
    type: "bpmn:Group",
    className: "bpmn-icon-group",
    appendable: false
  },
  {
    id: "append.boundary-message",
    label: "Message boundary event",
    description: "Attached message boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-message",
    options: { eventDefinitionType: "bpmn:MessageEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-timer",
    label: "Timer boundary event",
    description: "Attached timer boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-timer",
    options: { eventDefinitionType: "bpmn:TimerEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-signal",
    label: "Signal boundary event",
    description: "Attached signal boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-signal",
    options: { eventDefinitionType: "bpmn:SignalEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-conditional",
    label: "Conditional boundary event",
    description: "Attached conditional boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-condition",
    options: { eventDefinitionType: "bpmn:ConditionalEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-error",
    label: "Error boundary event",
    description: "Attached error boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-error",
    options: { eventDefinitionType: "bpmn:ErrorEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-escalation",
    label: "Escalation boundary event",
    description: "Attached escalation boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-escalation",
    options: { eventDefinitionType: "bpmn:EscalationEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-cancel",
    label: "Cancel boundary event",
    description: "Attached cancel boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-cancel",
    options: { eventDefinitionType: "bpmn:CancelEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  },
  {
    id: "append.boundary-compensation",
    label: "Compensation boundary event",
    description: "Attached compensation boundary",
    group: "Boundary Events",
    type: "bpmn:BoundaryEvent",
    className: "bpmn-icon-intermediate-event-catch-compensation",
    options: { eventDefinitionType: "bpmn:CompensateEventDefinition" },
    createOnly: false,
    appendOnly: true,
    kind: "boundary"
  }
];

const MENU_GROUP_ORDER = [
  "Events",
  "Tasks",
  "Gateways",
  "Containers",
  "Collaboration",
  "Artifacts",
  "Boundary Events"
];

const WORKFLOW_JS_VERSION = "20260425_01";

export async function createModeler(container, xml, dotNetRef) {
  if (typeof window.BpmnJS === "undefined") {
    throw new Error("bpmn-js is not available on window.");
  }

  if (container && typeof container.replaceChildren === "function") {
    container.replaceChildren();
  }

  const modeler = new window.BpmnJS({
    container
  });
  const eventBus = modeler.get("eventBus", false);

  let suppressDirtyEvents = false;
  let lastImportDebug = null;
  const cssScopeAttribute = getCssScopeAttribute(container);
  const requestConfigure = async (element) => {
    if (!dotNetRef) {
      return;
    }

    await dotNetRef.invokeMethodAsync("RequestConfigureElement", describeElement(element));
  };

  modeler.on("commandStack.changed", async () => {
    if (suppressDirtyEvents || !dotNetRef) {
      return;
    }

    await dotNetRef.invokeMethodAsync("NotifyDiagramChanged");
  });

  suppressDirtyEvents = true;
  try {
    const importResult = await modeler.importXML(xml);
    fitAndCenter(modeler);
    refreshScriptIdentityMarkers({ modeler });
    lastImportDebug = buildImportDebug(modeler, container, importResult?.warnings ?? []);
  } finally {
    suppressDirtyEvents = false;
  }

  const configureMenu = createConfigureContextMenu(cssScopeAttribute);
  const elementRegistry = modeler.get("elementRegistry", false);

  // We listen at the DOM level rather than via eventBus("element.contextmenu")
  // because bpmn-js's delegated event filter only fires for targets matching
  // ".djs-element". Hovering a connection drops a bendpoints/segment-dragger
  // overlay on top of the line — those overlays carry data-element-id but
  // live in the .djs-overlays layer, so right-clicks on the connection never
  // reach the eventBus handler. The DOM listener climbs to the nearest
  // [data-element-id], which matches both shape groups and bendpoint
  // overlays, so right-click on edges works regardless of overlay coverage.
  const onContainerContextMenu = (originalEvent) => {
    const target = originalEvent.target instanceof Element ? originalEvent.target : null;
    if (!target) {
      configureMenu.hide();
      return;
    }

    const node = target.closest("[data-element-id]");
    if (!node) {
      configureMenu.hide();
      return;
    }

    const elementId = node.getAttribute("data-element-id");
    if (!elementId) {
      configureMenu.hide();
      return;
    }

    const element = elementRegistry?.get?.(elementId);
    const businessObject = element?.businessObject;
    if (!businessObject || typeof businessObject.$type !== "string") {
      configureMenu.hide();
      return;
    }

    // Right-clicking the canvas / pool / lane shouldn't surface "Configure…" —
    // those aren't routable to any of our editor modals.
    const $type = businessObject.$type;
    if ($type === "bpmn:Process"
      || $type === "bpmn:Collaboration"
      || $type === "bpmn:Participant"
      || $type === "bpmn:Lane"
      || $type === "bpmn:LaneSet") {
      configureMenu.hide();
      return;
    }

    originalEvent.preventDefault();
    originalEvent.stopPropagation();

    configureMenu.show({
      x: originalEvent.clientX,
      y: originalEvent.clientY,
      onConfigure: () => requestConfigure(element)
    });
  };

  const onCanvasClick = () => configureMenu.hide();
  const onCanvasViewboxChanged = () => configureMenu.hide();

  container?.addEventListener?.("contextmenu", onContainerContextMenu);
  eventBus?.on?.("canvas.click", onCanvasClick);
  eventBus?.on?.("canvas.viewbox.changed", onCanvasViewboxChanged);

  return {
    modeler,
    container,
    lastImportDebug,
    setSuppressDirtyEvents(value) {
      suppressDirtyEvents = value;
    },
    dispose() {
      container?.removeEventListener?.("contextmenu", onContainerContextMenu);
      eventBus?.off?.("canvas.click", onCanvasClick);
      eventBus?.off?.("canvas.viewbox.changed", onCanvasViewboxChanged);
      configureMenu.dispose();
      modeler.destroy();
    }
  };
}

export async function createReadonlyViewer(container, xml) {
  let contextMenu = null;
  let currentActivityIds = [];
  const cssScopeAttribute = getCssScopeAttribute(container);

  if (typeof window.BpmnJS === "undefined") {
    throw new Error("bpmn-js is not available on window.");
  }

  const ViewerCtor = window.BpmnJS.NavigatedViewer || window.BpmnJS.Viewer || window.BpmnJS;
  const viewer = new ViewerCtor({
    container
  });

  await viewer.importXML(xml);
  fitAndCenter(viewer);

  let hoverTooltip = null;

  return {
    viewer,
    activeMarkers: [],
    cssScopeAttribute,
    getCurrentActivityIds() {
      return [...currentActivityIds];
    },
    setCurrentActivityIds(activityIds) {
      currentActivityIds = Array.isArray(activityIds) ? [...activityIds] : [];
    },
    setContextMenu(nextContextMenu) {
      contextMenu = nextContextMenu;
    },
    setHoverTooltip(nextHoverTooltip) {
      hoverTooltip = nextHoverTooltip;
    },
    getHoverTooltip() {
      return hoverTooltip;
    },
    dispose() {
      contextMenu?.dispose?.();
      hoverTooltip?.dispose?.();
      viewer.destroy();
    }
  };
}

export async function saveXml(modelerHandle) {
  const popupMenu = modelerHandle.modeler.get("popupMenu", false);
  const directEditing = modelerHandle.modeler.get("directEditing", false);

  if (directEditing && typeof directEditing.complete === "function") {
    directEditing.complete();
  }

  if (popupMenu && typeof popupMenu.close === "function") {
    popupMenu.close();
  }

  await new Promise((resolve) => window.requestAnimationFrame(() => resolve()));
  const { xml } = await modelerHandle.modeler.saveXML({ format: true });
  const definitionsXml = await saveDefinitionsXml(modelerHandle);
  const registryXml = await saveRegistryXml(modelerHandle);
  const manualRegistryXml = saveManualRegistryXml(modelerHandle);

  return pickBestBpmnXml([xml, definitionsXml, registryXml, manualRegistryXml]);
}

export async function getSaveDebugInfo(modelerHandle) {
  const modeler = modelerHandle?.modeler;
  const container = modelerHandle?.container;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const businessObjects = elementRegistry
    ? elementRegistry
      .getAll()
      .map((element) => element?.businessObject)
      .filter((businessObject) => businessObject && typeof businessObject.id === "string" && typeof businessObject.$type === "string")
    : [];

  const distinctTypes = [...new Set(businessObjects.map((businessObject) => businessObject.$type))].sort();
  const saveXmlCandidate = await modeler.saveXML({ format: true }).then((result) => result?.xml ?? "");
  const definitionsXmlCandidate = await saveDefinitionsXml(modelerHandle);
  const registryXmlCandidate = await saveRegistryXml(modelerHandle);
  const manualRegistryXmlCandidate = saveManualRegistryXml(modelerHandle);

  return {
    version: WORKFLOW_JS_VERSION,
    elementCount: businessObjects.length,
    distinctTypes,
    domElementCount: container?.querySelectorAll?.(".djs-element").length ?? 0,
    domShapeCount: container?.querySelectorAll?.("svg .djs-shape, svg .djs-connection").length ?? 0,
    domContainerCount: document.querySelectorAll(".djs-container").length,
    saveXmlScore: scoreBpmnXml(saveXmlCandidate),
    definitionsScore: scoreBpmnXml(definitionsXmlCandidate),
    registryScore: scoreBpmnXml(registryXmlCandidate),
    manualRegistryScore: scoreBpmnXml(manualRegistryXmlCandidate)
  };
}

export function getLoadDebugInfo(modelerHandle) {
  return modelerHandle?.lastImportDebug ?? null;
}

function buildImportDebug(modeler, container, warnings) {
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const businessObjects = elementRegistry
    ? elementRegistry
      .getAll()
      .map((element) => element?.businessObject)
      .filter((businessObject) => businessObject && typeof businessObject.id === "string" && typeof businessObject.$type === "string")
    : [];

  return {
    version: WORKFLOW_JS_VERSION,
    warningCount: Array.isArray(warnings) ? warnings.length : 0,
    elementCount: businessObjects.length,
    distinctTypes: [...new Set(businessObjects.map((businessObject) => businessObject.$type))].sort(),
    domElementCount: container?.querySelectorAll?.(".djs-element").length ?? 0,
    domShapeCount: container?.querySelectorAll?.("svg .djs-shape, svg .djs-connection").length ?? 0
  };
}

async function saveDefinitionsXml(modelerHandle) {
  const modeler = modelerHandle?.modeler;
  const moddle = modeler?.get?.("moddle", false);
  const definitions = typeof modeler?.getDefinitions === "function"
    ? modeler.getDefinitions()
    : null;

  if (!moddle || typeof moddle.toXML !== "function" || !definitions) {
    return null;
  }

  try {
    const { xml } = await moddle.toXML(definitions, { format: true });
    return typeof xml === "string" ? xml : null;
  } catch {
    return null;
  }
}

function shouldPreferDefinitionsXml(definitionsXml, savedXml) {
  if (typeof definitionsXml !== "string" || !definitionsXml.trim()) {
    return false;
  }

  if (typeof savedXml !== "string" || !savedXml.trim()) {
    return true;
  }

  const definitionsScore = scoreBpmnXml(definitionsXml);
  const savedScore = scoreBpmnXml(savedXml);
  return definitionsScore > savedScore;
}

function pickBestBpmnXml(candidates) {
  let bestXml = null;
  let bestScore = -1;

  for (const candidate of candidates) {
    if (typeof candidate !== "string" || !candidate.trim()) {
      continue;
    }

    const score = scoreBpmnXml(candidate);
    if (score > bestScore) {
      bestXml = candidate;
      bestScore = score;
    }
  }

  return bestXml ?? candidates.find((candidate) => typeof candidate === "string") ?? "";
}

function scoreBpmnXml(xml) {
  if (typeof xml !== "string" || !xml.trim()) {
    return 0;
  }

  const elementMatches = xml.match(/<bpmn:(startEvent|endEvent|userTask|serviceTask|scriptTask|businessRuleTask|sendTask|receiveTask|manualTask|task|exclusiveGateway|inclusiveGateway|parallelGateway|eventBasedGateway|complexGateway|subProcess|callActivity|boundaryEvent|intermediateCatchEvent|intermediateThrowEvent|sequenceFlow)\b/g);
  const shapeMatches = xml.match(/<bpmndi:(BPMNShape|BPMNEdge)\b/g);

  return (elementMatches?.length ?? 0) * 10 + (shapeMatches?.length ?? 0);
}

async function saveRegistryXml(modelerHandle) {
  const modeler = modelerHandle?.modeler;
  const moddle = modeler?.get?.("moddle", false);
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const definitions = typeof modeler?.getDefinitions === "function"
    ? modeler.getDefinitions()
    : null;

  if (!moddle || !elementRegistry || !definitions || typeof moddle.toXML !== "function") {
    return null;
  }

  try {
    const { xml: baseXml } = await moddle.toXML(definitions, { format: true });
    if (typeof baseXml !== "string" || !baseXml.trim()) {
      return null;
    }

    const parser = new DOMParser();
    const document = parser.parseFromString(baseXml, "application/xml");
    const definitionsElement = document.documentElement;
    const processElement = definitionsElement.getElementsByTagNameNS("http://www.omg.org/spec/BPMN/20100524/MODEL", "process")[0];
    const planeElement = definitionsElement.getElementsByTagNameNS("http://www.omg.org/spec/BPMN/20100524/DI", "BPMNPlane")[0];

    if (!processElement || !planeElement) {
      return null;
    }

    definitionsElement.setAttribute("xmlns:bpmndi", "http://www.omg.org/spec/BPMN/20100524/DI");
    definitionsElement.setAttribute("xmlns:dc", "http://www.omg.org/spec/DD/20100524/DC");
    definitionsElement.setAttribute("xmlns:di", "http://www.omg.org/spec/DD/20100524/DI");

    processElement.replaceChildren();
    planeElement.replaceChildren();

    const elements = elementRegistry.getAll().filter((element) => !element?.labelTarget && element?.businessObject);
    const rootProcessId = processElement.getAttribute("id");
    const flowElements = [];
    const sequenceFlows = [];
    const diElements = [];

    for (const element of elements) {
      const businessObject = element.businessObject;
      if (!businessObject || typeof businessObject.$type !== "string") {
        continue;
      }

      if (businessObject.$type === "bpmn:Process" && businessObject.id === rootProcessId) {
        continue;
      }

      if (element.di) {
        diElements.push(element.di);
      }

      if (businessObject.$type === "bpmn:SequenceFlow") {
        sequenceFlows.push(businessObject);
        continue;
      }

      if (businessObject.$instanceOf?.("bpmn:FlowElement") === true) {
        flowElements.push(businessObject);
      }
    }

    for (const businessObject of [...flowElements, ...sequenceFlows]) {
      const node = await serializeXmlNode(document, moddle, businessObject);
      if (node) {
        processElement.appendChild(node);
      }
    }

    for (const diObject of diElements) {
      const node = await serializeXmlNode(document, moddle, diObject);
      if (node) {
        planeElement.appendChild(node);
      }
    }

    return new XMLSerializer().serializeToString(document);
  } catch {
    return null;
  }
}

function saveManualRegistryXml(modelerHandle) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const canvas = modeler?.get?.("canvas", false);
  const definitions = typeof modeler?.getDefinitions === "function"
    ? modeler.getDefinitions()
    : null;

  if (!elementRegistry || !canvas || !definitions) {
    return null;
  }

  try {
    const root = canvas.getRootElement?.();
    const processId = root?.businessObject?.id || definitions.rootElements?.find?.((element) => element.$type === "bpmn:Process")?.id;
    const processName = root?.businessObject?.name || definitions.rootElements?.find?.((element) => element.$type === "bpmn:Process")?.name || processId;
    const definitionId = definitions.id || `Definitions_${processId || "workflow"}`;

    if (!processId) {
      return null;
    }

    const shapes = [];
    const flows = [];

    for (const element of elementRegistry.getAll()) {
      if (!element || element.labelTarget || !element.businessObject) {
        continue;
      }

      const businessObject = element.businessObject;
      if (businessObject.$type === "bpmn:Process") {
        continue;
      }

      if (element.waypoints && businessObject.$type === "bpmn:SequenceFlow") {
        flows.push({
          id: businessObject.id,
          name: businessObject.name || null,
          type: businessObject.$type,
          sourceRef: businessObject.sourceRef?.id || element.source?.businessObject?.id || null,
          targetRef: businessObject.targetRef?.id || element.target?.businessObject?.id || null,
          waypoints: element.waypoints.map((point) => ({ x: point.x, y: point.y }))
        });
        continue;
      }

      if (typeof element.x !== "number" || typeof element.y !== "number") {
        continue;
      }

      shapes.push({
        id: businessObject.id,
        name: businessObject.name || null,
        type: businessObject.$type,
        x: element.x,
        y: element.y,
        width: element.width,
        height: element.height
      });
    }

    if (shapes.length === 0) {
      return null;
    }

    const shapeXml = shapes.map((shape) => {
      const tagName = toBpmnTagName(shape.type);
      if (!tagName) {
        return "";
      }

      return [
        `    <bpmn:${tagName} id="${escapeXml(shape.id)}"${shape.name ? ` name="${escapeXml(shape.name)}"` : ""}>`,
        ...flows
          .filter((flow) => flow.targetRef === shape.id)
          .map((flow) => `      <bpmn:incoming>${escapeXml(flow.id)}</bpmn:incoming>`),
        ...flows
          .filter((flow) => flow.sourceRef === shape.id)
          .map((flow) => `      <bpmn:outgoing>${escapeXml(flow.id)}</bpmn:outgoing>`),
        `    </bpmn:${tagName}>`
      ].join("\n");
    }).filter(Boolean);

    const flowXml = flows
      .filter((flow) => flow.id && flow.sourceRef && flow.targetRef)
      .map((flow) =>
        `    <bpmn:sequenceFlow id="${escapeXml(flow.id)}" sourceRef="${escapeXml(flow.sourceRef)}" targetRef="${escapeXml(flow.targetRef)}"${flow.name ? ` name="${escapeXml(flow.name)}"` : ""} />`);

    const diShapeXml = shapes.map((shape) =>
      [
        `      <bpmndi:BPMNShape id="Shape_${escapeXml(shape.id)}" bpmnElement="${escapeXml(shape.id)}">`,
        `        <dc:Bounds x="${shape.x}" y="${shape.y}" width="${shape.width}" height="${shape.height}" />`,
        "      </bpmndi:BPMNShape>"
      ].join("\n"));

    const diFlowXml = flows
      .filter((flow) => flow.id && flow.waypoints?.length > 0)
      .map((flow) =>
        [
          `      <bpmndi:BPMNEdge id="Edge_${escapeXml(flow.id)}" bpmnElement="${escapeXml(flow.id)}">`,
          ...flow.waypoints.map((point) => `        <di:waypoint x="${point.x}" y="${point.y}" />`),
          "      </bpmndi:BPMNEdge>"
        ].join("\n"));

    return [
      '<?xml version="1.0" encoding="UTF-8"?>',
      `<bpmn:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:autonate="http://autonate.dev/workflows" id="${escapeXml(definitionId)}" targetNamespace="http://autonate.dev/workflows">`,
      `  <bpmn:process id="${escapeXml(processId)}" name="${escapeXml(processName || processId)}" isExecutable="true">`,
      ...shapeXml,
      ...flowXml,
      "  </bpmn:process>",
      `  <bpmndi:BPMNDiagram id="BPMNDiagram_1">`,
      `    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="${escapeXml(processId)}">`,
      ...diShapeXml,
      ...diFlowXml,
      "    </bpmndi:BPMNPlane>",
      "  </bpmndi:BPMNDiagram>",
      "</bpmn:definitions>"
    ].join("\n");
  } catch {
    return null;
  }
}

function toBpmnTagName(type) {
  if (typeof type !== "string" || !type.startsWith("bpmn:")) {
    return null;
  }

  return type.slice("bpmn:".length);
}

function escapeXml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

async function serializeXmlNode(document, moddle, moddleElement) {
  try {
    const { xml } = await moddle.toXML(moddleElement, { format: true });
    if (typeof xml !== "string" || !xml.trim()) {
      return null;
    }

    const parsed = new DOMParser().parseFromString(xml, "application/xml");
    const root = parsed.documentElement;
    if (!root || root.tagName === "parsererror") {
      return null;
    }

    return document.importNode(root, true);
  } catch {
    return null;
  }
}

function describeElement(element) {
  return describeBusinessObject(element?.businessObject ?? null);
}

function describeBusinessObject(businessObject) {
  if (!businessObject || typeof businessObject.id !== "string" || typeof businessObject.$type !== "string") {
    return null;
  }

  const conditionExpression = businessObject.conditionExpression;
  const signal = describeSignalStartEvent(businessObject);
  const timer = describeTimerStartEvent(businessObject);
  const timerCatch = describeTimerIntermediateCatchEvent(businessObject);
  const serviceTask = describeServiceTask(businessObject);
  const description = {
    id: businessObject.id,
    type: businessObject.$type,
    name: typeof businessObject.name === "string" ? businessObject.name : null,
    scriptFormat: typeof businessObject.scriptFormat === "string" ? businessObject.scriptFormat : null,
    // #153. Stored in the autonate namespace, which is on the do-not-rename
    // list. Read from $attrs the same way every other namespaced property in
    // this file is: bpmn-js has no moddle extension loaded for it, so
    // modeling.updateProperties would serialise it without the prefix.
    runAs: readAutoNateAttribute(businessObject, "runAs"),
    script: typeof businessObject.script === "string" ? businessObject.script : null,
    resultVariable: typeof businessObject.resultVariable === "string" ? businessObject.resultVariable : null,
    conditionExpression: typeof conditionExpression?.body === "string" ? conditionExpression.body : null,
    assignee: readFlowableString(businessObject, "assignee"),
    candidateUsers: readFlowableList(businessObject, "candidateUsers"),
    candidateGroups: readFlowableList(businessObject, "candidateGroups"),
    dueDate: readFlowableString(businessObject, "dueDate"),
    // userForm controls how the SPA renders this user task to assignees:
    // "simple" → confirm-and-complete modal (default when omitted),
    // "modal" → JsxFormHost in a modal, "page" → full-page route.
    // userFormShortCode references the Form to render for "modal"/"page".
    userFormMode: readFlowableString(businessObject, "userFormMode"),
    userFormShortCode: readFlowableString(businessObject, "userFormShortCode")
  };

  if (signal) {
    // Only present for signal start events. Used by the SPA to discriminate
    // from plain start events; downstream code treats `signalName` as the
    // signal's display name (Flowable matches it against incoming eventType).
    description.signalName = signal.signalName;
    description.signalTopic = signal.signalTopic;
    description.recordTypeShortCodes = signal.recordTypeShortCodes;
  }

  if (timer) {
    // Only present for timer start events. Same discrimination role: lets the
    // SPA route the selection to the timer modal and pre-populate the picker.
    description.timerCycleCron = timer.timerCycleCron;
    description.timerEndDate = timer.timerEndDate;
  }

  if (timerCatch) {
    // Only present for timer intermediate catch events. Mutually exclusive
    // with the start-event timer fields above (start/intermediate are
    // different $type values), so the studio can route on whichever is set.
    description.timerDuration = timerCatch.timerDuration;
    description.timerDate = timerCatch.timerDate;
  }

  if (serviceTask) {
    // Only present for service tasks the studio recognizes (delegateExpression
    // points at the AutoNate behavior bridge). Lets the studio route the
    // selection to the service-task modal and pre-populate the picker.
    description.serviceTaskKind = serviceTask.serviceTaskKind;
    description.behaviorKey = serviceTask.behaviorKey;
  }

  if (businessObject.$type === "bpmn:ExclusiveGateway" || businessObject.$type === "bpmn:InclusiveGateway") {
    // Only Exclusive and Inclusive gateways carry a `default` outgoing flow.
    // Surface it (and the candidate outgoing flows) so the studio panel can
    // render a default-flow picker.
    const outgoing = Array.isArray(businessObject.outgoing) ? businessObject.outgoing : [];
    description.defaultFlowId = typeof businessObject.default?.id === "string" ? businessObject.default.id : null;
    description.outgoingFlows = outgoing
      .filter((flow) => flow?.$type === "bpmn:SequenceFlow" && typeof flow.id === "string")
      .map((flow) => ({
        id: flow.id,
        name: typeof flow.name === "string" ? flow.name : null
      }));
  }

  if (businessObject.$type === "bpmn:SequenceFlow") {
    // Surface the source element's $type so the sequence-flow editor can
    // suppress the condition field for parallel-gateway outflows (Flowable
    // ignores conditions there at runtime).
    description.sourceType = typeof businessObject.sourceRef?.$type === "string" ? businessObject.sourceRef.$type : null;
  }

  return description;
}

function describeServiceTask(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:ServiceTask") {
    return null;
  }

  // Two cases route to our modal:
  //   1. delegateExpression is already ${autonateBehaviorDelegate} — the
  //      task was previously configured by us; pre-populate from the
  //      flowable: attributes we wrote.
  //   2. The task is unwired (no class / expression / delegateExpression /
  //      type) — fresh from the palette; let the user pick a behavior and
  //      we'll write the wiring on apply.
  // A task pointing at a different delegate (custom Java class, plugin-
  // shipped delegate, etc.) is left alone — returning null here means
  // selecting it shows no modal, matching the "we don't manage this" stance.
  const delegateExpression = readFlowableServiceTaskAttr(businessObject, "delegateExpression");
  const className = readFlowableServiceTaskAttr(businessObject, "class");
  const expression = readFlowableServiceTaskAttr(businessObject, "expression");
  const flowableType = readFlowableServiceTaskAttr(businessObject, "type");

  const isOurs = delegateExpression === "${autonateBehaviorDelegate}";
  const isUnwired =
    !delegateExpression && !className && !expression && !flowableType;

  if (!isOurs && !isUnwired) {
    return null;
  }

  const kind = readFlowableServiceTaskAttr(businessObject, "autonateServiceKind") ?? "behavior";
  const behaviorKey = readFlowableServiceTaskAttr(businessObject, "behaviorKey");

  return {
    serviceTaskKind: kind,
    behaviorKey: behaviorKey
  };
}

// The autonate-namespace equivalents of the flowable: helpers above. The
// namespace URI is on the do-not-rename list: changing it orphans the property
// on every diagram that already carries it.
const AUTONATE_ATTR_PREFIX = "autonate:";

// #153: mark script tasks that declare an identity, so a reviewer can see the
// privileged steps by looking at the diagram rather than opening each one.
//
// A marker class rather than a rendered overlay: bpmn-js reapplies markers
// across re-renders, and the styling then lives in CSS with the rest of the
// studio's appearance.
export function refreshScriptIdentityMarkers(modelerHandle) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const canvas = modeler?.get?.("canvas", false);
  if (!elementRegistry || !canvas?.addMarker) return;

  for (const element of elementRegistry.getAll?.() ?? []) {
    if (element?.businessObject?.$type !== "bpmn:ScriptTask") continue;
    const runAs = readAutoNateAttribute(element.businessObject, "runAs");
    canvas.removeMarker(element.id, "an8-script-system");
    canvas.removeMarker(element.id, "an8-script-author");
    if (runAs === "system") {
      canvas.addMarker(element.id, "an8-script-system");
    } else if (runAs === "workflowAuthor") {
      canvas.addMarker(element.id, "an8-script-author");
    }
  }
}

function readAutoNateAttribute(businessObject, name) {
  const value = businessObject?.$attrs?.[`${AUTONATE_ATTR_PREFIX}${name}`];
  return typeof value === "string" && value.length > 0 ? value : null;
}

function writeAutoNateAttribute(businessObject, name, value) {
  if (!businessObject) return;
  businessObject.$attrs = businessObject.$attrs ?? {};
  const key = `${AUTONATE_ATTR_PREFIX}${name}`;
  if (value === null || value === undefined || value === "") {
    delete businessObject.$attrs[key];
    return;
  }
  businessObject.$attrs[key] = value;
}

function readFlowableServiceTaskAttr(businessObject, name) {
  const direct = businessObject[name];
  if (typeof direct === "string" && direct.length > 0) return direct;
  const fromAttrs = businessObject.$attrs?.[`flowable:${name}`];
  return typeof fromAttrs === "string" && fromAttrs.length > 0 ? fromAttrs : null;
}

function describeTimerIntermediateCatchEvent(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:IntermediateCatchEvent") {
    return null;
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const timerEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:TimerEventDefinition"
  );
  if (!timerEventDefinition) {
    return null;
  }

  const duration = typeof timerEventDefinition.timeDuration?.body === "string"
    ? timerEventDefinition.timeDuration.body
    : null;
  const date = typeof timerEventDefinition.timeDate?.body === "string"
    ? timerEventDefinition.timeDate.body
    : null;

  return { timerDuration: duration, timerDate: date };
}

function describeTimerStartEvent(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:StartEvent") {
    return null;
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const timerEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:TimerEventDefinition"
  );
  if (!timerEventDefinition) {
    return null;
  }

  const timeCycle = timerEventDefinition.timeCycle;
  const cron = typeof timeCycle?.body === "string" ? timeCycle.body : null;

  // Flowable's <flowable:endDate> ends up either as a typed extension element
  // (if the schema picked it up) or in $attrs as the ns-prefixed attribute
  // when bpmn-moddle didn't materialize it. Read both shapes defensively.
  let endDate = null;
  if (typeof timerEventDefinition.endDate === "string") {
    endDate = timerEventDefinition.endDate;
  } else if (typeof timerEventDefinition.$attrs?.["flowable:endDate"] === "string") {
    endDate = timerEventDefinition.$attrs["flowable:endDate"];
  }

  return { timerCycleCron: cron, timerEndDate: endDate };
}

function describeSignalStartEvent(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:StartEvent") {
    return null;
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const signalEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:SignalEventDefinition"
  );
  if (!signalEventDefinition) {
    return null;
  }

  const signalRef = signalEventDefinition.signalRef;
  const signalName = typeof signalRef?.name === "string" ? signalRef.name : null;
  const signalTopic =
    typeof signalRef?.$attrs?.["flowable:topic"] === "string"
      ? signalRef.$attrs["flowable:topic"]
      : null;

  // Per-event record-type filter lives on the <signalEventDefinition>, not on
  // the shared <signal> root, so different events can subscribe to the same
  // signal name but apply different filters.
  const rawShortCodes =
    typeof signalEventDefinition.$attrs?.["flowable:recordTypeShortCodes"] === "string"
      ? signalEventDefinition.$attrs["flowable:recordTypeShortCodes"]
      : null;
  const recordTypeShortCodes = rawShortCodes
    ? rawShortCodes
        .split(",")
        .map((s) => s.trim())
        .filter((s) => s.length > 0)
    : [];

  return { signalName, signalTopic, recordTypeShortCodes };
}

function readFlowableString(businessObject, name) {
  const direct = businessObject[name];
  if (typeof direct === "string" && direct.trim()) {
    return direct;
  }

  const fromAttrs = businessObject.$attrs?.[`flowable:${name}`];
  if (typeof fromAttrs === "string" && fromAttrs.trim()) {
    return fromAttrs;
  }

  return null;
}

function readFlowableList(businessObject, name) {
  const raw = readFlowableString(businessObject, name);
  if (!raw) {
    return [];
  }

  const trimmed = raw.trim();
  if (trimmed.startsWith("${")) {
    return [trimmed];
  }

  return trimmed
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

function writeFlowableAttribute(businessObject, name, value) {
  if (!businessObject) {
    return;
  }

  const attrs = businessObject.$attrs ?? (businessObject.$attrs = {});
  const key = `flowable:${name}`;
  if (typeof value === "string" && value.trim()) {
    attrs[key] = value;
  } else {
    delete attrs[key];
  }
}

function serializeFlowableList(values) {
  if (!Array.isArray(values)) {
    return null;
  }

  const trimmed = values
    .map((entry) => (typeof entry === "string" ? entry.trim() : ""))
    .filter((entry) => entry.length > 0);

  if (trimmed.length === 0) {
    return null;
  }

  if (trimmed.length === 1 && trimmed[0].startsWith("${")) {
    return trimmed[0];
  }

  return trimmed.join(",");
}

function normalizeOptionalString(value) {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

export function getElementSnapshots(modelerHandle) {
  const elementRegistry = modelerHandle.modeler.get("elementRegistry");
  return elementRegistry
    .getAll()
    .map((element) => element?.businessObject)
    .filter((businessObject) => businessObject && typeof businessObject.id === "string" && typeof businessObject.$type === "string")
    .map((businessObject) => describeBusinessObject(businessObject));
}

// IDs of the elements currently selected in the modeler. Returns [] when no
// modeler instance is available (still loading) or no selection.
export function getSelectedElementIds(modelerHandle) {
  const selection = modelerHandle?.modeler?.get?.("selection", false);
  if (!selection || typeof selection.get !== "function") return [];
  const elements = selection.get() ?? [];
  return elements
    .map((element) => (element && typeof element.id === "string" ? element.id : null))
    .filter((id) => id !== null);
}

// Full describe of one element by id, reading the live businessObject. Used
// by the page-context provider to answer 'fresh' per-node queries from the
// chatbot. Returns null when the element id is not in the registry.
export function describeElementById(modelerHandle, id) {
  if (typeof id !== "string" || id.length === 0) return null;
  const elementRegistry = modelerHandle?.modeler?.get?.("elementRegistry", false);
  if (!elementRegistry) return null;
  const element = elementRegistry.get(id);
  const businessObject = element?.businessObject;
  if (!businessObject) return null;
  return describeBusinessObject(businessObject);
}

export function updateScriptTaskProperties(modelerHandle, task) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !task?.id) {
    throw new Error("The BPMN modeler is not ready to update the script task.");
  }

  const element = elementRegistry.get(task.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:ScriptTask") {
    throw new Error(`Script task '${task.id}' is no longer available in the diagram.`);
  }

  modeling.updateProperties(element, {
    name: normalizeOptionalString(task.name),
    // The author's language choice, stored in the standard BPMN attribute
    // rather than an Auton8-specific one (#154). Defaulted rather than trusted:
    // a task authored before Python support carries no value.
    scriptFormat: task.scriptFormat === "python" ? "python" : "javascript",
    script: typeof task.script === "string" ? task.script : "",
    resultVariable: normalizeOptionalString(task.resultVariable)
  });

  // #153: the identity declaration. Written after updateProperties so it is
  // not cleared by it, and only when set — an unset value is the default
  // (the preceding user task's assignee) and writing an empty attribute would
  // make "unset" and "explicitly nothing" indistinguishable in the XML.
  writeAutoNateAttribute(
    element.businessObject,
    "runAs",
    task.runAs === "system" || task.runAs === "workflowAuthor" ? task.runAs : null
  );
  refreshScriptIdentityMarkers(modelerHandle);
}

export function updateUserTaskProperties(modelerHandle, task) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !task?.id) {
    throw new Error("The BPMN modeler is not ready to update the user task.");
  }

  const element = elementRegistry.get(task.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:UserTask") {
    throw new Error(`User task '${task.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  const assignee = normalizeOptionalString(task.assignee);
  const candidateUsers = serializeFlowableList(task.candidateUsers);
  const candidateGroups = serializeFlowableList(task.candidateGroups);
  const dueDate = normalizeOptionalString(task.dueDate);

  writeFlowableAttribute(businessObject, "assignee", assignee);
  writeFlowableAttribute(businessObject, "candidateUsers", candidateUsers);
  writeFlowableAttribute(businessObject, "candidateGroups", candidateGroups);
  writeFlowableAttribute(businessObject, "dueDate", dueDate);

  // userFormMode is the source of truth for rendering. We only persist it
  // when it's "modal" or "page"; "simple" (or null) means the default
  // simple-complete UI, which doesn't need a stored value. Same idea for
  // userFormShortCode: only relevant when a form is referenced.
  const userFormMode = normalizeOptionalString(task.userFormMode);
  const userFormShortCode = normalizeOptionalString(task.userFormShortCode);
  writeFlowableAttribute(
    businessObject,
    "userFormMode",
    userFormMode === "simple" ? null : userFormMode
  );
  writeFlowableAttribute(
    businessObject,
    "userFormShortCode",
    userFormMode === "modal" || userFormMode === "page" ? userFormShortCode : null
  );

  modeling.updateProperties(element, {
    name: normalizeOptionalString(task.name)
  });
}

export function updateSignalStartEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the signal start event.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:StartEvent") {
    throw new Error(`Signal start event '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const signalEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:SignalEventDefinition"
  );
  if (!signalEventDefinition) {
    throw new Error(
      `Start event '${payload.id}' is not a signal start event — drop a signal start event from the palette instead.`
    );
  }

  const signalName = normalizeOptionalString(payload.signalName);
  const topic = normalizeOptionalString(payload.signalTopic);

  // Wire the signal root element. Reuse an existing root by stable id (kept on
  // the previously-attached signalRef) so multiple events sharing one signal
  // stay linked when the user renames it. Fall through to creating a new root
  // if the user changed the name to one that already has its own definition.
  const definitions =
    typeof modeler.getDefinitions === "function" ? modeler.getDefinitions() : null;
  if (!definitions) {
    throw new Error("The BPMN modeler is missing a definitions root.");
  }

  const rootElements = Array.isArray(definitions.rootElements) ? definitions.rootElements : [];
  let signal = signalEventDefinition.signalRef ?? null;

  if (!signalName) {
    // Strip the binding when the user clears the name. The server will
    // surface a validation error before publish.
    signalEventDefinition.signalRef = undefined;
    modeling.updateProperties(element, {
      name: normalizeOptionalString(payload.name)
    });
    return;
  }

  const existingByName = rootElements.find(
    (rootElement) => rootElement?.$type === "bpmn:Signal" && rootElement.name === signalName
  );

  if (existingByName && existingByName !== signal) {
    signal = existingByName;
  } else if (!signal || signal.$type !== "bpmn:Signal") {
    signal = moddle.create("bpmn:Signal", {
      id: buildSignalId(rootElements, signalName),
      name: signalName
    });
    rootElements.push(signal);
    if (typeof signal.$parent === "object") {
      signal.$parent = definitions;
    }
  }

  signal.name = signalName;
  writeFlowableAttribute(signal, "topic", topic);

  // Record-type filter is per-event (lives on <signalEventDefinition>), not on
  // the shared <signal> root. Order is preserved — the studio decides ordering
  // and the bridge faithfully relays it. Empty list clears the attribute.
  const shortCodes = Array.isArray(payload.recordTypeShortCodes)
    ? payload.recordTypeShortCodes
        .map((s) => (typeof s === "string" ? s.trim() : ""))
        .filter((s) => s.length > 0)
    : [];

  writeFlowableAttribute(
    signalEventDefinition,
    "recordTypeShortCodes",
    shortCodes.length === 0 ? null : shortCodes.join(",")
  );

  signalEventDefinition.signalRef = signal;

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name)
  });
}

export function updateTimerStartEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the timer start event.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:StartEvent") {
    throw new Error(`Timer start event '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const timerEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:TimerEventDefinition"
  );
  if (!timerEventDefinition) {
    throw new Error(
      `Start event '${payload.id}' is not a timer start event — drop a timer start event from the palette instead.`
    );
  }

  const cron = normalizeOptionalString(payload.timeCycle);
  if (!cron) {
    // Clear the schedule entirely. Server-side validation will reject the
    // workflow on publish; we still write the empty state so saving a draft
    // round-trips cleanly.
    timerEventDefinition.timeCycle = undefined;
  } else {
    const expression = moddle.create("bpmn:FormalExpression", { body: cron });
    // Annotate the formal expression with flowable:type="cron" so Flowable
    // dispatches the body to its cron parser instead of ISO 8601.
    writeFlowableAttribute(expression, "type", "cron");
    timerEventDefinition.timeCycle = expression;
  }

  // Drop the alternative kinds in case the user had configured a one-shot
  // schedule before; we only emit cycle from this picker.
  timerEventDefinition.timeDate = undefined;
  timerEventDefinition.timeDuration = undefined;

  const endDate = normalizeOptionalString(payload.endDate);
  // Flowable's endDate isn't part of the BPMN moddle schema, so write it as
  // an attribute under the flowable namespace; the XML serializer keeps it
  // intact and the engine reads it natively. ApplyElementSnapshots also
  // normalizes the on-disk shape to a child element on save.
  writeFlowableAttribute(timerEventDefinition, "endDate", endDate);

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name)
  });
}

export function updateTimerIntermediateCatchEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the timer intermediate catch event.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:IntermediateCatchEvent") {
    throw new Error(`Timer intermediate catch event '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const timerEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:TimerEventDefinition"
  );
  if (!timerEventDefinition) {
    throw new Error(
      `Intermediate catch event '${payload.id}' is not a timer catch event — drop a timer intermediate catch event from the palette instead.`
    );
  }

  const duration = normalizeOptionalString(payload.timerDuration);
  const date = normalizeOptionalString(payload.timerDate);

  // Catch timers fire once: clear every kind first so a mode switch can't
  // leave the previous child behind. Flowable rejects multiple kinds.
  timerEventDefinition.timeCycle = undefined;
  timerEventDefinition.timeDuration = undefined;
  timerEventDefinition.timeDate = undefined;

  if (duration) {
    timerEventDefinition.timeDuration = moddle.create("bpmn:FormalExpression", { body: duration });
  } else if (date) {
    timerEventDefinition.timeDate = moddle.create("bpmn:FormalExpression", { body: date });
  }

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name)
  });
}

export function updateServiceTaskProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the service task.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:ServiceTask") {
    throw new Error(`Service task '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  const kind = normalizeOptionalString(payload.serviceTaskKind) ?? "behavior";
  const behaviorKey = normalizeOptionalString(payload.behaviorKey);

  // Drop alternative wirings before writing ours so the saved XML can't end
  // up wired two ways at once. We sweep both shapes:
  //   * flowable:-prefixed entries — what we write today.
  //   * Plain (no-prefix) entries — bpmn-moddle imports unknown plain
  //     attributes into $attrs without a prefix, and an earlier studio
  //     build wrote `delegateExpression` as plain via
  //     modeling.updateProperties. Without removing them they survive the
  //     next save and Flowable rejects the deploy with "Attribute
  //     'delegateExpression' is not allowed to appear in element
  //     'bpmn:serviceTask'".
  if (businessObject.$attrs) {
    for (const key of [
      "flowable:class",
      "flowable:expression",
      "flowable:type",
      "flowable:delegateExpression",
      "class",
      "expression",
      "type",
      "delegateExpression"
    ]) {
      delete businessObject.$attrs[key];
    }
  }
  pruneLegacyServiceTaskExtensionFields(businessObject);

  // Mirrors how every other Flowable property in this codebase is stored
  // (assignee, dueDate, endDate, topic): a flowable: attribute on the
  // owning BPMN element via $attrs. The studio's bpmn-js doesn't load a
  // Flowable moddle extension, so going through modeling.updateProperties
  // for these would serialize them WITHOUT the flowable: prefix and
  // Flowable's deploy validator would reject the resulting XML.
  writeFlowableAttribute(businessObject, "delegateExpression", "${autonateBehaviorDelegate}");
  writeFlowableAttribute(businessObject, "autonateServiceKind", kind);
  writeFlowableAttribute(businessObject, "behaviorKey", behaviorKey);

  // Clear any plain (no-namespace) leftovers a prior studio iteration may
  // have set via modeling.updateProperties; passing null here removes them
  // from the businessObject so they don't survive the next save.
  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name),
    class: null,
    expression: null,
    type: null,
    delegateExpression: null
  });
}

// Strips any prior extension-element field-injection entries the studio
// wrote during an earlier (broken) iteration, so re-applying picks the
// attribute shape and stops the moddle parser from later rejecting an
// orphan flowable:Field child.
function pruneLegacyServiceTaskExtensionFields(businessObject) {
  const extensionElements = businessObject.extensionElements;
  const values = Array.isArray(extensionElements?.values) ? extensionElements.values : null;
  if (!values) return;
  const filtered = values.filter((value) => {
    if (!value) return false;
    if (value.$type !== "flowable:Field") return true;
    return value.name !== "autonateServiceKind" && value.name !== "behaviorKey";
  });
  if (filtered.length === 0) {
    businessObject.extensionElements = undefined;
  } else if (filtered.length !== values.length) {
    extensionElements.values = filtered;
  }
}

function buildSignalId(rootElements, signalName) {
  const slug = signalName
    .replace(/[^A-Za-z0-9_]+/g, "_")
    .replace(/^_+|_+$/g, "");
  const base = slug ? `Signal_${slug}` : "Signal_event";
  const existingIds = new Set(
    rootElements
      .map((rootElement) => rootElement?.id)
      .filter((id) => typeof id === "string" && id.length > 0)
  );

  if (!existingIds.has(base)) {
    return base;
  }

  let counter = 2;
  while (existingIds.has(`${base}_${counter}`)) {
    counter += 1;
  }
  return `${base}_${counter}`;
}

export function updateSequenceFlowProperties(modelerHandle, flow) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !flow?.id) {
    throw new Error("The BPMN modeler is not ready to update the sequence flow.");
  }

  const element = elementRegistry.get(flow.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:SequenceFlow") {
    throw new Error(`Sequence flow '${flow.id}' is no longer available in the diagram.`);
  }

  const conditionBody = normalizeOptionalString(flow.conditionExpression);
  const conditionExpression = conditionBody
    ? moddle.create("bpmn:FormalExpression", { body: conditionBody })
    : undefined;

  modeling.updateProperties(element, {
    name: normalizeOptionalString(flow.name),
    conditionExpression
  });
}

export function updateGatewayDefaultFlow(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the gateway.");
  }

  const gatewayElement = elementRegistry.get(payload.id);
  if (!gatewayElement?.businessObject) {
    throw new Error(`Gateway '${payload.id}' is no longer available in the diagram.`);
  }

  const businessType = gatewayElement.businessObject.$type;
  if (businessType !== "bpmn:ExclusiveGateway" && businessType !== "bpmn:InclusiveGateway") {
    throw new Error(`Default outgoing flow is only supported on exclusive or inclusive gateways (got ${businessType}).`);
  }

  const defaultFlowId = normalizeOptionalString(payload.defaultFlowId);
  let defaultElement;
  if (defaultFlowId) {
    const flowElement = elementRegistry.get(defaultFlowId);
    if (!flowElement?.businessObject || flowElement.businessObject.$type !== "bpmn:SequenceFlow") {
      throw new Error(`Sequence flow '${defaultFlowId}' is no longer available in the diagram.`);
    }
    // bpmn-js expects the SequenceFlow business object (not the element) when
    // setting `default` so it can serialise as a reference attribute.
    defaultElement = flowElement.businessObject;
  } else {
    // Passing undefined removes the attribute entirely on save.
    defaultElement = undefined;
  }

  modeling.updateProperties(gatewayElement, {
    default: defaultElement
  });
}

export function updateGenericElementName(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the element.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject) {
    throw new Error(`Element '${payload.id}' is no longer available in the diagram.`);
  }

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name)
  });
}

export async function loadXml(modelerHandle, xml) {
  modelerHandle.setSuppressDirtyEvents(true);
  try {
    await modelerHandle.modeler.importXML(xml);
    fitAndCenter(modelerHandle.modeler);
  } finally {
    modelerHandle.setSuppressDirtyEvents(false);
  }
}

export async function createNewDiagram(modelerHandle, xml) {
  await loadXml(modelerHandle, xml);
}

export async function loadReadonlyDiagram(viewerHandle, xml) {
  clearExecutionState(viewerHandle);
  await viewerHandle.viewer.importXML(xml);
  fitAndCenter(viewerHandle.viewer);
}

// Fits the diagram in the viewport and centers it. Deferred to the next
// animation frame so layout has settled — at import time the container often
// hasn't reached its final size yet, which causes fit-viewport to size
// against a stale (smaller) viewport and pin the diagram to the top-left.
function fitAndCenter(instance) {
  const apply = () => {
    const canvas = instance.get("canvas");
    if (typeof canvas.resized === "function") {
      canvas.resized();
    }
    canvas.zoom("fit-viewport", "auto");
  };
  if (typeof requestAnimationFrame === "function") {
    requestAnimationFrame(apply);
  } else {
    apply();
  }
}

export function highlightExecutionState(
  viewerHandle,
  completedActivityIds,
  currentActivityIds,
  cancelledActivityIds,
  failedActivityIds
) {
  clearExecutionState(viewerHandle);

  console.log("[AutoNate viewer] highlightExecutionState", {
    completed: completedActivityIds,
    current: currentActivityIds,
    cancelled: cancelledActivityIds,
    failed: failedActivityIds
  });

  viewerHandle?.setCurrentActivityIds?.(currentActivityIds);
  addMarkers(viewerHandle, completedActivityIds, "execution-step-completed");
  addMarkers(viewerHandle, currentActivityIds, "execution-step-current");
  addMarkers(viewerHandle, cancelledActivityIds, "execution-step-cancelled");
  addMarkers(viewerHandle, failedActivityIds, "execution-step-failed");
}

// `options` carries thunks that are read on every right-click so React state
// (notably the override permission check) can change without rebuilding the
// viewer. All thunks are optional with sensible defaults.
//   - getCanOverride()                — boolean. Suppresses the override-only
//                                       task actions (Complete, Reassign,
//                                       Change Due Date) on current-activity
//                                       nodes when false.
//   - getCanMoveState()               — boolean. Unlocks "Move Execution Here"
//                                       on non-current activity nodes when
//                                       true. The React layer also gates this
//                                       on whether the run is still in flight.
//   - getActiveTasksAtActivity(id)    — array of { id, assignee, dueDate? }
//                                       for runtime tasks at this BPMN
//                                       activity. dueDate is forwarded into
//                                       the change-due-date callback.
//   - getCompletedAssignees(id)       — Promise resolving to assignees that
//                                       already completed an instance (used to
//                                       gray out submenu entries for parallel
//                                       multi-instance user tasks).
export function enableCurrentStepContextMenu(viewerHandle, dotNetRef, options) {
  if (!viewerHandle?.viewer || !dotNetRef) {
    return;
  }

  const opts = options || {};
  const getCanOverride = typeof opts.getCanOverride === "function" ? opts.getCanOverride : () => true;
  const getCanMoveState = typeof opts.getCanMoveState === "function" ? opts.getCanMoveState : () => false;
  const getActiveTasksAtActivity = typeof opts.getActiveTasksAtActivity === "function"
    ? opts.getActiveTasksAtActivity
    : () => [];
  const getCompletedAssignees = typeof opts.getCompletedAssignees === "function"
    ? opts.getCompletedAssignees
    : () => Promise.resolve([]);

  const eventBus = viewerHandle.viewer.get("eventBus");
  const contextMenu = createExecutionContextMenu(dotNetRef, viewerHandle.cssScopeAttribute, {
    getCompletedAssignees
  });

  const onElementContextMenu = (event) => {
    const element = event?.element;
    const originalEvent = event?.originalEvent;

    if (!element || element.waypoints || !originalEvent) {
      contextMenu.hide();
      return;
    }

    const businessObject = element.businessObject;
    if (!businessObject || typeof businessObject.$type !== "string") {
      contextMenu.hide();
      return;
    }

    // The root process / pool / participant rectangles aren't activity nodes.
    // Right-clicking the canvas itself shouldn't surface admin actions.
    const $type = businessObject.$type;
    if ($type === "bpmn:Process"
      || $type === "bpmn:Collaboration"
      || $type === "bpmn:Participant"
      || $type === "bpmn:Lane"
      || $type === "bpmn:LaneSet"
      || $type === "bpmn:TextAnnotation"
      || $type === "bpmn:Group") {
      contextMenu.hide();
      return;
    }

    const currentActivityIds = viewerHandle.getCurrentActivityIds?.() || [];
    const isCurrentActivity = currentActivityIds.includes(element.id);
    const activityName = businessObject.name || null;

    let activeTasks = [];
    const showTaskActions = isCurrentActivity && getCanOverride();
    if (showTaskActions) {
      activeTasks = getActiveTasksAtActivity(element.id, activityName) || [];
    }

    // Move-here is meaningful only on a *different* node from the current
    // activity, and only while the run is still in flight (some current
    // activities exist to cancel and replace).
    const showMoveHere = !isCurrentActivity
      && getCanMoveState()
      && currentActivityIds.length > 0;

    const hasTaskActionsToShow = showTaskActions && activeTasks.length > 0;
    if (!hasTaskActionsToShow && !showMoveHere) {
      contextMenu.hide();
      return;
    }

    originalEvent.preventDefault();
    originalEvent.stopPropagation();

    // clientX/clientY are viewport-relative — matches our position: fixed
    // wrapper. pageX/pageY would add scroll offset and place the menu below
    // the cursor on scrolled pages.
    contextMenu.show({
      activityId: element.id,
      activityName,
      tasks: activeTasks,
      showMoveHere,
      x: originalEvent.clientX,
      y: originalEvent.clientY
    });
  };

  const onCanvasClick = () => contextMenu.hide();
  const onViewboxChanged = () => contextMenu.hide();

  eventBus.on("element.contextmenu", onElementContextMenu);
  eventBus.on("canvas.click", onCanvasClick);
  eventBus.on("canvas.viewbox.changed", onViewboxChanged);

  viewerHandle.setContextMenu({
    dispose() {
      contextMenu.dispose();
      eventBus.off("element.contextmenu", onElementContextMenu);
      eventBus.off("canvas.click", onCanvasClick);
      eventBus.off("canvas.viewbox.changed", onViewboxChanged);
    }
  });
}

// options.getInfo(activityId, activityName, bpmn) → { title, rows } | null
//   Where bpmn is { assignee, dueDate } pulled from flowable:* attributes
//   (always present for shape sake; null on non-userTask elements). Returning
//   null suppresses the tooltip for that element.
//
// options.failedActivityIds: readonly string[] — the set of activities the
//   diagram has marked failed. Hover fires on any element whose id is in
//   this set, in addition to all bpmn:UserTask elements. The React side
//   decides what to render via getInfo.
export function enableUserTaskHoverTooltip(viewerHandle, options) {
  if (!viewerHandle?.viewer) {
    return;
  }

  const opts = options || {};
  const getInfo = typeof opts.getInfo === "function" ? opts.getInfo : null;
  if (!getInfo) {
    return;
  }

  // Held in a closure-mutable ref so the React side can update the failed set
  // without rebuilding the viewer. workflow.js exposes setFailedActivityIds
  // below for the hook to call on prop change.
  let failedActivityIds = new Set(Array.isArray(opts.failedActivityIds) ? opts.failedActivityIds : []);

  const eventBus = viewerHandle.viewer.get("eventBus");
  const tooltip = createUserTaskHoverTooltip(viewerHandle.cssScopeAttribute);

  const shouldShowFor = (element) => {
    if (!element || element.waypoints) return false;
    const businessObject = element.businessObject;
    if (!businessObject) return false;
    if (businessObject.$type === "bpmn:UserTask") return true;
    return failedActivityIds.has(element.id);
  };

  const onHover = (event) => {
    const element = event?.element;
    if (!shouldShowFor(element)) {
      return;
    }
    const businessObject = element.businessObject;
    const activityName = typeof businessObject.name === "string" ? businessObject.name : null;
    const bpmn = {
      assignee: readFlowableString(businessObject, "assignee"),
      dueDate: readFlowableString(businessObject, "dueDate")
    };

    const info = getInfo(element.id, activityName, bpmn);
    if (!info) {
      tooltip.hide();
      return;
    }

    const gfx = event?.gfx;
    const rect = gfx?.getBoundingClientRect?.();
    if (!rect) {
      return;
    }
    tooltip.show(info, rect);
  };

  const onOut = (event) => {
    const element = event?.element;
    if (!shouldShowFor(element)) {
      return;
    }
    tooltip.hide();
  };

  const onCanvasClick = () => tooltip.hide();
  const onViewboxChanged = () => tooltip.hide();

  eventBus.on("element.hover", onHover);
  eventBus.on("element.out", onOut);
  eventBus.on("canvas.click", onCanvasClick);
  eventBus.on("canvas.viewbox.changed", onViewboxChanged);

  viewerHandle.setHoverTooltip({
    setFailedActivityIds(ids) {
      failedActivityIds = new Set(Array.isArray(ids) ? ids : []);
    },
    dispose() {
      tooltip.dispose();
      eventBus.off("element.hover", onHover);
      eventBus.off("element.out", onOut);
      eventBus.off("canvas.click", onCanvasClick);
      eventBus.off("canvas.viewbox.changed", onViewboxChanged);
    }
  });
}

function createUserTaskHoverTooltip(cssScopeAttribute) {
  const root = document.createElement("div");
  root.className = "workflow-execution-task-tooltip";
  root.style.position = "fixed";
  // Above the BPMN context menu wrapper (1080) so it stacks correctly when
  // a right-click menu is also being prepared, but below modal dialogs.
  root.style.zIndex = "1075";
  root.style.pointerEvents = "none";
  root.style.display = "none";
  if (cssScopeAttribute) {
    root.setAttribute(cssScopeAttribute, "");
  }
  document.body.appendChild(root);

  let isDisposed = false;

  const setScope = (el) => {
    if (cssScopeAttribute) {
      el.setAttribute(cssScopeAttribute, "");
    }
  };

  return {
    show(info, anchorRect) {
      if (isDisposed) {
        return;
      }

      root.replaceChildren();

      const title = document.createElement("div");
      title.className = "workflow-execution-task-tooltip__title";
      title.textContent = info?.title ?? "";
      setScope(title);
      root.appendChild(title);

      const rows = Array.isArray(info?.rows) ? info.rows : [];
      for (const row of rows) {
        const r = document.createElement("div");
        r.className = "workflow-execution-task-tooltip__row";
        setScope(r);

        const label = document.createElement("span");
        label.className = "workflow-execution-task-tooltip__label";
        label.textContent = `${row.label}: `;
        setScope(label);

        const value = document.createElement("span");
        value.className = "workflow-execution-task-tooltip__value";
        value.textContent = row.value;
        setScope(value);

        r.appendChild(label);
        r.appendChild(value);
        root.appendChild(r);
      }

      // Two-pass position: render off-screen first to measure, then place
      // above the element — flipping below if there isn't room — and clamp
      // horizontally so the tip never overflows the viewport.
      root.style.display = "block";
      root.style.left = "-9999px";
      root.style.top = "-9999px";
      const tipRect = root.getBoundingClientRect();
      const margin = 8;
      let x = anchorRect.left + anchorRect.width / 2 - tipRect.width / 2;
      let y = anchorRect.top - tipRect.height - margin;
      if (y < margin) {
        y = anchorRect.bottom + margin;
      }
      x = Math.max(margin, Math.min(x, window.innerWidth - tipRect.width - margin));
      root.style.left = `${x}px`;
      root.style.top = `${y}px`;
    },
    hide() {
      if (isDisposed) {
        return;
      }
      root.style.display = "none";
    },
    dispose() {
      if (isDisposed) {
        return;
      }
      isDisposed = true;
      root.remove();
    }
  };
}

export function disposeModeler(modelerHandle) {
  modelerHandle?.dispose?.();
}

function getCssScopeAttribute(element) {
  return element?.getAttributeNames?.().find((name) => name.startsWith("b-")) || null;
}

// Minimal single-item context menu for the Workflow Studio. Right-clicking a
// node or edge surfaces "Configure…" which the React layer routes to the
// element's editor modal. Mirrors the structure of createExecutionContextMenu
// but stays intentionally bare — no submenus, no async lookups — because the
// only action is "open the modal for this element."
function createConfigureContextMenu(cssScopeAttribute) {
  const menu = document.createElement("div");
  menu.className = "workflow-studio-context-menu";
  menu.style.position = "fixed";
  menu.style.zIndex = "1080";
  if (cssScopeAttribute) {
    menu.setAttribute(cssScopeAttribute, "");
  }

  const list = document.createElement("ul");
  list.className = "dropdown-menu workflow-studio-context-menu__list";
  list.hidden = true;
  // Bootstrap's .dropdown-menu defaults to position:absolute and is normally
  // placed by Popper. Without Popper, anchor it to the wrapper's origin so
  // x/y coords on the wrapper place the list correctly.
  list.style.position = "static";
  list.style.margin = "0";
  if (cssScopeAttribute) {
    list.setAttribute(cssScopeAttribute, "");
  }

  menu.appendChild(list);
  document.body.appendChild(menu);

  let isDisposed = false;

  const hide = () => {
    if (isDisposed) {
      return;
    }

    list.hidden = true;
    list.classList.remove("show");
    menu.setAttribute("aria-hidden", "true");
    list.replaceChildren();
  };

  const setScope = (el) => {
    if (cssScopeAttribute) {
      el.setAttribute(cssScopeAttribute, "");
    }
  };

  const buildItem = (label, handler) => {
    const li = document.createElement("li");
    setScope(li);
    const button = document.createElement("button");
    button.type = "button";
    button.className = "dropdown-item workflow-studio-context-menu__item";
    button.textContent = label;
    button.addEventListener("click", handler);
    setScope(button);
    li.appendChild(button);
    return li;
  };

  const onPointerDown = (event) => {
    if (list.hidden) {
      return;
    }
    // Right-clicks elsewhere are handled by the bpmn-js eventBus listener,
    // which re-shows the menu at the new position. Suppress the close path
    // here so we don't flicker.
    if (event.button === 2) {
      return;
    }
    if (menu.contains(event.target)) {
      return;
    }
    hide();
  };

  const onWindowResize = () => hide();
  const onKeyDown = (event) => {
    if (event.key === "Escape") {
      hide();
    }
  };

  document.addEventListener("pointerdown", onPointerDown, true);
  window.addEventListener("resize", onWindowResize);
  window.addEventListener("keydown", onKeyDown);

  return {
    show({ x, y, onConfigure }) {
      if (isDisposed) {
        return;
      }

      list.replaceChildren();
      list.appendChild(buildItem("Configure…", async () => {
        hide();
        await onConfigure();
      }));

      menu.style.left = `${x}px`;
      menu.style.top = `${y}px`;
      list.hidden = false;
      list.classList.add("show");
      menu.setAttribute("aria-hidden", "false");
    },
    hide,
    dispose() {
      if (isDisposed) {
        return;
      }

      isDisposed = true;
      document.removeEventListener("pointerdown", onPointerDown, true);
      window.removeEventListener("resize", onWindowResize);
      window.removeEventListener("keydown", onKeyDown);
      menu.remove();
    }
  };
}

function createExecutionContextMenu(dotNetRef, cssScopeAttribute, contextOptions) {
  const opts = contextOptions || {};
  const getCompletedAssignees = typeof opts.getCompletedAssignees === "function"
    ? opts.getCompletedAssignees
    : () => Promise.resolve([]);

  // Drop the Bootstrap "dropdown" class from the outer wrapper — it sets
  // position: relative and would put us into normal page flow. Force fixed
  // positioning inline so no later cascade can knock us out.
  const menu = document.createElement("div");
  menu.className = "workflow-execution-context-menu";
  menu.style.position = "fixed";
  menu.style.zIndex = "1080";
  if (cssScopeAttribute) {
    menu.setAttribute(cssScopeAttribute, "");
  }

  const list = document.createElement("ul");
  list.className = "dropdown-menu workflow-execution-context-menu__list";
  list.hidden = true;
  // Bootstrap's .dropdown-menu defaults to position:absolute and is normally
  // placed by Popper. Without Popper, anchor it to the wrapper's origin so
  // x/y coords on the wrapper place the list correctly.
  list.style.position = "static";
  list.style.margin = "0";
  if (cssScopeAttribute) {
    list.setAttribute(cssScopeAttribute, "");
  }

  menu.appendChild(list);
  document.body.appendChild(menu);

  let isDisposed = false;
  // Submenu lives at top level for z-index isolation; only one is open at a time.
  let activeSubmenu = null;

  const hide = () => {
    if (isDisposed) {
      return;
    }

    list.hidden = true;
    list.classList.remove("show");
    menu.setAttribute("aria-hidden", "true");
    list.replaceChildren();
    closeSubmenu();
  };

  const closeSubmenu = () => {
    if (activeSubmenu) {
      activeSubmenu.remove();
      activeSubmenu = null;
    }
  };

  const setScope = (el) => {
    if (cssScopeAttribute) {
      el.setAttribute(cssScopeAttribute, "");
    }
  };

  const buildItem = (label, handler, { disabled = false } = {}) => {
    const li = document.createElement("li");
    setScope(li);
    const button = document.createElement("button");
    button.type = "button";
    button.className = "dropdown-item workflow-execution-context-menu__item";
    button.textContent = label;
    if (disabled) {
      button.classList.add("disabled");
      button.setAttribute("aria-disabled", "true");
      button.disabled = true;
    } else {
      button.addEventListener("click", handler);
    }
    setScope(button);
    li.appendChild(button);
    return li;
  };

  const buildSubmenuItem = (label, onActivate) => {
    const li = document.createElement("li");
    setScope(li);
    const button = document.createElement("button");
    button.type = "button";
    button.className = "dropdown-item workflow-execution-context-menu__item dropdown-toggle";
    button.textContent = label;
    setScope(button);
    button.addEventListener("click", (event) => {
      event.stopPropagation();
      const rect = button.getBoundingClientRect();
      onActivate(rect);
    });
    li.appendChild(button);
    return li;
  };

  const renderSubmenu = (anchorRect, tasks, completedAssignees, onPick) => {
    closeSubmenu();
    const submenu = document.createElement("ul");
    submenu.className = "dropdown-menu workflow-execution-context-menu__list show";
    setScope(submenu);
    submenu.style.position = "fixed";
    submenu.style.left = `${anchorRect.right}px`;
    submenu.style.top = `${anchorRect.top}px`;
    submenu.style.zIndex = "1090";

    const completedSet = new Set(completedAssignees || []);
    for (const task of tasks) {
      const assignee = task.assignee || "(unassigned)";
      const alreadyCompleted = task.assignee ? completedSet.has(task.assignee) : false;
      const label = alreadyCompleted ? `${assignee} (completed)` : assignee;
      submenu.appendChild(buildItem(label, () => onPick(task), { disabled: alreadyCompleted }));
    }

    document.body.appendChild(submenu);
    activeSubmenu = submenu;
  };

  const onPointerDown = (event) => {
    if (list.hidden) {
      return;
    }
    if (event.button === 2) {
      return;
    }
    if (menu.contains(event.target)) {
      return;
    }
    if (activeSubmenu && activeSubmenu.contains(event.target)) {
      return;
    }
    hide();
  };

  const onWindowResize = () => hide();
  const onKeyDown = (event) => {
    if (event.key === "Escape") {
      hide();
    }
  };

  document.addEventListener("pointerdown", onPointerDown, true);
  window.addEventListener("resize", onWindowResize);
  window.addEventListener("keydown", onKeyDown);

  return {
    show({ activityId, activityName, tasks, showMoveHere, x, y }) {
      if (isDisposed) {
        return;
      }

      list.replaceChildren();
      closeSubmenu();

      const hasTasks = Array.isArray(tasks) && tasks.length > 0;
      if (!hasTasks && !showMoveHere) {
        return;
      }

      if (hasTasks && tasks.length === 1) {
        const onlyTask = tasks[0];
        list.appendChild(buildItem("Complete Task", async () => {
          hide();
          await dotNetRef.invokeMethodAsync(
            "CompleteTaskFromContextMenu", activityId, activityName, onlyTask.id);
        }));
        list.appendChild(buildItem("Reassign Task…", async () => {
          hide();
          await dotNetRef.invokeMethodAsync(
            "ReassignTaskFromContextMenu",
            activityId, activityName, onlyTask.id, onlyTask.assignee ?? null);
        }));
        list.appendChild(buildItem("Change Due Date…", async () => {
          hide();
          await dotNetRef.invokeMethodAsync(
            "ChangeDueDateFromContextMenu",
            activityId, activityName, onlyTask.id, onlyTask.dueDate ?? null);
        }));
      } else if (hasTasks) {
        const taskIds = tasks.map((t) => t.id);
        const taskCount = tasks.length;
        list.appendChild(buildItem(`Complete Task (all ${taskCount})`, async () => {
          if (!window.confirm(`Override-complete ${taskCount} runtime tasks at this step?`)) {
            return;
          }
          hide();
          await dotNetRef.invokeMethodAsync(
            "CompleteAllTasksFromContextMenu", activityId, activityName, taskIds);
        }));

        list.appendChild(buildSubmenuItem("Complete Task For…", async (anchorRect) => {
          let completed = [];
          try {
            completed = await getCompletedAssignees(activityId);
          } catch {
            // Submenu still renders without disabled state if the lookup fails.
          }
          renderSubmenu(anchorRect, tasks, completed, async (task) => {
            hide();
            await dotNetRef.invokeMethodAsync(
              "CompleteTaskFromContextMenu", activityId, activityName, task.id);
          });
        }));

        // Per-instance reassign + due-date submenus mirror "Complete Task For…":
        // pick which parallel instance to act on, then the React layer opens
        // the picker modal for that one task.
        list.appendChild(buildSubmenuItem("Reassign Task For…", (anchorRect) => {
          renderSubmenu(anchorRect, tasks, [], async (task) => {
            hide();
            await dotNetRef.invokeMethodAsync(
              "ReassignTaskFromContextMenu",
              activityId, activityName, task.id, task.assignee ?? null);
          });
        }));

        list.appendChild(buildSubmenuItem("Change Due Date For…", (anchorRect) => {
          renderSubmenu(anchorRect, tasks, [], async (task) => {
            hide();
            await dotNetRef.invokeMethodAsync(
              "ChangeDueDateFromContextMenu",
              activityId, activityName, task.id, task.dueDate ?? null);
          });
        }));
      }

      if (showMoveHere) {
        list.appendChild(buildItem("Move Execution Here", async () => {
          hide();
          await dotNetRef.invokeMethodAsync(
            "MoveExecutionHereFromContextMenu", activityId, activityName);
        }));
      }

      menu.style.left = `${x}px`;
      menu.style.top = `${y}px`;
      list.hidden = false;
      list.classList.add("show");
      menu.setAttribute("aria-hidden", "false");
    },
    hide,
    dispose() {
      if (isDisposed) {
        return;
      }

      isDisposed = true;
      closeSubmenu();
      document.removeEventListener("pointerdown", onPointerDown, true);
      window.removeEventListener("resize", onWindowResize);
      window.removeEventListener("keydown", onKeyDown);
      menu.remove();
    }
  };
}

function addMarkers(viewerHandle, elementIds, markerClass) {
  const elementRegistry = viewerHandle.viewer.get("elementRegistry");
  const canvas = viewerHandle.viewer.get("canvas");

  for (const elementId of elementIds || []) {
    const element = elementRegistry.get(elementId);
    if (!element || element.waypoints) {
      continue;
    }

    canvas.addMarker(elementId, markerClass);
    viewerHandle.activeMarkers.push({ elementId, markerClass });
  }
}

function clearExecutionState(viewerHandle) {
  const canvas = viewerHandle?.viewer?.get?.("canvas");
  if (!canvas || !viewerHandle?.activeMarkers) {
    return;
  }

  for (const marker of viewerHandle.activeMarkers) {
    canvas.removeMarker(marker.elementId, marker.markerClass);
  }

  viewerHandle.activeMarkers = [];
}
