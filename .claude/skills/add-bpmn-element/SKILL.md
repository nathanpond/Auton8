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
| A browser round-trip test | Author's configuration silently disappears on their next save (fact 8) |
| Deploying the expansion once | Publish answers 500 on a schema violation the unit tests cannot see (fact 9) |

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

## When a cold test is required — and what it costs to skip one

**Correcting a skill by reading it is not sufficient.** On 2026-09-05 all ten project
skills were cold-tested — an agent given only the skill and one realistic task, asked
to plan and report what was wrong. Every skill came back with findings, including six
of ten corrections that had been made hours earlier by reading the code carefully. Two
skills had the blast radius of a permission failure exactly backwards; one claimed a
trap that does not reproduce when measured; one worked example would have produced the
silent no-op its own skill exists to prevent — and `verify-symbols.sh` was **green**
against it, because a mechanical path check only ever reads `SKILL.md`.

So the rule:

- **A cold test is required** before a skill is relied on by work it has not yet been
  used for, and after any change to its *steps* (as opposed to a path or a symbol).
- **`verify-symbols.sh` is necessary and not sufficient.** It catches rot in claims;
  it cannot catch a step that is coherent, followed, and wrong.
- **A browser or engine check is required** for any claim about what bpmn-js preserves
  or what Flowable accepts. Facts 8 and 9 below are both classes where reading the
  code gives a confident wrong answer.

## Honest record: this skill went largely unused in M4's later stories

The M4 stories implemented after this skill was written — #218 (complex gateway),
#115 (compensation), #163 (ad-hoc subprocess), #166 (data objects) — were built
**without invoking it**. That is a finding about the skill, not about the work, and
the story that scheduled this review named it as one: *"a skill nobody reaches for is
a worse problem than an inaccurate one, and the remedy is different."*

The diagnosis, from what those stories actually needed:

- **They did not start at the element.** Each began with *"what does the engine
  actually do with this?"* — a probe against a running Flowable — and the answer
  reshaped the story before any of the nine steps applied. #218's expansion shape,
  #115's refusal of waiting handlers and #166's storage decision were all settled by
  probing, and none of them is a step in this skill.
- **The nine steps assume the element is authorable as drawn.** Four of M4's later
  elements needed a publish-time **expansion** instead, because the engine does not run
  what the author draws. That path — rewrite the deployed copy, leave the authored
  diagram alone — is now the milestone's dominant pattern and appears nowhere in the
  step list.
- **The skill is 380 lines.** It is at the length where a reader skims, which is its
  own answer to why it was not opened.

**The remedy is structural, not another correction**, and it is bigger than this pass:
lead with the engine probe, make expansion a first-class path beside the nine steps,
and cut what the first four stories never used. Raised as a finding here rather than
attempted at the tail of a long run — a restructure done carelessly would be worse
than the skim.

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

**4. `/publish` validates — since #225. It did not before, and the reversal matters.**
`WorkflowBpmnXml.ValidateProcess` now has **two** call sites: `POST
/api/workflows/prepare` and `POST /api/workflows/{id}/publish`, which answers 400 with
the errors before deploying.

*This entry used to say the opposite*, and it was right when written: publish went
straight to `DeployProcessAsync`, so every rule written as a gate was advisory and a
test posting an invalid diagram to `/publish` passed with a 200 deploy. Two
consequences of the change:

- **A validation test may now be written against `/publish`**, and asserting the 4xx
  is no longer enough on its own — also assert the engine was never called
  (`Assert.DoesNotContain("Deploy:<key>", factory.FlowableStub.Calls)`), or an
  implementation that deploys first and complains after still passes.
- **Publish is stricter than it was**, so a diagram that published last month may be
  refused now. When #225 landed, 4 of 11 stored dev models were newly refused — all
  for defects that already failed at run time.

`ValidateProcess` and `ValidateStructureForPublish` are **one set**: the latter
delegates to the shared `BuildStructureErrors`, and
`ValidateProcess_IncludesEveryRulePromotedToPublish` asserts they agree. They diverged
once — #225 pointed publish at `ValidateProcess`, which did not contain the promoted
structure rules, and three of them silently stopped running. Add a new rule to the
shared builder, not to one caller.

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

**Not every waiting element has this problem, so check rather than assume.** #157
established that timers *wake themselves* — the job executor polls for due jobs, and a
timer boundary fires with no `flowable:async` on the activity it guards. Conditional
events sit at the other end: a behaviour class, and no trigger at all. Ask which kind
your element is.

**6. Not every element needs all nine steps — some need only validation.** #161
(embedded subprocess) added no describe helper, no `update*Properties`, no snapshot
field and no modal: bpmn-js already authors subprocesses and their expand/collapse
(12 `sub-process` entries in the vendored bundle), the engine already executes them,
and the only Auton8-side work was *refusing the shapes that fail*. Steps 3–6 and 8
were correctly skipped.

So read the nine steps as a checklist to answer, not a sequence to perform. The
question each step asks is "does this element carry configuration the studio must
round-trip?" — when the answer is no, the story is a validation story and the honest
completion comment says which steps did not apply and why.

**8. bpmn-js DROPS what its moddle does not model — and it does so silently.**
Three separate times in M4, verified in a browser rather than reasoned about:

| what was stored | what came back after a studio save |
|---|---|
| `<bpmn:script>` child on a `complexGateway` (#218) | **gone** — ComplexGateway has no `script` property |
| `itemSubjectRef="xsd:double"` on a `dataObject` (#166) | **gone** — moddle resolves it as a *reference*, and a bare QName names nothing in the document |
| `itemSubjectRef="ItemDouble"` → a real `<itemDefinition>` (#166) | survives, but **the engine then ignores the type** and every variable is `string` |

So: **an author's configuration goes in an `autonate:` attribute via `$attrs`** — the
route `runAs` already uses and the only one proven to survive — and publish rewrites
the deployed copy into whatever the engine actually reads. Never store authoring data
in a child element or a typed moddle property the modeller does not know about, and
never conclude it round-trips without a browser test: this cannot be established by
reading, because the vendored bundle is the authority.

**`writeAutoNateAttribute` mutates `$attrs`; it must never assign it.** moddle defines
`$attrs` on `Base` with only a getter, so `businessObject.$attrs = …` throws *"Cannot
set property $attrs of #<Base> which has only a getter"*. Every element it had been
used on happened to have a writable own property until a complex gateway came along;
the symptom was Apply failing with a **clean console** (the error went to a toast) and
the modal left sitting over the Save button.

**9. Flowable validates the DEPLOYED XML against the strict BPMN schema, and a
violation is a 500 at publish — not a degradation.** This is a whole class of failure
the earlier version of this skill did not mention, and M4 hit four of them:

| written | refused with |
|---|---|
| `resultVariable` on `bpmn:scriptTask` | `Attribute 'resultVariable' is not allowed…` — it is `flowable:resultVariable` (still open as #230 for author-drawn script tasks) |
| `scriptFormat` / `<script>` left on a `bpmn:complexGateway` | same shape — strip authoring properties from the deployed copy once they have moved |
| a generated node appended after an `<association>` | `cvc-complex-type.2.4.a: Invalid content was found starting with element 'endEvent'` — **artifacts must come after every flow element**, so insert generated nodes before the first artifact (`AddFlowElement`) |
| `itemSubjectRef="xsd:double"` with no `xmlns:xsd` | `UndeclaredPrefix: Cannot resolve 'xsd:double' as a QName` — a QName's prefix must be declared, and no studio diagram carries one |

None of these degrade gracefully. If an expansion writes anything into the deployed
copy, deploy it once against a real engine before believing it.

**7. Some elements are removed rather than added, and that is a real outcome.**
Three ways so far, each with a different mechanism — pick by *why* it cannot work:

| Why | Treatment | Example |
|---|---|---|
| No model type at any layer | `studio: withdrawn`, refused at publish | link events (#160, spike #217) |
| No seam reaches it | delivered by composition instead | complex gateway (#218, spike #155) |
| It runs, but does nothing useful | **converted at design time** to the element that does | manual task, generic task (#167) |

The third is the newest and the least obvious: `bpmn:manualTask` and `bpmn:task` both
deploy and pass straight through, so a diagram containing one finishes having skipped
the step somebody was meant to perform. The studio replaces them with a user task on
**drop and on load**, and publish refuses any that survive.

⚠️ **Converting is not free, and the trap is namespaces.** A marker written with
`writeFlowableAttribute` is `flowable:`-prefixed, and bpmn-moddle **silently drops an
attribute whose prefix the document never declares**. Auton8's own starter diagram
declares `xmlns:flowable`; a diagram authored in another modeller does not — which is
exactly the diagram a conversion exists for. #167 lost its marker this way and only
caught it because the test read the saved XML instead of trusting the on-screen
notice. If you write an attribute during a conversion, declare the namespace on
`definitions.$attrs` first.

⚠️ **A conversion needs both paths.** Drop-only leaves every imported diagram
untouched, which is the population that most needs converting. `loadXml` and the
`importXML` inside `createModeler` are separate call sites; both need it.

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

A rule that should apply at *every* depth — "every subprocess anywhere must have a
start event" — is the ordinary flat `document.Descendants(...)` case and needs none of
the machinery below. #161 is that shape, and its test asserts a nested subprocess is
caught, because a check that walked only top-level children would pass a diagram that
fails one level down.

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
was 12 when written, 14 after #157, and it moves with every editor added.

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
- [ ] `npm run lint` passes without raising `--max-warnings` (currently 100 — a
      ratchet). If your story consumes a warning, **lower it to the new count in the
      same commit**: the budget tracks reality downward only. #158 took it 104 → 103
      by using an import that was sitting unused.
- [ ] Full backend suite passes (`cd infra && docker compose -p infra up -d postgres nats nats-init redis`)

## Worked example

`references/worked-example-timer-boundary.md` walks #157 through the steps.
