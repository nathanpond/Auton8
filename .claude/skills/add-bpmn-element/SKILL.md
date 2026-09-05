---
name: add-bpmn-element
description: Use when adding support for a BPMN element or event definition in the workflow studio — a new event type, task type, gateway, subprocess, activity marker or data element. Walks the manifest → serialisation → backend → validation → UI → tests path so the element is authorable, deployable, executable and proven, instead of drawing fine and silently doing nothing. Use for any story in M4 or M5 that adds a node type.
---

# Adding a BPMN element

M4 has 18 open stories walking this same path. Skipping a step produces a specific,
recognisable half-wired failure:

| Step skipped | Symptom |
|---|---|
| `describeBusinessObject` | Backend field is permanently null; property panel opens empty and "loses" settings on reselect |
| Any of the 4 type mirrors | Field set in one layer, invisible in the next |
| Key merged unconditionally | Elements sharing that `$type` misroute to the wrong modal, silently |
| `update*Properties` | Editor shows values, Apply appears to work, edit is lost on reload |
| `Apply*Snapshot` | Field reaches the backend, never reaches the XML |
| A carve-out site | Element works but still warns "deploys but does nothing" |
| Validation | Misconfiguration fails at runtime, on whoever ran the process |
| Fixture | #103's inventory has a verdict with no evidence behind it |

**The silent no-op is the failure this epic exists to end.** An element that deploys
and does nothing is worse than one that refuses, because nobody finds out until a
process is running.

## Before you start

Every path and symbol below is a **claim that may have rotted**. Run
`scripts/verify-symbols.sh` first — it checks every path and symbol this skill cites,
plus the three load-bearing claims most likely to go stale, and exits non-zero when
one has. When a code change invalidates a step here, **fix this skill in the
same commit** — "later" does not happen. #174 is the scheduled consolidation pass;
#157, #158, #160 and #161 each correct this skill in their own PR.

Read the element's story and #103's inventory row first. If the inventory says
Flowable has no behaviour for the element, stop — that is #155/#165 territory and
needs a custom `ActivityBehavior`, not this skill.

## The load-bearing facts

Read these before the steps. Each one is a trap that looks fine until it doesn't.

**1. The modeller is stock, vendored bpmn-js.** `createModeler`
(`src/AutoNate.Spa/src/lib/bpmn/workflow.js`) does `new window.BpmnJS({ container })`
— no `additionalModules`, no custom palette provider. The bundle is a static asset at
`src/AutoNate.Spa/public/vendor/bpmn-js/bpmn-modeler.development.js`, not an npm
import. **`BPMN_MENU_ENTRIES` and `MENU_GROUP_ORDER` at the top of `workflow.js` are
dead code** — each has exactly one occurrence, its own declaration. Editing them
changes nothing on screen and nothing in any test. Authoring affordances come from
bpmn-js's own palette, context pad and replace menu; grep the vendored bundle to find
what it already offers for your element.

**2. Custom attributes go in the `flowable:` namespace, not `autonate`.**
`writeFlowableAttribute` writes `flowable:<name>` into `businessObject.$attrs`.
`http://autonate.dev/workflows` is the **`targetNamespace` on `<bpmn:definitions>`
only** — never an attribute namespace. Even Auton8-proprietary properties use
`flowable:` (e.g. `writeFlowableAttribute(businessObject, "autonateServiceKind", …)`),
because bpmn-js loads no Flowable moddle extension here, so raw prefixed attributes
in `$attrs` are the only round-trip-safe shape.

**3. Editor routing is `$type` *plus* key presence.** `onRequestConfigure` routes on
`selection.type === "bpmn:StartEvent" && ("timerCycleCron" in selection || …)` — both
guards, at every branch. So your describe helper must gate on `$type` **and** omit the
keys for elements that don't have them; the two guards are independent and you need
both.

Merging a key unconditionally onto the base description object misroutes every element
whose `$type` also matches that branch — e.g. an unconditional `timerDuration` sends
message and signal intermediate catch events to the timer modal. Contained to one
element type, not catastrophic, but silent and confusing.

**4. `/publish` does not validate.** `WorkflowBpmnXml.ValidateProcess` has exactly one
call site: `POST /api/workflows/prepare` (`WorkflowEndpoints.cs`). `POST
/api/workflows/{id}/publish` goes straight to `DeployProcessAsync`. Validation blocks
the **SPA flow**, because `prepareAndStore` declines when `errors.length > 0` — it
does not block the API. A test that posts an invalid diagram to `/publish` and expects
a 4xx **passes with a 200 deploy**, which is the exact silent-no-op-shaped test
failure this skill exists to prevent. Write endpoint-level validation tests against
`/prepare`.

## Steps in order

### 1. Move it in the support manifest — three sites, not one

Until #107 lands, the truth is split and can disagree:

- `SUPPORTED_BPMN_TYPES` / `COMING_SOON_BPMN_TYPES` — `src/AutoNate.Spa/src/pages/workflow/WorkflowStudio.tsx`. Note the two lists use **different category taxonomies** (`Events` vs `Start Events`/`Intermediate Events`/`Boundary Events`/`End Events`). #103's test plan asserts the **combined count is 68**, so any move must be strictly 1-for-1.
- `BuildUnsupportedRuntimeWarnings` — `src/AutoNate.Web/Services/Workflow/WorkflowBpmnXml.cs`. **This method has two independent blocks** and your element may need a carve-out in both:
  1. the `UnsupportedRuntime*` element-name sets, where `intermediateCatchEvent` already shows the carve-out pattern for a single event-definition flavour;
  2. a separate `localName.EndsWith("EventDefinition")` block further down, whose carve-outs whitelist by **definition type *and* parent element type**.

  Missing the second is the standard trap: the element works, and still warns.

⚠️ These feed **`warnings`**, not `errors` — which is why unsupported elements deploy
today. #107 owns changing that; don't do it as a side effect.

### 2. Authoring affordance — usually no code

See load-bearing fact 1. Check the vendored bundle for what bpmn-js already offers
(replace menu entries carry `eventDefinitionAttrs`). If it is already reachable
through the wrench or context pad, **say so in the story** rather than adding a
palette entry that does nothing. Only if it genuinely isn't offered do you need a
custom module — and that means wiring `additionalModules` into `createModeler`, which
is a larger change than this skill covers.

### 3. Read it back — and all four type mirrors

`describeBusinessObject()` in `workflow.js` is the source of truth: `getElementSnapshots`
is literally this function mapped over the element registry, so **the describe output
*is* the snapshot wire format**. Add a field to the C# record without adding it here
and it is permanently null.

Add a `describe<Element>(businessObject)` helper (model: `describeTimerIntermediateCatchEvent`)
and merge its fields **conditionally**, per load-bearing fact 3 — the key must be
*absent*, not null, for elements that don't have it.

Then mirror the field in all four places:

1. `describeBusinessObject` — `workflow.js`
2. `ElementSelection` — `WorkflowStudio.tsx`
3. TS `WorkflowElementSnapshot` — `src/AutoNate.Spa/src/api/workflows.ts`
4. C# `WorkflowElementSnapshot` — `src/AutoNate.Web/Services/Workflow/WorkflowElementSnapshot.cs` (positional record; **append only**)

### 4. Write it back — `update<Element>Properties`

Exported from `workflow.js`. Model: `updateTimerIntermediateCatchEventProperties`.

1. Pull `elementRegistry`, `modeling`, `moddle` from the handle; throw if any is missing or `payload?.id` is absent.
2. `elementRegistry.get(payload.id)`, then **assert `$type`** — and for an event-definition variant, assert the definition is present too. The error should tell the author to drop the right element instead.
3. **Clear the alternatives.** The timer functions set unused kinds to `undefined`; a stale `timeCycle` beside a new `timeDuration` is a valid-looking diagram that behaves unpredictably.
4. **Make sure a command reaches the command stack.** The existing timer functions assign moddle properties directly (`timerEventDefinition.timeCycle = expression`) and call `modeling.updateProperties(element, { name })` only for the name. That works *because* the name update pushes a command. If your editor changes only a definition field and not the name, `commandStack.changed` never fires, the studio's dirty flag stays false, and **the edit is silently lost on reload**. Prefer `modeling.updateModdleProperties(element, definition, { … })`, which always pushes a command.

Custom attributes: `writeFlowableAttribute`, per load-bearing fact 2. Standard BPMN
attributes (`name`, `cancelActivity`, `isSequential`) go through `modeling`, not `$attrs`.

### 5–6. Backend: snapshot field and apply

`WorkflowElementSnapshot.cs` — append the optional parameter (done in step 3).

`WorkflowBpmnXml.cs` — add `Apply<Element>Snapshot(XElement, WorkflowElementSnapshot)`
and dispatch from the branch chain in `ApplyElementSnapshots`. Match on
`element.Name.LocalName` plus a child event-definition check for variants:

```csharp
if (string.Equals(element.Name.LocalName, "startEvent", StringComparison.Ordinal) &&
    element.Element(BpmnNamespace + "timerEventDefinition") is not null)
{
    ApplyTimerStartEventSnapshot(element, snapshot);
}
```

The branches are **not `else if`** — one element can fall into several handlers. The
loop only visits elements in `BpmnNamespace`, matched by id or by *unique* name. Set
an attribute to `null` to remove it.

### 7. Validate — at `/prepare`

`Build<Element>ValidationErrors(XDocument)` registered in `ValidateProcess`. Errors
for what cannot possibly work; warnings for probably-wrong-but-maybe-legitimate.
Every message must name the element (`name` attribute, falling back to `id`) —
"validation failed" tells an author nothing about which of forty elements to look at.

⚠️ Per load-bearing fact 4, write endpoint tests against `/prepare`, never `/publish`.

**If your rule is scope-sensitive** — link events matching per process level, for
instance — note that **every existing validator uses flat `document.Descendants(...)`
and there is no precedent to copy.** You need a helper that enumerates scope
containers (the `bpmn:process` plus each `subProcess` / `transaction` /
`adHocSubProcess`) and reads each container's **direct `Elements()`**. Copying the
nearest neighbour gives you a global matcher, which is usually exactly what the AC
forbids.

### 8. Studio UI

`WorkflowStudio.tsx`. Three parts:

1. A `<Element>Modal` component — model: `TimerIntermediateCatchEventModal`.
2. Editor state, plus a branch in **`onRequestConfigure`** — a `useCallback`, **not** a selection effect. It is invoked over the interop bridge as `RequestConfigureElement` and reached **only** through the right-click "Configure…" context menu. Selecting an element opens nothing; an E2E test that clicks to configure will fail.
3. An apply handler calling `workflow.update<Element>Properties(handle, payload)`.

**The state clearing is N×N, not 1×N.** Clear every other editor in your branch —
*and* add `set<YourEditor>(null)` to every existing branch, including `selectWorkflow`.
Grep for an existing `set*Editor(null)` and match its occurrence count exactly — it
was 12 at the time of writing and it moves.

Mantine v9 only. `Tooltip` from `@mantine/core`, never a native `title`. Toasts through
`toast` from `@/components/notifications/toast` — importing `@mantine/notifications`
directly is an ESLint **error**. In-page `<Alert>` for conditions belonging to the
page; toast for transient feedback.

### 9. Fixture and tests

- **Fixture** — a minimal `.bpmn` using the element. #103's must-haves name `tests/AutoNate.E2E.Tests/Bpmn/Fixtures/` (or equivalent); **that directory does not exist yet** and there are no `.bpmn` files in the repo. If #103 hasn't landed, you are creating it.
- **`tests/AutoNate.Web.Tests/WorkflowBpmnXmlTests.cs`** — round-trip and every validation branch. No engine needed; these are where most per-element logic lives.
- **E2E** — `RequiresService=Flowable` trait, or CI's exclusion stops holding and `ci.yml`'s shard reconciliation will notice.
- **`tests/AutoNate.Web.Tests/Invariants/DoNotRenameGuardTests.cs`** must still pass if you touched the namespace.

Assert that the element *does something*. See `references/testing-bpmn-elements.md` —
that is where most of the value is.

## Definition of done

- [ ] The element is authorable, configurable, deployable and executes
- [ ] Configuration round-trips through save and reload
- [ ] Misconfiguration is refused at `/prepare`, naming the element
- [ ] A fixture backs the inventory row
- [ ] A test asserts behaviour, not deployment
- [ ] **This skill is corrected for anything it got wrong, in this PR** — and if it needed no change, the completion comment says so explicitly
- [ ] `npm run lint` passes without raising `--max-warnings` (currently 104 — a ratchet)
- [ ] Full backend suite passes (`cd infra && docker compose -p infra up -d postgres nats nats-init redis`)

## Worked example

`references/worked-example-timer-boundary.md` walks #157 through the steps.
