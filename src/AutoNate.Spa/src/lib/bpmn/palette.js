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

/**
 * Every element the manifest does NOT call supported, by its bpmn-js icon class.
 *
 * The palette override cannot reach bpmn-js's other two surfaces, so this is what
 * closes them (#264). Derived from the same catalog and the same manifest, so an
 * element promoted in `bpmn-support.json` leaves this set with no edit here.
 */
/** Every element the manifest calls supported, and every one it does not. */
const OFFERED = PALETTE_CATALOG.filter((entry) => studioStatusOf(entry) === "supported");
const WITHHELD = PALETTE_CATALOG.filter((entry) => studioStatusOf(entry) !== "supported");

/** The manifest's (localName, eventDefinition) key, in bpmn-js's own vocabulary. */
function targetKeyOf(entry) {
  return entry?.type ? `${entry.type}|${entry.eventDefinitionType ?? ""}` : null;
}

/**
 * Withheld elements by bpmn-js's own `target`, which is what actually identifies
 * one (#282).
 *
 * `className` is not an identity. bpmn-js gives the supported Intermediate Throw
 * (None) and the withheld bare Boundary Event the SAME
 * `bpmn-icon-intermediate-event-none`, because they are drawn with the same
 * glyph — so a className-keyed filter must either let the boundary event through
 * or wrongly withdraw the intermediate throw. It let it through: the bare
 * boundary event was placeable, published with zero errors, and Flowable refused
 * the deployment (`flowable-boundary-event-no-event-definition`).
 *
 * `target.type` plus `target.eventDefinitionType` is the pair bpmn-js replaces
 * WITH, and it is the same pair the manifest keys on, so this is the manifest's
 * own key expressed in the bundle's vocabulary rather than a glyph that happens
 * to correlate with it.
 */
export const WITHHELD_TARGET_KEYS = new Set(
  WITHHELD.map(targetKeyOf).filter(Boolean)
);

/**
 * Class names safe to deny on, for entries carrying no `target`.
 *
 * Header entries — `toggle-loop` and friends — apply a marker rather than
 * replacing the element, so they have no target and this is the only key
 * available for them.
 *
 * A class name a SUPPORTED element also uses is excluded, because denying it
 * would withdraw that element too. Computed rather than hand-maintained: an
 * element promoted in `bpmn-support.json` drops out of this set with no edit
 * here, which is the property the whole module rests on.
 */
const SUPPORTED_ICON_CLASSES = new Set(
  OFFERED.flatMap((entry) => [entry.className, ...(entry.menuClassNames ?? [])]).filter(Boolean)
);

export const WITHHELD_ICON_CLASSES = new Set(
  WITHHELD
    // #264. `className` alone was not enough: bpmn-js names some elements
    // differently in its own popups than the palette does, so
    // `bpmn-icon-business-rule-task` matched NOTHING in the bundle and Business
    // Rule Task -- which publish refuses -- stayed one click away on every menu.
    // `menuClassNames` carries the bundle's spelling beside ours, and
    // BpmnPaletteManifestTests asserts every key here really occurs in the
    // vendored bundle, because a deny key that matches nothing fails silently.
    .flatMap((entry) => [entry.className, ...(entry.menuClassNames ?? [])])
    .filter(Boolean)
    .filter((className) => !SUPPORTED_ICON_CLASSES.has(className))
);

/**
 * bpmn-js's own entry ids for withheld elements (#282).
 *
 * The Create and Append popups build their entries through `toActionEntry`,
 * which keeps label / className / description / group / search / rank / action
 * and **drops `target`**. So on those two surfaces there is nothing to judge by
 * except the glyph — and the glyph is shared. The id is what is left, and
 * bpmn-js builds it as `${idPrefix}-${actionName}`, hence the suffix match.
 */
export const WITHHELD_MENU_ENTRY_IDS = new Set(
  WITHHELD.flatMap((entry) => entry.menuEntryIds ?? [])
);

/** Would this popup entry place something the manifest does not support? */
export function isWithheldMenuEntry(entry, id) {
  const target = entry?.target;
  if (target?.type) {
    // The replace menu keeps `target`, so judge by what it would place — never
    // by its glyph, which bpmn-js reuses across unrelated elements.
    return WITHHELD_TARGET_KEYS.has(`${target.type}|${target.eventDefinitionType ?? ""}`);
  }

  if (typeof id === "string") {
    for (const menuId of WITHHELD_MENU_ENTRY_IDS) {
      if (id === menuId || id.endsWith(`-${menuId}`)) return true;
    }
  }

  return WITHHELD_ICON_CLASSES.has(entry?.className);
}

export const FILTERED_POPUP_MENUS = ["bpmn-replace", "bpmn-create", "bpmn-append"];

/**
 * Strips withheld elements from bpmn-js's Create-element popup and replace menu.
 *
 * #241 derived the palette and overrode `paletteProvider`. That was necessary and
 * not sufficient: the vendored bundle appends `create-append-anything` AFTER any
 * `additionalModules`, and it registers under a DIFFERENT name
 * (`createPaletteProvider`), so its "Create element" button survives the
 * override. Its popup offered Transaction, Cancel End, Business Rule Task,
 * Manual Task and generic Task — three of which publish then refuses. The stock
 * replace menu (the wrench on the context pad) offered the same, plus both link
 * events.
 *
 * So the withdrawn elements stayed one click away, and neither guard could see
 * it: the catalog test reads JSON, and the browser test compares palette
 * `data-action` values against catalog ids — the popup's own entries are neither.
 *
 * Implemented as popup-menu middleware rather than by replacing the providers.
 * `PopupMenu._getEntries` lets a provider return a FUNCTION, which receives every
 * entry accumulated so far and returns what survives — so this filters whatever
 * the bundle offers, including entries a future bpmn-js adds, instead of
 * reproducing its option tables and drifting from them.
 *
 * Registered at a priority BELOW the default so it runs last, after every
 * provider has contributed.
 */
export function createManifestMenuFilter() {
  const RUN_LAST = 500; // diagram-js default is 1000; higher runs first.

  function ManifestMenuFilter(popupMenu) {
    const strip = (entries) => Object.fromEntries(
      Object.entries(entries).filter(([id, entry]) => !isWithheldMenuEntry(entry, id))
    );

    const filter = {
      getPopupMenuEntries: () => strip,

      // #264, second pass. The HEADER row is a separate reduce.
      //
      // `PopupMenu._getEntries` and `_getHeaderEntries` walk the providers
      // independently and call different hooks, so implementing only the first
      // left the replace menu's header buttons untouched — and one of them,
      // `toggle-loop`, applies `bpmn:StandardLoopCharacteristics`, whose manifest
      // row is `withdrawn` / `cannot-execute`. Publish refuses it. An author
      // could reach it in two clicks: wrench, Loop, save.
      getPopupMenuHeaderEntries: () => strip
    };

    for (const menu of FILTERED_POPUP_MENUS) {
      popupMenu.registerProvider(menu, RUN_LAST, filter);
    }
  }

  ManifestMenuFilter.$inject = ["popupMenu"];

  return {
    __init__: ["autonateManifestMenuFilter"],
    autonateManifestMenuFilter: ["type", ManifestMenuFilter]
  };
}
