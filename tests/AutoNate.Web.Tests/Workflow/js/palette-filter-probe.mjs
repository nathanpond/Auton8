// Exercises Auton8's REAL popup-menu filter (#392).
//
// Every earlier palette guard read JSON or parsed palette.js as text, so each
// one pinned a proxy for "a withdrawn element is not one click away" rather than
// the thing itself. #392 is what that costs: deleting the entire filter left
// 32/32 green, with Pool, Transaction, Cancel End, Business Rule Task and
// toggle-loop back on Create, Append and Replace.
//
// This imports the module and runs `createManifestMenuFilter`'s own `strip`
// over the entry shapes bpmn-js actually produces. It cannot be satisfied by a
// comment, by building a deny set nobody consults, or by a read that is never
// used -- only by the filter removing the entries.
//
// Argument: the path to a rewritten copy of palette.js whose "@shared/*" imports
// have been turned into file URLs (the C# side does that; node has no Vite).
const [, , paletteUrl] = process.argv;
const { createManifestMenuFilter, PALETTE_CATALOG, studioStatusOf } = await import(paletteUrl);

const withheld = PALETTE_CATALOG.filter((e) => studioStatusOf(e) !== "supported");

// A class name a SUPPORTED element also uses is deliberately NOT a deny key --
// denying it would withdraw that element too. That subtraction is the module's
// stated design, so this harness must not demand it.
const SUPPORTED_CLASSES = new Set(
  PALETTE_CATALOG.filter((e) => studioStatusOf(e) === "supported")
    .flatMap((e) => [e.className, ...(e.menuClassNames ?? [])])
    .filter(Boolean)
);

// The shapes bpmn-js hands the filter. Replace keeps `target`; Create and Append
// build the id as `${idPrefix}-${actionName}` and DROP `target`, which is why
// `menuEntryIds` exists at all (#282).
function shapesFor(e) {
  const out = [];
  out.push({
    surface: "replace",
    id: `replace-${e.id}`,
    entry: {
      className: e.className,
      target: { type: e.type, eventDefinitionType: e.eventDefinitionType ?? "" }
    }
  });
  for (const mid of e.menuEntryIds ?? []) {
    out.push({ surface: "create", id: `bpmn-create-append-${mid}`, entry: { className: e.className } });
    out.push({ surface: "append", id: `append-${mid}`, entry: { className: e.className } });
  }
  if (e.className && !SUPPORTED_CLASSES.has(e.className)) {
    out.push({ surface: "className", id: `cls-${e.id}`, entry: { className: e.className } });
  }
  for (const cn of e.menuClassNames ?? []) {
    out.push({ surface: "menuClass", id: `mc-${e.id}`, entry: { className: cn } });
  }
  return out;
}

const registered = [];
const mod = createManifestMenuFilter();
const Factory = mod[mod.__init__[0]][1];
Factory({ registerProvider: (menu, priority, provider) => registered.push({ menu, provider }) });

const survivors = [];
const headerSurvivors = [];
for (const { menu, provider } of registered) {
  const strip = provider.getPopupMenuEntries();
  const stripHeader = provider.getPopupMenuHeaderEntries();
  for (const e of withheld) {
    for (const s of shapesFor(e)) {
      if (Object.keys(strip({ [s.id]: s.entry })).length > 0) {
        survivors.push(`${menu}: ${e.id} [${s.surface}] id=${s.id}`);
      }
      if (Object.keys(stripHeader({ [s.id]: s.entry })).length > 0) {
        headerSurvivors.push(`${menu}: ${e.id} [${s.surface}] id=${s.id}`);
      }
    }
  }
}

console.log(JSON.stringify({
  menus: registered.map((r) => r.menu),
  withheldRows: withheld.length,
  shapesTested: withheld.reduce((n, e) => n + shapesFor(e).length, 0),
  // A withheld row with no shape at all is invisible to the filter by
  // construction -- a different failure from surviving it, and worth its own name.
  unreachable: withheld.filter((e) => shapesFor(e).length === 0).map((e) => e.id),
  survivors,
  headerSurvivors
}));
