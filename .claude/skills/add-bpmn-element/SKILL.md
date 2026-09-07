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
| The manifest entry | Element works and the studio still calls it "coming soon" |
| Validation | Misconfiguration fails at runtime, on whoever ran the process |
| Fixture | #103's inventory has a verdict with no evidence behind it |
| The wake-up trigger | Element deploys, waits correctly, and never resumes (load-bearing fact 5) |

**The silent no-op is the failure this epic exists to end.** An element that deploys
and does nothing is worse than one that refuses, because nobody finds out until a
process is running.

## Before you start

Every path and symbol below is a **claim that may have rotted**. Run
`scripts/verify-symbols.sh` first — it checks every path and symbol this skill cites,
plus the three load-bearing claims most likely to go stale, and exits non-zero when
one has. When a code change invalidates a step here, **fix this skill in the
same commit** — "later" does not happen. #174 is the scheduled consolidation pass;
#157, #158, #160 and #161 each correct this skill in their own PR. #107 rewrote
step 1 and the manifest checks in `scripts/verify-symbols.sh`.

Read the element's story and #103's inventory row first, then ask the two questions
**in this order** — asking them the other way round is what cost #217 a spike:

1. **Does `flowable-bpmn-model-8.0.0.jar` have a type for this element at all?**
   ```sh
   unzip -l ~/.m2/.../flowable-bpmn-model-8.0.0.jar | grep -i '<ElementName>'
   ```
   If not, stop and say so on the story. Nothing downstream can help: the XML
   converter has no type to build, so the element is **discarded before validation
   or behaviour lookup ever runs**, and the diagram deploys with the element simply
   gone. Link events are this case (#160, #217) — implementing them would mean a
   model type, a converter, a parse handler, a replacement validator, a behaviour
   *and* a token transfer, which is six layers rather than one.

2. **Does it have an `ActivityBehavior`?** If the model type exists but the
   behaviour does not, that is #155/#165 territory — a custom `ActivityBehavior`
   through the existing factory, one layer, not this skill.

The distinction matters because both present identically from the studio: you draw
it, it deploys, nothing happens. Only the remedy differs, and the model-layer case
has no proportionate remedy at all.

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

**5. A behaviour class is not the same as a trigger.** #158 found this the hard
way, and it is the newest way to ship a silent no-op. Flowable has
`IntermediateCatchConditionalEventActivityBehavior` and
`BoundaryConditionalEventActivityBehavior` — so #103's inventory says conditional
events execute, and they do. They still never fire, because **Flowable does not
re-evaluate conditional events when a variable changes.** Something has to call
`POST /runtime/process-instances/{id}/evaluate-conditions` (POST, not PUT — PUT
returns a 500 reading "Request method 'PUT' is not supported", which looks like an
engine fault rather than a wrong verb).

So for any element that *waits*, ask the third question: **what makes it wake up,
and does Auton8 do that?** The inventory cannot answer it — deploying and starting
proves instantiation, and a process parked forever looks identical to one that is
correctly waiting. `IFlowableClient.EvaluateConditionalEventsAsync` is called after
every variable write, after every task completion, and after starting an instance;
those are the three moments a token can arrive somewhere it could already leave.

The test that catches this is behavioural and cannot be faked: change the world from
outside the process and assert it moved. See
`tests/AutoNate.E2E.Tests/ConditionalEventExecutionTests.cs`.

## Steps in order

### 1. Move it in the support manifest — one edit

**#107 landed.** `src/shared/bpmn-support.json` is the single source of truth. The
SPA imports it (`src/AutoNate.Spa/src/lib/bpmn/support.ts`, via the `@shared` alias);
`AutoNate.Web.csproj` embeds the same bytes as `AutoNate.Web.bpmn-support.json`. The
old `SUPPORTED_BPMN_TYPES` / `COMING_SOON_BPMN_TYPES` arrays and the
`UnsupportedRuntime*` deny-lists are gone, and `BpmnSupportManifestTests` fails if
either comes back.

Each of the 68 entries carries **two independent axes**, and picking the wrong one is
the mistake this step exists to prevent:

- **`studio`** — `supported` | `coming-soon` | `withdrawn`. What the BPMN types panel
  advertises. **This is the field your story moves**, from `coming-soon` to
  `supported`, once the element is authorable, configurable and round-tripping.
- **`engine`** — `executes` | `annotation` | `cannot-execute`. What Flowable does
  with it, established by deploying it in #103. This drives publish validation. Do
  not touch it unless you have re-run the element against a live engine; it is a
  measurement, not a preference.

The invariant `studio=supported ⟹ engine≠cannot-execute` is enforced by test. If
your element's `engine` is `cannot-execute`, moving `studio` to `supported` fails the
suite — correctly: see the note under "Before you start" about #155/#165 territory.

Entries are keyed on **`(localName, eventDefinition)`**, not on `localName`. That is
what lets the manifest refuse one boundary variant while permitting the other seven.
`localName: "*"` means an activity marker, keyed on the marker alone.

⚠️ An element the manifest marks `cannot-execute` is now a **deployment error**
carrying its `reason`, not a warning. So a `reason` is required on those entries and
is user-facing text — write a sentence an author can act on.

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

- **Fixture** — a minimal `.bpmn` using the element. #103 landed these in `tests/fixtures/bpmn-inventory/`, one per manifest entry, generated and then deployed against a live engine. Yours almost certainly exists already; extend it rather than starting a new directory.
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
- [ ] A test asserts behaviour, not deployment — and for anything that waits, that it *resumes*
- [ ] **This skill is corrected for anything it got wrong, in this PR** — and if it needed no change, the completion comment says so explicitly
- [ ] `npm run lint` passes without raising `--max-warnings` (currently 103 — a
      ratchet). If your story consumes a warning, **lower it to the new count in the
      same commit**: the budget tracks reality downward only. #158 took it 104 → 103
      by using an import that was sitting unused.
- [ ] Full backend suite passes (`cd infra && docker compose -p infra up -d postgres nats nats-init redis`)

## Worked example

`references/worked-example-timer-boundary.md` walks #157 through the steps.
