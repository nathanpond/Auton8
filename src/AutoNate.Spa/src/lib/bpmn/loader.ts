/**
 * The bpmn-js modeler bundle (with create-append-anything baked in) is served
 * as a static asset from the SPA's /vendor/bpmn-js/ prefix. We inject the tag
 * lazily and cache the load promise so multiple concurrent mounts share it.
 */
const BPMN_JS_SRC = new URL(
  "vendor/bpmn-js/bpmn-modeler.development.js",
  document.baseURI
).toString();

const BPMN_JS_CSS = [
  new URL("vendor/bpmn-js/diagram-js.css", document.baseURI).toString(),
  new URL("vendor/bpmn-js/bpmn-js.css", document.baseURI).toString(),
  new URL("vendor/bpmn-js/bpmn-font/css/bpmn-embedded.css", document.baseURI).toString()
];

let loadPromise: Promise<typeof window.BpmnJS> | null = null;
let stylesInjected = false;

declare global {
  interface Window {
    BpmnJS: unknown;
  }
}

function injectStyles() {
  if (stylesInjected) {
    return;
  }

  for (const href of BPMN_JS_CSS) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    document.head.appendChild(link);
  }

  stylesInjected = true;
}

export function ensureBpmnJsLoaded(): Promise<typeof window.BpmnJS> {
  if (loadPromise) {
    return loadPromise;
  }

  injectStyles();

  if (typeof window.BpmnJS !== "undefined") {
    loadPromise = Promise.resolve(window.BpmnJS);
    return loadPromise;
  }

  loadPromise = new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[data-autonate-bpmn="true"]`);
    const handleLoad = () => {
      if (typeof window.BpmnJS === "undefined") {
        reject(new Error("bpmn-js bundle loaded but window.BpmnJS is not defined."));
        return;
      }
      resolve(window.BpmnJS);
    };

    if (existing) {
      existing.addEventListener("load", handleLoad);
      existing.addEventListener("error", () =>
        reject(new Error(`Failed to load bpmn-js bundle from ${BPMN_JS_SRC}.`)));
      return;
    }

    const script = document.createElement("script");
    script.src = BPMN_JS_SRC;
    script.async = true;
    script.defer = true;
    script.dataset.autonateBpmn = "true";
    script.addEventListener("load", handleLoad);
    script.addEventListener("error", () =>
      reject(new Error(`Failed to load bpmn-js bundle from ${BPMN_JS_SRC}.`)));
    document.head.appendChild(script);
  });

  return loadPromise;
}
