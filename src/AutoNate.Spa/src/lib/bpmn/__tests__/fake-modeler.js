import { BpmnModdle } from "bpmn-moddle";

/**
 * A modeler the studio's own update functions can drive (#323, #411).
 *
 * `workflow.js` imports only `./palette` at module scope; everything else it
 * needs arrives through `modelerHandle.modeler.get(...)`. So covering the
 * authoring layer needs a stand-in for a few services, not the whole editor.
 *
 * **It has no routing rule of its own, and that is the fix.** The first version
 * asserted "a namespaced key goes to `$attrs`, a bare key to a direct field",
 * and #411 measured that against the shipped library: bpmn-js routes by whether
 * a **moddle descriptor declares the property**, and the colon is irrelevant. A
 * bare UNDECLARED key -- `resultVariable` on a `bpmn:ScriptTask`, which the
 * studio writes -- lands in `$attrs`, where the old fake put it in a direct
 * field. `null` is STORED, where the old fake deleted it.
 *
 * Both halves of that were wrong, and `fake-modeler.test.js` certified them. So
 * business objects are now created by the **real `bpmn-moddle`** and properties
 * applied through its own `set`, which is what bpmn-js calls. A fake that asks
 * the real library how it routes cannot drift from it.
 */
const moddle = new BpmnModdle();

/**
 * A real moddle element of any type — for the NESTED ones too (#417).
 *
 * Event definitions and loop characteristics used to be written as object
 * literals here, which had no `set` and so took the fake's hand-written
 * fallback: the exact rule #411 was filed for, on the path most of these tests
 * run. Built through moddle, they route the way the studio's will.
 */
export function moddleElement(type, fields = {}) {
  const element = moddle.create(type, {});
  for (const [key, value] of Object.entries(fields)) element.set(key, value);
  element.$attrs ??= {};
  return element;
}

/** A real moddle element, so routing is the library's answer rather than ours. */
export function businessObject(type, id, fields = {}) {
  const element = moddle.create(type, { id });
  for (const [key, value] of Object.entries(fields)) {
    element.set(key, value);
  }
  element.$attrs ??= {};
  return element;
}

export function fakeModeler(elements, options = {}) {
  const byId = new Map(elements.map((e) => [e.businessObject.id, e]));
  const commands = [];

  // The definitions root. `ensureSignalRootElement` reaches for it through
  // `modeler.getDefinitions()` and pushes new <bpmn:Signal> roots onto it, so a
  // fake without one fails in the writer before the code under test is reached.
  const definitions = {
    $type: "bpmn:Definitions",
    rootElements: options.rootElements ?? [],
    get(name) {
      return this[name];
    }
  };

  // Straight to moddle's own `set`, which is what bpmn-js's updateProperties
  // ends up calling. Whether a key lands in `$attrs` or in a direct field is the
  // library's decision, not ours -- #411 is what happens when we make it.
  //
  // #417: there is no fallback any more. The first version kept a hand-written
  // one for targets with no `.set`, and since every nested element in these
  // tests was a plain object literal, **18 of 92 tests took it** -- so the rule
  // #411 removed was still the one most of the suite ran on. Now a target that
  // is not a moddle element is a loud failure rather than a second rule.
  const applyKey = (target, key, value) => {
    if (typeof target?.set !== "function") {
      throw new Error(
        `fake-modeler: ${key} was written to a plain object, not a moddle element. `
        + "Build it with moddleElement(type, fields) so bpmn-js's own routing decides "
        + "where it lands -- a second routing rule here is #411 (#417).");
    }

    target.set(key, value);
    target.$attrs ??= {};
  };

  const services = {
    // `moddle.create` returns a plain object carrying its own `$type` and the
    // `$attrs` bag. That is what bpmn-moddle hands back for a type it knows,
    // and it is all the writers here touch.
    moddle: {
      create: (type, properties = {}) => {
        // Real moddle for types it knows; a plain object for the rest, since the
        // studio also creates extension elements moddle has no descriptor for.
        try {
          const element = moddle.create(type, properties);
          element.$attrs ??= {};
          return element;
        } catch {
          return { $type: type, $attrs: {}, ...properties };
        }
      },
      // `createAny` is how the signal scope rides on the event: an extension
      // ELEMENT, because two attribute routes failed on events parsed without
      // one (see the comment at the write site).
      createAny: (type, ns, properties = {}) => ({ $type: type, $ns: ns, $attrs: {}, ...properties })
    },
    elementRegistry: {
      get: (id) => byId.get(id) ?? null,
      getAll: () => [...byId.values()]
    },
    canvas: {
      getRootElement: () => ({ businessObject: { $parent: definitions } })
    },
    modeling: {
      updateProperties(element, properties) {
        commands.push({ kind: "updateProperties", id: element.businessObject.id });
        for (const [key, value] of Object.entries(properties)) {
          applyKey(element.businessObject, key, value);
        }
      },
      updateModdleProperties(element, moddleElement, properties) {
        commands.push({ kind: "updateModdleProperties", id: element.businessObject.id });
        for (const [key, value] of Object.entries(properties)) {
          applyKey(moddleElement, key, value);
        }
      }
    }
  };

  return {
    commands,
    definitions,
    handle: {
      modeler: {
        get: (name) => services[name] ?? null,
        getDefinitions: () => definitions
      }
    }
  };
}

/** An element wrapping a business object, as `elementRegistry.get` returns one. */
export function element(bo) {
  return { id: bo.id, type: bo.$type, businessObject: bo };
}
