import { describe, it, expect } from "vitest";
import {
  isWithheldMenuEntry,
  PALETTE_CATALOG,
  studioStatusOf,
  WITHHELD_MENU_ENTRY_IDS
} from "../palette.js";

/**
 * All three deny paths (#323, #312, #264, #282).
 *
 * #312 showed an entry escaping all three at once through a missing field, and
 * #392 showed the C#-side guards pinning how the deny sets are BUILT rather
 * than that they are consulted. This drives the real predicate directly, in the
 * runtime the studio uses.
 */
const withheld = PALETTE_CATALOG.filter((e) => studioStatusOf(e) !== "supported");
const supported = PALETTE_CATALOG.filter((e) => studioStatusOf(e) === "supported");

describe("the three deny paths each work on their own", () => {
  it("denies by target key, which the replace menu keeps", () => {
    const entry = withheld.find((e) => e.type);
    expect(entry, "no withheld catalog entry carries a target type").toBeTruthy();

    expect(
      isWithheldMenuEntry(
        { target: { type: entry.type, eventDefinitionType: entry.eventDefinitionType ?? "" } },
        "any-id"
      )
    ).toBe(true);
  });

  it("denies by menu entry id, which is all Create and Append leave behind", () => {
    const id = [...WITHHELD_MENU_ENTRY_IDS][0];
    expect(id, "no withheld entry declares a menu entry id").toBeTruthy();

    // bpmn-js builds `${idPrefix}-${actionName}`, hence the suffix match.
    expect(isWithheldMenuEntry({}, `create-${id}`)).toBe(true);
    expect(isWithheldMenuEntry({}, id)).toBe(true);
  });

  it("denies by class name for an entry with no target and no entry id", () => {
    const supportedClasses = new Set(
      supported.flatMap((e) => [e.className, ...(e.menuClassNames ?? [])]).filter(Boolean)
    );
    const entry = withheld.find((e) => e.className && !supportedClasses.has(e.className));
    expect(entry, "no withheld entry has a class name of its own").toBeTruthy();

    expect(isWithheldMenuEntry({ className: entry.className }, "unrelated-id")).toBe(true);
  });
});

describe("the filter does not withdraw what the manifest supports", () => {
  it("lets a supported element through on every path", () => {
    const entry = supported.find((e) => e.type && e.className);
    expect(entry).toBeTruthy();

    expect(
      isWithheldMenuEntry(
        {
          className: entry.className,
          target: { type: entry.type, eventDefinitionType: entry.eventDefinitionType ?? "" }
        },
        `create-${entry.id}`
      )
    ).toBe(false);
  });

  it("judges by target rather than glyph when a target is present", () => {
    // bpmn-js reuses glyphs across unrelated elements, so a className shared
    // with a withheld element must NOT withdraw a supported one (#264).
    const supportedWithTarget = supported.find((e) => e.type);
    const shared = withheld.find((e) => e.className === supportedWithTarget?.className);

    if (!shared) return; // no shared glyph in the catalog today; nothing to prove

    expect(
      isWithheldMenuEntry(
        {
          className: shared.className,
          target: {
            type: supportedWithTarget.type,
            eventDefinitionType: supportedWithTarget.eventDefinitionType ?? ""
          }
        },
        "x"
      )
    ).toBe(false);
  });

  it("does not deny an entry with no target, no id and no class", () => {
    expect(isWithheldMenuEntry({}, undefined)).toBe(false);
  });
});
