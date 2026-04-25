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

const WORKFLOW_JS_VERSION = "20260421_02";

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
  const notifySelectionChanged = async (element) => {
    if (!dotNetRef) {
      return;
    }

    await dotNetRef.invokeMethodAsync("NotifySelectionChanged", describeElement(element));
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
    modeler.get("canvas").zoom("fit-viewport");
    lastImportDebug = buildImportDebug(modeler, container, importResult?.warnings ?? []);
    await notifySelectionChanged(null);
  } finally {
    suppressDirtyEvents = false;
  }

  const onSelectionChanged = async (event) => {
    await notifySelectionChanged(event?.newSelection?.[0] ?? null);
  };

  eventBus?.on?.("selection.changed", onSelectionChanged);

  return {
    modeler,
    container,
    lastImportDebug,
    setSuppressDirtyEvents(value) {
      suppressDirtyEvents = value;
    },
    dispose() {
      eventBus?.off?.("selection.changed", onSelectionChanged);
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
  viewer.get("canvas").zoom("fit-viewport");

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
    dispose() {
      contextMenu?.dispose?.();
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
      `<bpmn:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" id="${escapeXml(definitionId)}" targetNamespace="http://autonate.dev/workflows">`,
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
  return {
    id: businessObject.id,
    type: businessObject.$type,
    name: typeof businessObject.name === "string" ? businessObject.name : null,
    scriptFormat: typeof businessObject.scriptFormat === "string" ? businessObject.scriptFormat : null,
    script: typeof businessObject.script === "string" ? businessObject.script : null,
    resultVariable: typeof businessObject.resultVariable === "string" ? businessObject.resultVariable : null,
    conditionExpression: typeof conditionExpression?.body === "string" ? conditionExpression.body : null,
    assignee: readFlowableString(businessObject, "assignee"),
    candidateUsers: readFlowableList(businessObject, "candidateUsers"),
    candidateGroups: readFlowableList(businessObject, "candidateGroups")
  };
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
    scriptFormat: "javascript",
    script: typeof task.script === "string" ? task.script : "",
    resultVariable: normalizeOptionalString(task.resultVariable)
  });
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

  writeFlowableAttribute(businessObject, "assignee", assignee);
  writeFlowableAttribute(businessObject, "candidateUsers", candidateUsers);
  writeFlowableAttribute(businessObject, "candidateGroups", candidateGroups);

  modeling.updateProperties(element, {
    name: normalizeOptionalString(task.name)
  });
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

export async function loadXml(modelerHandle, xml) {
  modelerHandle.setSuppressDirtyEvents(true);
  try {
    await modelerHandle.modeler.importXML(xml);
    modelerHandle.modeler.get("canvas").zoom("fit-viewport");
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
  viewerHandle.viewer.get("canvas").zoom("fit-viewport");
}

export function highlightExecutionState(viewerHandle, completedActivityIds, currentActivityIds) {
  clearExecutionState(viewerHandle);

  console.log("[AutoNate viewer] highlightExecutionState", {
    completed: completedActivityIds,
    current: currentActivityIds
  });

  viewerHandle?.setCurrentActivityIds?.(currentActivityIds);
  addMarkers(viewerHandle, completedActivityIds, "execution-step-completed");
  addMarkers(viewerHandle, currentActivityIds, "execution-step-current");
}

export function enableCurrentStepContextMenu(viewerHandle, dotNetRef) {
  if (!viewerHandle?.viewer || !dotNetRef) {
    return;
  }

  const eventBus = viewerHandle.viewer.get("eventBus");
  const contextMenu = createExecutionContextMenu(dotNetRef, viewerHandle.cssScopeAttribute);

  const onElementContextMenu = (event) => {
    const element = event?.element;
    const originalEvent = event?.originalEvent;

    if (!element || element.waypoints || !originalEvent) {
      contextMenu.hide();
      return;
    }

    const currentActivityIds = viewerHandle.getCurrentActivityIds?.() || [];
    if (!currentActivityIds.includes(element.id)) {
      contextMenu.hide();
      return;
    }

    originalEvent.preventDefault();
    originalEvent.stopPropagation();

    contextMenu.show({
      activityId: element.id,
      activityName: element.businessObject?.name || null,
      x: originalEvent.pageX,
      y: originalEvent.pageY
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

export function disposeModeler(modelerHandle) {
  modelerHandle?.dispose?.();
}

function getCssScopeAttribute(element) {
  return element?.getAttributeNames?.().find((name) => name.startsWith("b-")) || null;
}

function createExecutionContextMenu(dotNetRef, cssScopeAttribute) {
  const menu = document.createElement("div");
  menu.className = "workflow-execution-context-menu dropdown";
  if (cssScopeAttribute) {
    menu.setAttribute(cssScopeAttribute, "");
  }

  const list = document.createElement("ul");
  list.className = "dropdown-menu workflow-execution-context-menu__list";
  list.hidden = true;
  if (cssScopeAttribute) {
    list.setAttribute(cssScopeAttribute, "");
  }

  const listItem = document.createElement("li");
  if (cssScopeAttribute) {
    listItem.setAttribute(cssScopeAttribute, "");
  }

  const button = document.createElement("button");
  button.type = "button";
  button.className = "dropdown-item workflow-execution-context-menu__item";
  button.textContent = "Complete Task";
  if (cssScopeAttribute) {
    button.setAttribute(cssScopeAttribute, "");
  }

  listItem.appendChild(button);
  list.appendChild(listItem);
  menu.appendChild(list);
  document.body.appendChild(menu);

  let currentActivityId = null;
  let isDisposed = false;

  const hide = () => {
    if (isDisposed) {
      return;
    }

    list.hidden = true;
    list.classList.remove("show");
    menu.setAttribute("aria-hidden", "true");
    currentActivityId = null;
    delete button.dataset.activityName;
  };

  const onPointerDown = (event) => {
    if (list.hidden || menu.contains(event.target)) {
      return;
    }

    hide();
  };

  const onWindowBlur = () => hide();
  const onWindowResize = () => hide();
  const onKeyDown = (event) => {
    if (event.key === "Escape") {
      hide();
    }
  };

  const onButtonClick = async () => {
    if (!currentActivityId) {
      return;
    }

    const activityId = currentActivityId;
    const activityName = button.dataset.activityName || null;
    hide();
    await dotNetRef.invokeMethodAsync("CompleteTaskFromContextMenu", activityId, activityName);
  };

  document.addEventListener("pointerdown", onPointerDown, true);
  window.addEventListener("blur", onWindowBlur);
  window.addEventListener("resize", onWindowResize);
  window.addEventListener("keydown", onKeyDown);
  button.addEventListener("click", onButtonClick);

  return {
    show({ activityId, activityName, x, y }) {
      if (isDisposed) {
        return;
      }

      currentActivityId = activityId;
      button.dataset.activityName = activityName || "";
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
      delete button.dataset.activityName;
      document.removeEventListener("pointerdown", onPointerDown, true);
      window.removeEventListener("blur", onWindowBlur);
      window.removeEventListener("resize", onWindowResize);
      window.removeEventListener("keydown", onKeyDown);
      button.removeEventListener("click", onButtonClick);
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
