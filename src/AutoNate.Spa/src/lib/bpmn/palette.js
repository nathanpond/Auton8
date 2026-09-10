import catalog from "@shared/bpmn-palette.json";
import manifest from "@shared/bpmn-support.json";

/**
 * The studio palette, derived from the BPMN support manifest (#241).
 *
 * #107's AC1 promised the palette, the types modal and publish validation would
 * derive from one source "with a test that fails if any consumer disagrees". The
 * modal and the validation did. The palette did not - and worse, the 51-entry
 * array that was supposed to be it (`BPMN_MENU_ENTRIES`) was never referenced by
 * anything, so what authors actually saw was bpmn-js's stock palette: every
 * element the library ships, manifest unconsulted.
 *
 * The result was the founding complaint inverted. The palette offered Cancel End,
 * Transaction and Business Rule Task, each of which publish then refuses, and it
 * had no entry at all for the ad-hoc sub-process #163 shipped.
 *
 * So membership is not this module's decision and not the catalog's either: an
 * entry is offered when its manifest row says `studio: "supported"`. Withdrawing
 * an element in `bpmn-support.json` removes it from the palette with no edit
 * here, which is the property that was missing.
 */

const MANIFEST_BY_KEY = new Map(
  manifest.elements.map((element) => [
    manifestKey(element.localName, element.eventDefinition),
    element
  ])
);

function manifestKey(localName, eventDefinition) {
  return `${localName} ${eventDefinition ?? ""}`;
}

/** Every element an author could place, offered or not. */
export const PALETTE_CATALOG = catalog.entries;

export const PALETTE_GROUP_ORDER = catalog.groupOrder;

export function studioStatusOf(entry) {
  return MANIFEST_BY_KEY.get(manifestKey(entry.localName, entry.eventDefinition))?.studio ?? null;
}

/**
 * The entries the palette actually offers.
 *
 * An entry naming a manifest row that does not exist is dropped rather than
 * offered on a guess - `BpmnPaletteManifestTests` fails on it, so it cannot
 * reach a build silently.
 */
export const OFFERED_PALETTE_ENTRIES = PALETTE_CATALOG.filter(
  (entry) => studioStatusOf(entry) === "supported"
);

/**
 * A bpmn-js palette provider built from the entries above.
 *
 * Registering this as `paletteProvider` overrides bpmn-js's own - didi resolves
 * the last registration of a name, and `additionalModules` are applied after the
 * defaults. That override is the point: adding a provider alongside the stock one
 * only ever *adds* entries, and every defect in #241 was something the stock
 * palette offered that we needed gone.
 */
export function createManifestPaletteProvider() {
  function ManifestPaletteProvider(
    palette,
    create,
    elementFactory,
    spaceTool,
    lassoTool,
    handTool,
    globalConnect,
    translate
  ) {
    this._create = create;
    this._elementFactory = elementFactory;
    this._spaceTool = spaceTool;
    this._lassoTool = lassoTool;
    this._handTool = handTool;
    this._globalConnect = globalConnect;
    this._translate = translate;
    palette.registerProvider(this);
  }

  ManifestPaletteProvider.$inject = [
    "palette",
    "create",
    "elementFactory",
    "spaceTool",
    "lassoTool",
    "handTool",
    "globalConnect",
    "translate"
  ];

  ManifestPaletteProvider.prototype.getPaletteEntries = function () {
    const create = this._create;
    const elementFactory = this._elementFactory;
    const entries = {};

    // The tools are not BPMN elements and so are not in the manifest; they are
    // how the canvas is navigated, and are always present.
    entries["hand-tool"] = {
      group: "tools",
      className: "bpmn-icon-hand-tool",
      title: this._translate("Activate the hand tool"),
      action: { click: (event) => this._handTool.activateHand(event) }
    };
    entries["lasso-tool"] = {
      group: "tools",
      className: "bpmn-icon-lasso-tool",
      title: this._translate("Activate the lasso tool"),
      action: { click: (event) => this._lassoTool.activateSelection(event) }
    };
    entries["space-tool"] = {
      group: "tools",
      className: "bpmn-icon-space-tool",
      title: this._translate("Activate the create/remove space tool"),
      action: { click: (event) => this._spaceTool.activateSelection(event) }
    };
    entries["global-connect-tool"] = {
      group: "tools",
      className: "bpmn-icon-connection-multi",
      title: this._translate("Activate the global connect tool"),
      action: { click: (event) => this._globalConnect.start(event) }
    };
    entries["tool-separator"] = { group: "tools", separator: true };

    let previousGroup = null;
    for (const entry of OFFERED_PALETTE_ENTRIES) {
      if (previousGroup !== null && entry.group !== previousGroup) {
        entries[`separator-${entry.group}`] = { group: entry.group, separator: true };
      }
      previousGroup = entry.group;

      const start = (event) => {
        const shape = entry.createFactory === "participant"
          ? elementFactory.createParticipantShape()
          : elementFactory.createShape({
              type: entry.type,
              ...(entry.eventDefinitionType
                ? { eventDefinitionType: entry.eventDefinitionType }
                : {}),
              ...(entry.properties ?? {})
            });
        create.start(event, shape);
      };

      entries[entry.id] = {
        group: entry.group,
        className: entry.className,
        title: entry.description ?? entry.label,
        action: { dragstart: start, click: start }
      };
    }

    return entries;
  };

  return {
    __init__: ["paletteProvider"],
    paletteProvider: ["type", ManifestPaletteProvider]
  };
}
