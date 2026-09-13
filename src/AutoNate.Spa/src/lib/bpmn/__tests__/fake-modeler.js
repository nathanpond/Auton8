/**
 * A modeler the studio's own update functions can drive (#323).
 *
 * `workflow.js` imports only `./palette` at module scope; everything else it
 * needs arrives through `modelerHandle.modeler.get(...)`. So covering the
 * authoring layer needs a stand-in for two services, not bpmn-js.
 *
 * **The fidelity assumption, stated because everything here rests on it.**
 * bpmn-js routes a property whose key carries a namespace prefix and which no
 * moddle descriptor declares into `businessObject.$attrs`, and sets a bare
 * declared key as a direct field. That is exactly the split
 * `readAutoNateAttribute` (reads `$attrs["autonate:name"]`) and
 * `readFlowableString` (reads the direct field, then `$attrs["flowable:name"]`)
 * expect on the way back out. `TheFakeRoutesKeysTheWayBpmnJsDoes` in
 * `fake-modeler.test.js` pins the rule, because a fake that drifts from bpmn-js
 * would make every round-trip test pass while the studio stayed broken -- which
 * is the failure this milestone exists to end, wearing a new hat.
 */

/** A moddle-ish business object: direct fields plus the `$attrs` bag. */
export function businessObject(type, id, fields = {}) {
  return { $type: type, id, $attrs: {}, ...fields };
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

  const applyKey = (target, key, value) => {
    if (key.includes(":")) {
      // A namespaced key no descriptor declares -- bpmn-js parks it in $attrs.
      target.$attrs ??= {};
      if (value === undefined || value === null) delete target.$attrs[key];
      else target.$attrs[key] = value;
      return;
    }
    if (value === undefined) delete target[key];
    else target[key] = value;
  };

  const services = {
    // `moddle.create` returns a plain object carrying its own `$type` and the
    // `$attrs` bag. That is what bpmn-moddle hands back for a type it knows,
    // and it is all the writers here touch.
    moddle: {
      create: (type, properties = {}) => ({ $type: type, $attrs: {}, ...properties }),
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
