import manifest from "@shared/bpmn-support.json";

/**
 * The BPMN support manifest, as the SPA sees it (#107).
 *
 * `../../../../shared/bpmn-support.json` is the same file `AutoNate.Web.csproj`
 * embeds as `AutoNate.Web.bpmn-support.json`, so the studio and publish validation
 * read the same bytes rather than two hand-kept copies. That is the whole point:
 * before this, the types modal, the palette and the runtime deny-lists were three
 * parallel lists, and #103 measured them disagreeing on 47 of 68 elements.
 *
 * Two axes, deliberately independent:
 *
 * - `studio` — what we advertise. Drives this panel and nothing else.
 * - `engine` — what Flowable actually does with it, proven by deployment in #103.
 *   Drives publish validation and nothing else.
 *
 * An element can be runnable by the engine while the studio has not wired an
 * editor for it (most of the "coming soon" list), which is why one flag cannot
 * carry both.
 */
export type BpmnStudioStatus = "supported" | "coming-soon" | "withdrawn";

export type BpmnEngineStatus = "executes" | "annotation" | "cannot-execute";

export type BpmnSupportElement = {
  name: string;
  category: string;
  studio: BpmnStudioStatus;
  engine: BpmnEngineStatus;
  localName: string;
  eventDefinition: string | null;
  reason: string | null;
  evidence: string | null;
};

export type BpmnSupportGroup = {
  category: string;
  items: BpmnSupportElement[];
};

export const FLOWABLE_VERSION: string = manifest.flowableVersion;

export const BPMN_SUPPORT_ELEMENTS: readonly BpmnSupportElement[] =
  manifest.elements as BpmnSupportElement[];

/**
 * Executable elements the studio offers today.
 *
 * Annotations are excluded even though they are equally authorable — they are
 * presented separately, because listing "Text Annotation" beside "User Task"
 * under one "supported" heading is what let a reader believe a group box was a
 * step that runs.
 */
export const EXECUTABLE_SUPPORTED_ELEMENTS = BPMN_SUPPORT_ELEMENTS.filter(
  (element) => element.studio === "supported" && element.engine !== "annotation"
);

export const COMING_SOON_ELEMENTS = BPMN_SUPPORT_ELEMENTS.filter(
  (element) => element.studio === "coming-soon"
);

/** Diagram-only artifacts. Not gaps — BPMN defines them as carrying no behaviour. */
export const ANNOTATION_ELEMENTS = BPMN_SUPPORT_ELEMENTS.filter(
  (element) => element.engine === "annotation"
);

/**
 * Grouped for display, preserving manifest order within and across categories so
 * the panel's ordering is a property of the manifest rather than of this file.
 */
export function groupByCategory(
  elements: readonly BpmnSupportElement[]
): BpmnSupportGroup[] {
  const groups: BpmnSupportGroup[] = [];
  for (const element of elements) {
    const existing = groups.find((group) => group.category === element.category);
    if (existing) {
      existing.items.push(element);
    } else {
      groups.push({ category: element.category, items: [element] });
    }
  }
  return groups;
}
