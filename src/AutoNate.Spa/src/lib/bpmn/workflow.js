import { createManifestPaletteProvider, createManifestMenuFilter } from "./palette";


const WORKFLOW_JS_VERSION = "20260425_01";

// #167: what the most recent import converted, drained by the studio so it can say
// so once. Module scope on purpose — createModeler writes it during importXML,
// before any handle exists for a caller to read from.
let convertedOnImport = [];

export async function createModeler(container, xml, dotNetRef) {
  if (typeof window.BpmnJS === "undefined") {
    throw new Error("bpmn-js is not available on window.");
  }

  if (container && typeof container.replaceChildren === "function") {
    container.replaceChildren();
  }

  // #241. Overrides bpmn-js's stock palette provider, which offers every element
  // the library ships regardless of what the support manifest says we run.
  const modeler = new window.BpmnJS({
    container,
    additionalModules: [createManifestPaletteProvider(), createManifestMenuFilter()]
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
    // #167: an imported or hand-edited diagram is the path the drop handler cannot
    // reach, and the one a happy-path test misses.
    convertedOnImport = convertNonWaitingTasks(modeler);
    fitAndCenter(modeler);
    refreshScriptIdentityMarkers({ modeler });
    lastImportDebug = buildImportDebug(modeler, container, importResult?.warnings ?? []);
  } finally {
    suppressDirtyEvents = false;
  }

  // #167: and on drop, so an author who reaches for a manual task from bpmn-js's own
  // menu gets a user task immediately rather than at save time.
  modeler.get("eventBus", false)?.on("shape.added", (event) => {
    const type = event?.element?.businessObject?.$type;
    if (!CONVERTED_TASK_TYPES[type]) return;
    // Deferred: replacing inside the shape.added handler re-enters the command
    // stack while it is still applying the addition.
    setTimeout(() => {
      const converted = convertNonWaitingTasks(modeler);
      if (converted.length > 0 && dotNetRef) {
        dotNetRef.invokeMethodAsync("NotifyTasksConverted", converted).catch(() => {});
      }
    }, 0);
  });

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
  const conditionalEvent = describeConditionalEvent(businessObject);
  const timerBoundary = describeTimerBoundaryEvent(businessObject);
  const serviceTask = describeServiceTask(businessObject);
  const description = {
    id: businessObject.id,
    type: businessObject.$type,
    name: typeof businessObject.name === "string" ? businessObject.name : null,
    scriptFormat:
      businessObject.$type === "bpmn:ComplexGateway"
        ? readAutoNateAttribute(businessObject, "scriptFormat")
        : typeof businessObject.scriptFormat === "string"
          ? businessObject.scriptFormat
          : null,
    // #153. Stored in the autonate namespace, which is on the do-not-rename
    // list. Read from $attrs the same way every other namespaced property in
    // this file is: bpmn-js has no moddle extension loaded for it, so
    // modeling.updateProperties would serialise it without the prefix.
    runAs: readAutoNateAttribute(businessObject, "runAs"),
    // #218. A complex gateway's routing script is an autonate: ATTRIBUTE, not a
    // <bpmn:script> child. bpmn-js's moddle has no script property on
    // ComplexGateway and DROPS the child on save — proven in
    // ComplexGatewayStudioRoundTripTests, where a seeded child came back gone.
    // $attrs survives, which is the same route runAs already takes.
    script:
      businessObject.$type === "bpmn:ComplexGateway"
        ? readAutoNateAttribute(businessObject, "routeScript")
        : typeof businessObject.script === "string"
          ? businessObject.script
          : null,
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

  // #159/#163/#166. The three element-data shapes, merged CONDITIONALLY so a
  // key's presence is what routes the studio (load-bearing fact 3). An
  // unconditional key would send every subprocess to the ad-hoc panel.
  const elementData = describeElementData(businessObject);
  if (elementData) {
    Object.assign(description, elementData);
  }

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

  if (conditionalEvent) {
    // #158. Merged only when the element actually carries a conditional event
    // definition — see describeConditionalEvent for why the key must be absent
    // rather than null on everything else.
    //
    // conditionExpression is deliberately the same key sequence flows use: it is
    // the same concept, and $type separates the two.
    description.conditionExpression = conditionalEvent.conditionExpression;
    description.cancelActivity = conditionalEvent.cancelActivity;
  }

  if (timerBoundary) {
    // #157. Only present on a boundary event carrying a timer definition, so the
    // keys are ABSENT on conditional boundary events — which is what keeps the two
    // boundary editors apart, since both carry cancelActivity.
    description.boundaryTimerDuration = timerBoundary.boundaryTimerDuration;
    description.boundaryTimerDate = timerBoundary.boundaryTimerDate;
    description.boundaryTimerCycle = timerBoundary.boundaryTimerCycle;
    description.cancelActivity = timerBoundary.cancelActivity;
    description.attachedTo = timerBoundary.attachedTo;
  }

  if (serviceTask) {
    // Only present for service tasks the studio recognizes (delegateExpression
    // points at the AutoNate behavior bridge). Lets the studio route the
    // selection to the service-task modal and pre-populate the picker.
    description.serviceTaskKind = serviceTask.serviceTaskKind;
    description.behaviorKey = serviceTask.behaviorKey;
    description.retryPoint = serviceTask.retryPoint;
  }

  const callActivity = describeCallActivity(businessObject);
  if (callActivity) {
    // #113. Present only on a call activity.
    description.calledElement = callActivity.calledElement;
    description.callInputs = callActivity.callInputs;
    description.callOutputs = callActivity.callOutputs;
  }

  const signalEvent = describeSignalElement(businessObject);
  if (signalEvent) {
    // #156. Present only on signal-carrying events.
    description.signalEventName = signalEvent.signalEventName;
    description.signalEventScope = signalEvent.signalEventScope;
    description.signalEventIsNew = signalEvent.signalEventIsNew;
    description.signalEventInterrupting = signalEvent.signalEventInterrupting;
  }

  const codedEvent = describeCodedEvent(businessObject);
  if (codedEvent) {
    // #114. Present only on error/escalation events; absence keeps everything
    // else out of the code editor.
    description.codedEventKind = codedEvent.codedEventKind;
    description.codedEventCode = codedEvent.codedEventCode;
    description.codedEventInterrupting = codedEvent.codedEventInterrupting;
  }

  const messageElement = describeMessageElement(businessObject);
  if (messageElement) {
    // #112. Present only on message-carrying elements, receive tasks and send
    // tasks — absence is what keeps every other element out of the message
    // editor.
    description.messageDirection = messageElement.messageDirection;
    description.messageCorrelationKey = messageElement.messageCorrelationKey;
    description.messageTargetProcessKey = messageElement.messageTargetProcessKey;
    description.messageName = messageElement.messageName;
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
    behaviorKey: behaviorKey,
    // #168. flowable:async is the retry point: the engine commits before the
    // step, so a failure retries the step alone instead of discarding
    // everything since the last checkpoint. Absent means off — Flowable's own
    // default — so anything that is not the string "true" reads as false.
    retryPoint: readFlowableServiceTaskAttr(businessObject, "async") === "true"
  };
}

// The autonate-namespace equivalents of the flowable: helpers above. The
// namespace URI is on the do-not-rename list: changing it orphans the property
// on every diagram that already carries it.
const AUTONATE_ATTR_PREFIX = "autonate:";
const FLOWABLE_ATTR_PREFIX = "flowable:";
const FLOWABLE_NAMESPACE = "http://flowable.org/bpmn";

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

// #159/#163/#166. Everything an author configures on an ad-hoc subprocess, a
// data object, or a multi-instance marker.
//
// All of it is read from autonate:/flowable: attributes rather than moddle
// properties or child elements, because bpmn-js drops what its moddle does not
// model — a <bpmn:completionCondition> on one of these is gone on the next save,
// taking the author's condition with it. Publish rewrites the attributes into
// the elements the engine reads.
function describeElementData(businessObject) {
  const type = businessObject?.$type;
  if (!type) return null;

  if (type === "bpmn:AdHocSubProcess") {
    return {
      adhocCompletionCondition: readAutoNateAttribute(businessObject, "completionCondition") ?? "",
      adhocOrdering: businessObject.ordering === "Sequential" ? "Sequential" : "Parallel"
    };
  }

  if (type === "bpmn:DataObjectReference" || type === "bpmn:DataObject") {
    return { dataObjectType: readAutoNateAttribute(businessObject, "dataType") ?? "" };
  }

  // The marker lives on the ACTIVITY, in loopCharacteristics — bpmn-js's own
  // replace menu puts it there, so an author can already apply the marker; what
  // it cannot do is fill in the fields behind it.
  const loop = businessObject.loopCharacteristics;
  if (loop && loop.$type === "bpmn:MultiInstanceLoopCharacteristics") {
    return {
      multiInstanceCollection: readFlowableString(loop, "collection") ?? "",
      multiInstanceElementVariable: readFlowableString(loop, "elementVariable") ?? "",
      multiInstanceCompletionCondition: readAutoNateAttribute(loop, "completionCondition") ?? "",
      // #245. Stored as attributes for the same reason the completion condition
      // is: bpmn-js has no Flowable moddle extension, so a <bpmn:loopCardinality>
      // child and a <flowable:variableAggregation> extension element are both
      // dropped on the author's next save. Publish rebuilds them.
      multiInstanceCardinality: readAutoNateAttribute(loop, "loopCardinality") ?? "",
      multiInstanceAggregateTarget: readAutoNateAttribute(loop, "aggregateTarget") ?? "",
      multiInstanceAggregateSource: readAutoNateAttribute(loop, "aggregateSource") ?? "",
      // isSequential defaults to false in BPMN, and parallel is the common case.
      multiInstanceSequential: loop.isSequential === true
    };
  }

  return null;
}

function readAutoNateAttribute(businessObject, name) {
  const value = businessObject?.$attrs?.[`${AUTONATE_ATTR_PREFIX}${name}`];
  return typeof value === "string" && value.length > 0 ? value : null;
}

function writeAutoNateAttribute(businessObject, name, value) {
  if (!businessObject) return;

  // MUTATE $attrs; never assign it. moddle defines $attrs on Base with only a
  // getter, so `businessObject.$attrs = ...` throws
  //   "Cannot set property $attrs of #<Base> which has only a getter"
  // and the assignment above did that unconditionally. It went unnoticed because
  // the elements this was used on until now already had a writable own property;
  // a complex gateway does not, so the whole apply failed with the panel left
  // open over the Save button (#218).
  let attrs = businessObject.$attrs;
  if (!attrs) {
    try {
      businessObject.$attrs = {};
    } catch {
      // Getter-only and nothing behind it. Nowhere to write.
    }
    attrs = businessObject.$attrs;
  }
  if (!attrs) return;

  const key = `${AUTONATE_ATTR_PREFIX}${name}`;
  if (value === null || value === undefined || value === "") {
    delete attrs[key];
    return;
  }
  attrs[key] = value;
}

// #113. A call activity runs another workflow as a step. Three things matter:
// which workflow, what goes in, and what comes back.
//
// The KEY is stored, not a version — the studio shows the author what they
// picked. Publish resolves it to the exact definition that exists then and pins
// the deployed copy to it, so republishing the child cannot change what an
// already-deployed parent calls.
function describeCallActivity(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:CallActivity") return null;

  const extension = businessObject.extensionElements;
  const values = Array.isArray(extension?.values) ? extension.values : [];
  // Matched case-insensitively on the local name. These are written with
  // moddle.createAny (the studio loads no Flowable moddle extension, so there is
  // no typed flowable:In to create), which round-trips the qualified name exactly
  // as authored — and a diagram from another modeller may capitalise differently.
  const mappings = (localName) =>
    values
      .filter((value) => {
        const type = value?.$type ?? "";
        const local = type.includes(":") ? type.split(":")[1] : type;
        return local.toLowerCase() === localName;
      })
      .map((value) => ({
        source: value.source ?? value.$attrs?.source ?? "",
        target: value.target ?? value.$attrs?.target ?? ""
      }))
      .filter((pair) => pair.source.length > 0 || pair.target.length > 0);

  return {
    calledElement: businessObject.calledElement ?? "",
    callInputs: mappings("in"),
    callOutputs: mappings("out")
  };
}

// #113. Writes the chosen workflow key and the variable mappings.
export function updateCallActivityProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the call activity.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:CallActivity") {
    throw new Error(`Call activity '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;

  const pairs = (list, type) =>
    (Array.isArray(list) ? list : [])
      .map((pair) => ({
        source: normalizeOptionalString(pair?.source),
        target: normalizeOptionalString(pair?.target)
      }))
      // A half-filled row maps nothing and would serialise as an attribute
      // pointing at an empty name, which reads as configured and is not.
      .filter((pair) => pair.source && pair.target)
      // createAny, not create: bpmn-js here loads no Flowable moddle extension, so
      // "flowable:In" is not a type it knows and create() throws on it. createAny
      // produces an element that serialises under the qualified name given, which
      // is what the engine reads.
      .map((pair) =>
        moddle.createAny(type, FLOWABLE_NAMESPACE, {
          source: pair.source,
          target: pair.target
        }));

  const mappings = [
    ...pairs(payload.inputs, "flowable:in"),
    ...pairs(payload.outputs, "flowable:out")
  ];

  // Everything that is NOT a mapping is preserved — a call activity may carry
  // other extension elements, and rebuilding the list from scratch would drop
  // them silently.
  const isMapping = (value) => {
    const type = value?.$type ?? "";
    const local = type.includes(":") ? type.split(":")[1] : type;
    return local.toLowerCase() === "in" || local.toLowerCase() === "out";
  };
  const existing = Array.isArray(businessObject.extensionElements?.values)
    ? businessObject.extensionElements.values.filter((value) => value && !isMapping(value))
    : [];

  const combined = [...existing, ...mappings];
  const extensionElements =
    combined.length > 0
      ? moddle.create("bpmn:ExtensionElements", { values: combined })
      : undefined;

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name),
    calledElement: normalizeOptionalString(payload.calledElement),
    extensionElements
  });
}

// #156. Signal events: throw, catch, boundary and end. Two things matter — the
// name, which is what matches one end to the other, and the scope.
//
// Scope lives on the root <bpmn:signal> element as Flowable's own
// flowable:scope, so THE ENGINE enforces it. The alternative was an Auton8
// attribute and our own filtering on top of a broadcast, which would have meant
// reimplementing something the engine already does correctly.
//
//   instance — flowable:scope="processInstance". Only this run of this workflow.
//   global   — no attribute. Any subscriber anywhere, which is BPMN's default and
//              the reason two unrelated workflows using "approved" silently couple.
function describeSignalElement(businessObject) {
  if (!businessObject) return null;

  const definitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const definition = definitions.find((d) => d && d.$type === "bpmn:SignalEventDefinition");
  if (!definition) return null;

  const ref = definition.signalRef;
  const resolved = typeof ref === "string" ? null : ref;

  return {
    signalEventName: (typeof ref === "string" ? ref : resolved?.name ?? resolved?.id) ?? "",
    // Absent means global — Flowable's default, and the shape every diagram
    // authored before this story carries. Reported as it is rather than
    // defaulted to instance, so opening an existing signal does not silently
    // propose changing what a deployed process does.
    // Read from the EVENT, not the signal root: moddle refuses to attach an
    // attribute to a freshly created root element ("Cannot set property $attrs
    // of #<Base> which has only a getter"), so the author's choice is recorded
    // here and publish writes Flowable's own flowable:scope onto the root.
    // Absent means global — Flowable's default, and what every diagram authored
    // before this story carries.
    signalEventScope: readSignalScope(businessObject),
    // A new element has no signal yet; the editor defaults THOSE to instance.
    signalEventIsNew: !ref,
    signalEventInterrupting:
      businessObject.$type === "bpmn:BoundaryEvent"
        ? businessObject.cancelActivity !== false
        : null
  };
}

// Absent means global — Flowable's default, and what every diagram authored
// before this story carries. Read as it is rather than defaulted to instance, so
// opening an existing signal does not silently propose narrowing a deployed
// process.
function readSignalScope(businessObject) {
  const values = Array.isArray(businessObject?.extensionElements?.values)
    ? businessObject.extensionElements.values
    : [];
  const found = values.find((value) => {
    const type = value?.$type ?? "";
    const local = type.includes(":") ? type.split(":")[1] : type;
    return local === "autonateSignalScope";
  });
  const raw = found?.value ?? found?.$attrs?.value;
  return raw === "instance" ? "instance" : "global";
}

// #156. Writes the signal name and scope, maintaining the root element behind it.
export function updateSignalElementProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update this signal event.");
  }

  const element = elementRegistry.get(payload.id);
  const businessObject = element?.businessObject;
  if (!businessObject) {
    throw new Error(`Element '${payload.id}' is no longer available in the diagram.`);
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const definition = eventDefinitions.find(
    (d) => d && d.$type === "bpmn:SignalEventDefinition"
  );
  if (!definition) {
    throw new Error(`Element '${payload.id}' does not carry a signal definition.`);
  }

  const name = normalizeOptionalString(payload.signalName);
  const scope = payload.scope === "global" ? "global" : "instance";
  const root = name ? ensureSignalRootElement(modeler, moddle, name) : undefined;

  modeling.updateModdleProperties(element, definition, { signalRef: root });

  // The scope rides on the event as an extension ELEMENT, and publish moves it
  // onto the signal root as Flowable's own attribute.
  //
  // An element, not an attribute, after two attribute routes failed on an event
  // parsed without any extension attribute: `$attrs` is getter-only on those, so
  // a direct write silently did nothing, and a namespaced key through
  // updateProperties did not serialise either. createAny is the mechanism this
  // file already uses for the call activity's in/out mappings, and it round-trips.
  const scopeElement = moddle.createAny(
    "flowable:autonateSignalScope", FLOWABLE_NAMESPACE, { value: scope });

  const keptExtensions = Array.isArray(businessObject.extensionElements?.values)
    ? businessObject.extensionElements.values.filter((value) => {
        const type = value?.$type ?? "";
        const local = type.includes(":") ? type.split(":")[1] : type;
        return local !== "autonateSignalScope";
      })
    : [];

  const properties = {
    name: normalizeOptionalString(payload.name),
    extensionElements: moddle.create("bpmn:ExtensionElements", {
      values: [...keptExtensions, scopeElement]
    })
  };
  if (businessObject.$type === "bpmn:BoundaryEvent" && typeof payload.interrupting === "boolean") {
    properties.cancelActivity = payload.interrupting;
  }
  modeling.updateProperties(element, properties);
}

// One root element per name. Scope is NOT part of the key here: it lives on the
// event until publish, which is what separates two events sharing a name but not
// a scope into distinct signals in the deployed copy.
function ensureSignalRootElement(modeler, moddle, name) {
  const definitions = modeler.getDefinitions?.();
  if (!definitions) throw new Error("The BPMN definitions are not available.");

  const rootElements = definitions.get ? definitions.get("rootElements") : definitions.rootElements;

  const existing = (rootElements ?? []).find(
    (candidate) => candidate?.$type === "bpmn:Signal" && candidate.name === name
  );
  if (existing) return existing;

  const created = moddle.create("bpmn:Signal", {
    id: `Signal_${name.replace(/[^A-Za-z0-9_-]/g, "_")}`,
    name
  });
  rootElements.push(created);
  return created;
}

// #114. Error and escalation events are one shape with two codes. The code lives
// on a root <bpmn:error>/<bpmn:escalation> element and the event points at it by
// ref — the same indirection messages use — so the studio edits the code and
// maintains the root element behind it.
//
// Returns null for everything else, so the keys are ABSENT on other elements and
// the studio can route on presence, as the timer and message editors do.
function describeCodedEvent(businessObject) {
  if (!businessObject) return null;

  const definitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];

  const error = definitions.find((d) => d && d.$type === "bpmn:ErrorEventDefinition");
  const escalation = definitions.find((d) => d && d.$type === "bpmn:EscalationEventDefinition");
  if (!error && !escalation) return null;

  const kind = error ? "error" : "escalation";
  const ref = error ? error.errorRef : escalation.escalationRef;

  return {
    codedEventKind: kind,
    // bpmn-moddle resolves the ref to the root element when the diagram declares
    // it and leaves a raw id when it does not; both shapes are read.
    codedEventCode:
      (typeof ref === "string"
        ? ref
        : ref?.errorCode ?? ref?.escalationCode ?? ref?.name ?? ref?.id) ?? "",
    // Only a boundary event interrupts, and only an ESCALATION boundary may
    // choose: BPMN gives an error boundary no option, it always interrupts. So
    // the key is null on an error boundary, and the studio offers no switch
    // rather than one that cannot be honoured.
    codedEventInterrupting:
      businessObject.$type === "bpmn:BoundaryEvent" && kind === "escalation"
        ? businessObject.cancelActivity !== false
        : null
  };
}

// #114. Writes the code, creating or reusing the root element it refers to.
//
// A code with no root element behind it is the silent failure this story is
// about: the ref dangles, nothing matches, and the diagram looks correct.
export function updateCodedEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update this event.");
  }

  const element = elementRegistry.get(payload.id);
  const businessObject = element?.businessObject;
  if (!businessObject) {
    throw new Error(`Element '${payload.id}' is no longer available in the diagram.`);
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const isError = payload.kind === "error";
  const definition = eventDefinitions.find(
    (d) => d && d.$type === (isError ? "bpmn:ErrorEventDefinition" : "bpmn:EscalationEventDefinition")
  );
  if (!definition) {
    throw new Error(`Element '${payload.id}' does not carry a ${payload.kind} definition.`);
  }

  const code = normalizeOptionalString(payload.code);
  const root = code ? ensureCodedRootElement(modeler, moddle, isError, code) : undefined;

  // updateModdleProperties so the command stack records it — assigning straight
  // to the moddle object looks identical in the editor and is silently lost.
  modeling.updateModdleProperties(element, definition, isError ? { errorRef: root } : { escalationRef: root });

  const properties = { name: normalizeOptionalString(payload.name) };
  // Only an escalation boundary may be non-interrupting; an error boundary always
  // interrupts, so writing cancelActivity on one would suggest a choice that BPMN
  // does not offer.
  if (businessObject.$type === "bpmn:BoundaryEvent" && !isError && typeof payload.interrupting === "boolean") {
    properties.cancelActivity = payload.interrupting;
  }
  modeling.updateProperties(element, properties);
}

// One root element per code, reused. Creating a second <bpmn:error> for a code
// that already exists would leave two ids for one code, and a boundary pointing
// at the other one would never match.
function ensureCodedRootElement(modeler, moddle, isError, code) {
  const definitions = modeler.getDefinitions?.();
  if (!definitions) throw new Error("The BPMN definitions are not available.");

  const rootElements = definitions.get ? definitions.get("rootElements") : definitions.rootElements;
  const wantedType = isError ? "bpmn:Error" : "bpmn:Escalation";
  const codeField = isError ? "errorCode" : "escalationCode";

  const existing = (rootElements ?? []).find(
    (candidate) => candidate?.$type === wantedType && candidate[codeField] === code
  );
  if (existing) return existing;

  const created = moddle.create(wantedType, {
    id: `${isError ? "Error" : "Escalation"}_${code.replace(/[^A-Za-z0-9_-]/g, "_")}`,
    name: code,
    [codeField]: code
  });
  rootElements.push(created);
  return created;
}

// #112. Message elements split into two directions, and they need different
// configuration:
//
//   catch  — a message start / intermediate catch / boundary event, or a receive
//            task. Carries a correlation key: which process variable identifies
//            the instance a sender is addressing. A start event is the exception
//            (nothing is waiting yet, so nothing is correlated).
//   send   — an intermediate throw or end event carrying a message definition, or
//            a send task. Carries the workflow to address and the variable whose
//            value picks the instance over there.
//
// Returns null for everything else so the key is ABSENT rather than null on
// non-message elements — the studio routes on key presence, the same rule the
// timer and conditional editors rely on.
function describeMessageElement(businessObject) {
  if (!businessObject) return null;

  const type = businessObject.$type;
  const definitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const hasMessageDefinition = definitions.some(
    (definition) => definition && definition.$type === "bpmn:MessageEventDefinition"
  );

  let direction = null;
  if (type === "bpmn:ReceiveTask") {
    direction = "catch";
  } else if (type === "bpmn:SendTask") {
    direction = "send";
  } else if (hasMessageDefinition) {
    if (type === "bpmn:StartEvent") direction = "start";
    else if (type === "bpmn:IntermediateCatchEvent" || type === "bpmn:BoundaryEvent") direction = "catch";
    else if (type === "bpmn:IntermediateThrowEvent" || type === "bpmn:EndEvent") direction = "send";
  }

  if (!direction) return null;

  return {
    messageDirection: direction,
    messageCorrelationKey: readAutoNateFlowableAttr(businessObject, "autonateCorrelationKey") ?? "",
    messageTargetProcessKey: readAutoNateFlowableAttr(businessObject, "autonateTargetProcessKey") ?? "",
    // A send task has no message element to name it, so the name lives on the
    // element itself. Empty for everything else, whose name comes from messageRef.
    messageName:
      type === "bpmn:SendTask"
        ? readAutoNateFlowableAttr(businessObject, "autonateMessageName") ?? ""
        : messageNameOf(businessObject) ?? ""
  };
}

// The <bpmn:message> the element's definition points at. bpmn-moddle resolves
// messageRef to the element when the diagram declares it, and leaves a raw id
// when it does not, so both shapes are handled.
function messageNameOf(businessObject) {
  const definitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const definition = definitions.find(
    (d) => d && d.$type === "bpmn:MessageEventDefinition"
  );
  const ref = definition?.messageRef;
  if (!ref) return null;
  return typeof ref === "string" ? ref : ref.name ?? ref.id ?? null;
}

// Autonate-named attributes live under the FLOWABLE prefix, not an autonate one.
// #167 found out why: a diagram reliably declares xmlns:flowable, and bpmn-moddle
// silently discards an attribute whose prefix is undeclared.
function readAutoNateFlowableAttr(businessObject, name) {
  const direct = businessObject[name];
  if (typeof direct === "string" && direct.length > 0) return direct;
  const value = businessObject.$attrs?.[`flowable:${name}`];
  return typeof value === "string" && value.length > 0 ? value : null;
}

// #112. Writes the correlation configuration an author sets in the studio.
export function updateMessageElementProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update this message element.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject) {
    throw new Error(`Element '${payload.id}' is no longer available in the diagram.`);
  }

  const businessObject = element.businessObject;
  writeFlowableAttribute(
    businessObject, "autonateCorrelationKey", normalizeOptionalString(payload.correlationKey));
  writeFlowableAttribute(
    businessObject, "autonateTargetProcessKey", normalizeOptionalString(payload.targetProcessKey));
  if (businessObject.$type === "bpmn:SendTask") {
    writeFlowableAttribute(
      businessObject, "autonateMessageName", normalizeOptionalString(payload.messageName));
  }

  // Through modeling so the command stack records it and the dirty flag flips —
  // assigning to the businessObject alone looks identical in the editor and is
  // silently lost on save.
  modeling.updateProperties(element, { name: normalizeOptionalString(payload.name) });
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

// #158: the condition on a conditional event, in all three placements —
// intermediate catch, boundary, and the event-subprocess start (#162).
//
// Returns null for anything without a conditionalEventDefinition, so the key is
// ABSENT rather than null on other elements. That is load-bearing:
// onRequestConfigure routes on $type plus key presence, and an unconditional
// `conditionExpression` here would send every intermediate catch event to the
// conditional modal, including the timer ones.
function describeConditionalEvent(businessObject) {
  if (!businessObject) return null;

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const conditionalEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:ConditionalEventDefinition"
  );
  if (!conditionalEventDefinition) {
    return null;
  }

  const condition = conditionalEventDefinition.condition;
  return {
    conditionExpression: typeof condition?.body === "string" ? condition.body : null,
    // Only a boundary event interrupts. BPMN defaults cancelActivity to true when
    // the attribute is absent, so the default here has to match or a
    // non-interrupting event would read back as interrupting.
    cancelActivity:
      businessObject.$type === "bpmn:BoundaryEvent"
        ? businessObject.cancelActivity !== false
        : null
  };
}

// #157: a timer boundary event's time and whether it interrupts.
//
// Neither existing timer helper is reusable. describeTimerStartEvent returns null
// unless $type is bpmn:StartEvent and reads only timeCycle;
// describeTimerIntermediateCatchEvent is gated on bpmn:IntermediateCatchEvent and
// reads only duration and date. A boundary event needs all three kinds and is a
// third $type, so this is a third helper.
//
// The keys are deliberately NOT timerDuration/timerDate/timerCycleCron: those are
// what routes the other two editors, and onRequestConfigure routes on $type plus
// key presence.
function describeTimerBoundaryEvent(businessObject) {
  if (!businessObject || businessObject.$type !== "bpmn:BoundaryEvent") {
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

  const body = (expression) => (typeof expression?.body === "string" ? expression.body : null);

  return {
    boundaryTimerDuration: body(timerEventDefinition.timeDuration),
    boundaryTimerDate: body(timerEventDefinition.timeDate),
    boundaryTimerCycle: body(timerEventDefinition.timeCycle),
    // BPMN treats an absent cancelActivity as true, so the default here has to
    // match or a non-interrupting timer reads back as interrupting.
    cancelActivity: businessObject.cancelActivity !== false,
    attachedTo: typeof businessObject.attachedToRef?.id === "string" ? businessObject.attachedToRef.id : null
  };
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
  const type = element?.businessObject?.$type;
  // #218. A complex gateway carries a routing script, and it is edited through
  // the same panel — the fields are the same fields.
  const isGateway = type === "bpmn:ComplexGateway";
  if (!element?.businessObject || (type !== "bpmn:ScriptTask" && !isGateway)) {
    throw new Error(`Script task '${task.id}' is no longer available in the diagram.`);
  }

  const scriptFormat = task.scriptFormat === "python" ? "python" : "javascript";
  const script = typeof task.script === "string" ? task.script : "";

  if (isGateway) {
    // Name through modeling so the canvas relabels; everything else through
    // $attrs, because the modeller models none of it on this element and
    // silently drops what it cannot model.
    modeling.updateProperties(element, { name: normalizeOptionalString(task.name) });
    writeAutoNateAttribute(element.businessObject, "scriptFormat", scriptFormat);
    writeAutoNateAttribute(element.businessObject, "routeScript", script);
  } else {
    modeling.updateProperties(element, {
      name: normalizeOptionalString(task.name),
      // The author's language choice, stored in the standard BPMN attribute
      // rather than an Auton8-specific one (#154). Defaulted rather than trusted:
      // a task authored before Python support carries no value.
      scriptFormat,
      script,
      resultVariable: normalizeOptionalString(task.resultVariable)
    });
  }

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

// #158: writes the condition, and on a boundary event whether it interrupts.
//
// One function for all three placements. The $type assertion accepts the set BPMN
// allows a conditional event definition on, and the definition check is what makes
// the error useful — telling an author to drop the right element rather than
// reporting that something generic failed.
export function updateConditionalEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the conditional event.");
  }

  const element = elementRegistry.get(payload.id);
  const businessObject = element?.businessObject;
  const allowedTypes = ["bpmn:IntermediateCatchEvent", "bpmn:BoundaryEvent", "bpmn:StartEvent"];
  if (!businessObject || !allowedTypes.includes(businessObject.$type)) {
    throw new Error(`Conditional event '${payload.id}' is no longer available in the diagram.`);
  }

  const eventDefinitions = Array.isArray(businessObject.eventDefinitions)
    ? businessObject.eventDefinitions
    : [];
  const conditionalEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:ConditionalEventDefinition"
  );
  if (!conditionalEventDefinition) {
    throw new Error(
      `'${payload.id}' is not a conditional event — drop a conditional event from the palette instead.`
    );
  }

  const expression = normalizeOptionalString(payload.conditionExpression);

  // updateModdleProperties rather than assigning the property directly: it always
  // pushes a command onto the stack, so the studio's dirty flag flips and the edit
  // survives a reload. Assigning straight to the moddle object looks identical in
  // the editor and is silently lost.
  modeling.updateModdleProperties(element, conditionalEventDefinition, {
    condition: expression
      ? moddle.create("bpmn:FormalExpression", { body: expression })
      : undefined
  });

  const properties = { name: normalizeOptionalString(payload.name) };

  // Only a boundary event interrupts, and it is written explicitly in both
  // directions: BPMN treats an absent cancelActivity as true, so omitting it to
  // mean "interrupting" would make non-interrupting impossible to undo.
  if (businessObject.$type === "bpmn:BoundaryEvent" && typeof payload.cancelActivity === "boolean") {
    properties.cancelActivity = payload.cancelActivity;
  }

  modeling.updateProperties(element, properties);
}

// #157: writes the timer boundary's time and whether it interrupts.
export function updateTimerBoundaryEventProperties(modelerHandle, payload) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  const moddle = modeler?.get?.("moddle", false);
  if (!elementRegistry || !modeling || !moddle || !payload?.id) {
    throw new Error("The BPMN modeler is not ready to update the timer boundary event.");
  }

  const element = elementRegistry.get(payload.id);
  if (!element?.businessObject || element.businessObject.$type !== "bpmn:BoundaryEvent") {
    throw new Error(`Timer boundary event '${payload.id}' is no longer available in the diagram.`);
  }

  const eventDefinitions = Array.isArray(element.businessObject.eventDefinitions)
    ? element.businessObject.eventDefinitions
    : [];
  const timerEventDefinition = eventDefinitions.find(
    (definition) => definition && definition.$type === "bpmn:TimerEventDefinition"
  );
  if (!timerEventDefinition) {
    throw new Error(
      `Boundary event '${payload.id}' is not a timer boundary event — drop a timer boundary event from the palette instead.`
    );
  }

  const duration = normalizeOptionalString(payload.boundaryTimerDuration);
  const date = normalizeOptionalString(payload.boundaryTimerDate);
  const cycle = normalizeOptionalString(payload.boundaryTimerCycle);

  // Clear every kind before setting one. Flowable rejects a definition carrying
  // two, and a stale timeCycle beside a new timeDuration behaves unpredictably.
  //
  // updateModdleProperties rather than direct assignment: it always pushes a
  // command, so the studio's dirty flag flips and the edit survives a reload. The
  // older timer functions get away with assignment only because they also push a
  // name update.
  modeling.updateModdleProperties(element, timerEventDefinition, {
    timeDuration: duration ? moddle.create("bpmn:FormalExpression", { body: duration }) : undefined,
    timeDate: !duration && date ? moddle.create("bpmn:FormalExpression", { body: date }) : undefined,
    timeCycle: !duration && !date && cycle ? moddle.create("bpmn:FormalExpression", { body: cycle }) : undefined
  });

  modeling.updateProperties(element, {
    name: normalizeOptionalString(payload.name),
    // A standard BPMN attribute, so it goes through modeling rather than $attrs.
    cancelActivity: payload.cancelActivity !== false
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

  // #168. Written as an attribute for the same reason as the rest: bpmn-js here
  // loads no Flowable moddle extension, so modeling.updateProperties would
  // serialise it without the flowable: prefix and the deploy validator would
  // reject it. Removed rather than written "false" when off, so the XML says
  // what Flowable's default already means.
  writeFlowableAttribute(businessObject, "async", payload.retryPoint === true ? "true" : null);

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

// #167: manual tasks and plain tasks become user tasks, at design time.
//
// Neither waits. Verified against Flowable 8.0.0 by running both: the process passed
// straight through each one, creating no task and pausing nowhere.
// `ManualTaskActivityBehavior` is 488 bytes, and BPMN specifies a manual task as work
// done outside the system with no engine involvement; a plain `bpmn:task` is the
// same. So a diagram containing either reaches its end having done nothing a person
// was meant to do — the silent no-op this milestone exists to end.
//
// Converting at DESIGN time rather than at publish is what keeps the stored diagram,
// the deployed diagram and the execution view identical. There is nothing to map
// back, because nothing was rewritten on the way out.
const CONVERTED_TASK_TYPES = {
  "bpmn:ManualTask": "manual task",
  "bpmn:Task": "task"
};

function convertNonWaitingTasks(modeler) {
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const bpmnReplace = modeler?.get?.("bpmnReplace", false);
  if (!elementRegistry || !bpmnReplace) return [];

  // Snapshot first: replacing mutates the registry we would otherwise be iterating.
  const targets = elementRegistry
    .filter((element) => Boolean(CONVERTED_TASK_TYPES[element?.businessObject?.$type]))
    .slice();

  if (targets.length === 0) return [];

  // The marker below is written as `flowable:autonateConvertedFrom`, and bpmn-moddle
  // drops an attribute whose prefix the document never declares. Auton8's own starter
  // diagram declares xmlns:flowable, but a diagram authored in another modeller does
  // not — which is exactly the diagram this conversion exists for. Without this the
  // marker vanishes on save and the publish-time assignee check never fires on the
  // tasks that need it most.
  const definitions = modeler.get("canvas", false)?.getRootElement?.()?.businessObject?.$parent;
  if (definitions) {
    const attrs = definitions.$attrs ?? (definitions.$attrs = {});
    if (!attrs["xmlns:flowable"]) {
      attrs["xmlns:flowable"] = "http://flowable.org/bpmn";
    }
  }

  const converted = [];
  for (const element of targets) {
    const was = CONVERTED_TASK_TYPES[element.businessObject.$type];
    const name = typeof element.businessObject.name === "string" ? element.businessObject.name : null;

    const replacement = bpmnReplace.replaceElement(element, { type: "bpmn:UserTask" });

    // Marks what it came from, so publish validation can require an assignee on
    // exactly these and not on every user task in the product — an unassigned user
    // task is a first-class state elsewhere in Auton8.
    writeFlowableAttribute(replacement.businessObject, "autonateConvertedFrom", was);
    converted.push({ id: replacement.id, name, was });
  }

  return converted;
}

export async function loadXml(modelerHandle, xml) {
  modelerHandle.setSuppressDirtyEvents(true);
  let converted = [];
  try {
    await modelerHandle.modeler.importXML(xml);
    // #167. Same reason as the import inside createModeler: opening an existing
    // diagram is how a manual task authored elsewhere arrives.
    converted = convertNonWaitingTasks(modelerHandle.modeler);
    fitAndCenter(modelerHandle.modeler);
  } finally {
    modelerHandle.setSuppressDirtyEvents(false);
  }
  return converted;
}

// #167: what the last import converted, so the studio can say so once rather than
// once per element — the author did nothing on this path and a toast per task
// would be noise.
export function takeConvertedTasks() {
  const converted = convertedOnImport;
  convertedOnImport = [];
  return converted;
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

// #159/#163/#166. Writes what describeElementData reads.
//
// Everything goes through `modeling`, including the namespaced attributes.
// bpmn-js puts an unknown key straight into $attrs, so this stores the same
// thing writeAutoNateAttribute would — but it also pushes a COMMAND, and that is
// the whole point.
//
// The first version wrote $attrs directly and called
// `modeling.updateProperties(element, { name })` alongside it to push one. When
// the author had not changed the name that update was a no-op, no command
// reached the stack, the studio's dirty flag stayed false, and Save wrote
// nothing — the edit vanished with the panel reporting success. That is
// load-bearing fact 3 in the add-bpmn-element skill, walked into anyway.
export function updateElementDataProperties(modelerHandle, editor) {
  const modeler = modelerHandle?.modeler;
  const elementRegistry = modeler?.get?.("elementRegistry", false);
  const modeling = modeler?.get?.("modeling", false);
  if (!elementRegistry || !modeling || !editor?.id) {
    throw new Error("The BPMN modeler is not ready to update this element.");
  }

  const element = elementRegistry.get(editor.id);
  const businessObject = element?.businessObject;
  if (!businessObject) {
    throw new Error(`Element '${editor.id}' is no longer available in the diagram.`);
  }

  const name = normalizeOptionalString(editor.name);

  if (editor.kind === "adhoc") {
    modeling.updateProperties(element, {
      name,
      // `ordering` is standard BPMN on AdHocSubProcess; the condition is ours.
      ordering: editor.sequential ? "Sequential" : "Parallel",
      [`${AUTONATE_ATTR_PREFIX}completionCondition`]:
        normalizeOptionalString(editor.completionCondition)
    });
    return;
  }

  if (editor.kind === "dataObject") {
    modeling.updateProperties(element, {
      name,
      [`${AUTONATE_ATTR_PREFIX}dataType`]: normalizeOptionalString(editor.dataType)
    });
    return;
  }

  if (editor.kind === "multiInstance") {
    const loop = businessObject.loopCharacteristics;
    if (!loop) {
      throw new Error(
        `'${editor.id}' is no longer marked as multi-instance. Apply the marker from ` +
        "the element's replace menu first."
      );
    }

    // The marker's fields live on the NESTED moddle object, and the two update
    // helpers are not interchangeable there: `updateProperties` routes an unknown
    // prefixed key into $attrs, `updateModdleProperties` sets it as a plain
    // property that the writer never serialises. So the namespaced attributes are
    // written to $attrs directly...
    writeFlowableAttribute(loop, "collection", editor.collection);
    writeFlowableAttribute(loop, "elementVariable", editor.elementVariable);
    writeAutoNateAttribute(loop, "completionCondition", editor.completionCondition);
    writeAutoNateAttribute(loop, "loopCardinality", editor.cardinality);
    writeAutoNateAttribute(loop, "aggregateTarget", editor.aggregateTarget);
    writeAutoNateAttribute(loop, "aggregateSource", editor.aggregateSource);

    // ...and isSequential goes through updateModdleProperties, which is what
    // pushes the command. Without a command the studio never re-serialises and
    // the $attrs above are lost.
    modeling.updateProperties(element, { name });
    modeling.updateModdleProperties(element, loop, { isSequential: editor.sequential === true });
    return;
  }

  throw new Error(`Unknown element data editor kind '${editor.kind}'.`);
}
