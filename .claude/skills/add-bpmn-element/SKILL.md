---
name: add-bpmn-element
description: Use when adding support for a BPMN element or event definition in the workflow studio — a new event type, task type, gateway, subprocess, activity marker or data element. Starts at the engine, routes to one of three delivery paths (authored, expanded, or removed), and ends with a test that proves the element does something, instead of drawing fine and silently doing nothing.
---

# Adding a BPMN element

**The silent no-op is the failure this epic exists to end.** An element that deploys
and does nothing is worse than one that refuses, because nobody finds out until a
process is running.

Every path and symbol below is a **claim that may have rotted**. Run
`.claude/skills/add-bpmn-element/scripts/verify-symbols.sh` first — it checks every
path, symbol and load-bearing claim this skill cites and exits non-zero when one has
gone stale. When a code change invalidates a step here, **fix this skill in the same
commit**; "later" does not happen.

---

## Step 0 — Probe the engine. Everything else depends on the answer.

**Start here, before reading the rest.** Every M4 element story that was actually
built — #218 (complex gateway), #115 (compensation), #163 (ad-hoc subprocess), #166
(data objects) — began with *"what does the engine actually do with this?"*, and the
answer reshaped the story before any authoring step applied. None of what it found was
reachable from a step list:

| what was assumed | what the engine did |
|---|---|
| `complexGateway` is walked past | runs as an exclusive gateway and **silently picks a branch** (#218) |
| a compensation handler may wait | a **waiting** handler crashes the engine mid-completion, so it must be refused (#115) |
| a compensation **end** event compensates | it ends the process and compensates **nothing** (#115) |
| either BPMN spelling types a data object | neither both survives bpmn-js **and** types the variable (#166) |
| a type-mismatched DMN input errors | **accepted**, 201, empty result — identical to a legitimate no-match (#106) |

**Treat #103's inventory verdict as a hypothesis, not an input.** It records that an
element instantiates; it cannot record what the element *does*, and a process parked
forever looks identical to one correctly waiting.

Ask, in this order:

1. **Does `flowable-bpmn-model-8.0.0.jar` have a type for this element at all?**
   ```sh
   docker exec autonate-flowable sh -lc 'ls /app/WEB-INF/lib | grep -i bpmn-model'
   ```
   If not, **stop and say so on the story**. The XML converter has no type to build,
   so the element is discarded before validation or behaviour lookup runs, and the
   diagram deploys with it simply gone. Link events are this case (#160, #217):
   remedying it means a model type, a converter, a parse handler, a replacement
   validator, a behaviour *and* a token transfer — six layers, not one. → **Path C.**

2. **Does it have an `ActivityBehavior`?** Model type but no behaviour is #155/#165
   territory — a custom `ActivityBehavior` through the existing factory, one layer,
   not this skill.

3. **If it waits: what makes it wake up, and does Auton8 do that?** A behaviour class
   is not a trigger. Flowable has `IntermediateCatchConditionalEventActivityBehavior`,
   so #103 says conditional events execute — and they do. They still never fire,
   because **Flowable does not re-evaluate a conditional event when a variable
   changes.** Something must call
   `POST /runtime/process-instances/{id}/evaluate-conditions` (POST, not PUT — PUT
   answers 500 *"Request method 'PUT' is not supported"*, which reads like an engine
   fault rather than a wrong verb). `IFlowableClient.EvaluateConditionalEventsAsync`
   is called after every variable write, every task completion, and every start —
   the three moments a token can arrive somewhere it could already leave.

   **Not every waiting element has this problem, so check rather than assume.** #157
   established that timers wake themselves: the job executor polls for due jobs, and a
   timer boundary fires with no `flowable:async` on the activity it guards.

**How to probe.** Deploy a minimal diagram straight to the engine and drive it —
`tests/fixtures/bpmn-inventory/` has one fixture per manifest entry. Read the
engine's answer, not its documentation and not this file. The measurements above were
each worth an afternoon and each contradicted a confident reading of the code.

⚠️ **One shape is not a measurement.** #229's first probe used only the arrangement
that fails, and stopping there would have produced *"Flowable's interrupting event
subprocess does not interrupt"* — false. Probe the shape your story describes **and**
its near neighbour; the difference between them is usually the finding.

---

## Step 1 — Pick the path. There are three, and only one is the nine steps.

The old version of this skill had a single sequence and assumed the element was
authorable as drawn. Four of M4's later elements were not, and expansion is now the
milestone's dominant pattern.

| | when | what you write |
|---|---|---|
| **A · Authored** | the engine runs what the author draws | manifest, describe/update, snapshot, validation, modal |
| **B · Expanded** | the engine runs something *else* | a publish-time rewrite of the deployed copy; the authored diagram is untouched |
| **C · Removed** | it cannot work, or should not | `withdrawn`, composition, or a design-time conversion |

**Path A does not mean all nine steps.** #161 (embedded subprocess) added no describe
helper, no `update*Properties`, no snapshot field and no modal: bpmn-js already
authors subprocesses and their expand/collapse, the engine already executes them, and
the only Auton8 work was *refusing the shapes that fail*. Read A's steps as a
checklist to answer — "does this element carry configuration the studio must
round-trip?" — and when the answer is no, say in the completion comment which steps
did not apply and why.

---

## Path A — Authored

### A1. Move it in the support manifest — one edit

`src/shared/bpmn-support.json` is the single source of truth. The SPA imports it
(`lib/bpmn/support.ts`, via `@shared`); `AutoNate.Web.csproj` embeds the same bytes.

Each of the **69** entries carries **two independent axes**, and picking the wrong one
is the mistake this step exists to prevent:

- **`studio`** — `supported` | `coming-soon` | `withdrawn`. What the types panel
  advertises. **This is the field your story moves.**
- **`engine`** — `executes` | `annotation` | `cannot-execute`. What Flowable does,
  established by deploying it. **A measurement, not a preference** — do not touch it
  without re-running the element against a live engine.

`studio=supported ⟹ engine≠cannot-execute` is test-enforced. Entries are keyed on
**`(localName, eventDefinition)`**, which is what lets the manifest refuse one
boundary variant and permit the other seven; `localName: "*"` is an activity marker.

⚠️ `cannot-execute` is a **deployment error** carrying its `reason`, not a warning, so
the `reason` is user-facing text — write a sentence an author can act on.

⚠️ **`reason` is digest-pinned.** `BpmnSupportManifestTests.Every_reason_is_still_the_one_that_was_measured`
fails on any edit, naming the row. That is #380 working: regenerate
`bpmn-reason-baseline.tsv` with `AUTONATE_REGENERATE_REASON_BASELINE=1` **in the same
commit**, and check the diff is only the rows you meant.

### A2. Authoring affordance — usually no code

The modeller is **stock, vendored bpmn-js**: `createModeler` (`lib/bpmn/workflow.js`)
does `new window.BpmnJS({ container })` against a static asset at
`public/vendor/bpmn-js/bpmn-modeler.development.js`, not an npm import.

1. Set the row in `bpmn-support.json` — `studio` decides whether it is offered.
2. Add a row to **`src/shared/bpmn-palette.json`** with its label, icon class, group
   and the manifest row it claims. If it is not a shape an author drags — a marker, a
   connection, a process-level declaration — add it to `notOnThePalette` **with a
   reason saying how the author reaches it instead**.
3. Nothing else. `lib/bpmn/palette.js` derives the palette from those two files,
   overrides bpmn-js's `paletteProvider`, and filters its three popup menus
   (`bpmn-replace`, `bpmn-create`, `bpmn-append`).

`BpmnPaletteManifestTests` fails if a catalog row names a manifest element that does
not exist, if a supported element is neither offered nor excused, or if a deny key
does not occur in the vendored bundle. `WorkflowPaletteTests` opens the real palette
and all three popups in a browser.

### A3. Read it back — and all four type mirrors

`describeBusinessObject()` in `workflow.js` is the source of truth: `getElementSnapshots`
is that function mapped over the element registry, so **the describe output *is* the
snapshot wire format**. Add a field to the C# record without adding it here and it is
permanently null.

Add `describe<Element>(businessObject)` (model: `describeTimerIntermediateCatchEvent`)
and merge conditionally — see **Routing is `$type` plus key presence** below. Then
mirror the field in all four places:

1. `describeBusinessObject` — `workflow.js`
2. `ElementSelection` — `WorkflowStudio.tsx`
3. TS `WorkflowElementSnapshot` — `src/AutoNate.Spa/src/api/workflows.ts`
4. C# `WorkflowElementSnapshot` — `Services/Workflow/WorkflowElementSnapshot.cs`
   (positional record; **append only**)

### A4. Write it back — `update<Element>Properties`

Exported from `workflow.js`. Model: `updateTimerIntermediateCatchEventProperties`.

1. Pull `elementRegistry`, `modeling`, `moddle` from the handle; throw if any is
   missing or `payload?.id` is absent.
2. `elementRegistry.get(payload.id)`, then **assert `$type`** — and for an
   event-definition variant, assert the definition is present too.
3. **Clear the alternatives.** A stale `timeCycle` beside a new `timeDuration` is a
   valid-looking diagram that behaves unpredictably.
4. **Make sure a command reaches the command stack.** The existing timer functions
   assign moddle properties directly and call `modeling.updateProperties` only for the
   name — that works *because* the name update pushes a command. If your editor
   changes only a definition field, `commandStack.changed` never fires, the dirty flag
   stays false, and **the edit is silently lost on reload**. Prefer
   `modeling.updateModdleProperties(element, definition, { … })`, which always pushes.

### A5. Backend — snapshot field and apply

`WorkflowBpmnXml.cs`: add `Apply<Element>Snapshot(XElement, WorkflowElementSnapshot)`
and dispatch from the branch chain in `ApplyElementSnapshots`, matching on
`element.Name.LocalName` plus a child event-definition check for variants. The
branches are **not `else if`** — one element can fall into several handlers. Set an
attribute to `null` to remove it.

### A6. Validate — at `/prepare`

`Build<Element>ValidationErrors(XDocument)` registered in `ValidateProcess`. Errors for
what cannot possibly work; warnings for probably-wrong-but-maybe-legitimate. **Every
message must name the element** (`name`, falling back to `id`) — "validation failed"
tells an author nothing about which of forty elements to look at.

A rule that should apply at *every* depth — "every subprocess anywhere must have a
start event" — is the ordinary flat `document.Descendants(...)` case. #161 is that
shape, and its test asserts a **nested** subprocess is caught, because a check walking
only top-level children passes a diagram that fails one level down.

**If your rule is scope-sensitive**, note that every existing validator uses flat
`Descendants(...)` and there is no precedent to copy: you need a helper enumerating
scope containers (`bpmn:process` plus each `subProcess` / `transaction` /
`adHocSubProcess`) and reading each container's **direct `Elements()`**. Copying the
nearest neighbour gives you a global matcher, which is usually what the AC forbids.

⚠️ **Do not use `Descendants` where `Elements` is meant.** #229's warning fires only
for an error end event that is a *direct child* of the scope holding the handler;
`Descendants` would have made it fire on the arrangement that works correctly.

### A7. Studio UI

`WorkflowStudio.tsx`. Three parts:

1. A `<Element>Modal` component — model: `TimerIntermediateCatchEventModal`.
2. Editor state, plus a branch in **`onRequestConfigure`** — a `useCallback`, **not** a
   selection effect. It is reached **only** through the right-click "Configure…"
   context menu; selecting an element opens nothing, so an E2E test that clicks to
   configure will fail.
3. An apply handler calling `workflow.update<Element>Properties(handle, payload)`.

**The state clearing is N×N, not 1×N.** Clear every other editor in your branch *and*
add `set<YourEditor>(null)` to every existing branch, including `selectWorkflow`. Grep
`set[A-Za-z]*Editor(null)` and match the distinct count exactly — **16** at the time of
writing, and it moves with every editor added.

Mantine v9 only. `Tooltip` from `@mantine/core`, never a native `title`. Toasts through
`toast` from `@/components/notifications/toast`. In-page `<Alert>` for conditions
belonging to the page; toast for transient feedback.

⚠️ **Give every `<Alert>` an explicit `role`.** Mantine's default is `role="alert"`,
which is *assertive* — a screen reader interrupts whatever the user is reading. That
is right for "we could not reach the engine" and wrong for "nothing is configured
yet". The same defect has shipped three times (#597, and twice in #172); see #602.

---

## Path B — Expanded at publish

**The engine runs something other than what the author drew.** Publish rewrites the
deployed copy; the authored diagram is stored untouched, so the author's canvas never
changes under them. #112, #156, #115, #218, #166 and #223 are all this shape.

Its concerns are not Path A's, and none of them is intuitive:

**B1. The authored diagram is the one that is stored.** Two XML strings exist from
publish onward. `WorkflowModelVersion.BpmnXml` is the author's; the deployed copy is
the engine's. The execution view renders the **authored** one (#218), which is why B2
exists.

**B2. Map generated ids back to the author's element.** An expansion invents nodes,
and the engine's history then names ids the author has never seen. #218 shipped the
mapping and #327 records the eight surfaces that still leak raw generated ids. If you
generate a node, decide where its id surfaces and map it.

**B3. Idempotence.** Publish runs the expansion every time. Running it over an
already-expanded document must be a no-op, not a second expansion — assert it by
expanding twice and comparing.

**B4. Artifact ordering.** The strict BPMN schema requires **artifacts after every
flow element**, so a generated node appended after an `<association>` is refused with
`cvc-complex-type.2.4.a: Invalid content was found starting with element 'endEvent'`.
Insert before the first artifact — `AddFlowElement` does this.

**B5. Strip the authoring attributes from the deployed copy.** Whatever carried the
author's intent is not what the engine reads, and leaving it behind is a schema
violation: `scriptFormat` / `<script>` left on a `bpmn:complexGateway` is refused the
same way `resultVariable` on a `bpmn:scriptTask` is (it is `flowable:resultVariable`;
still open as #230 for author-drawn script tasks).

**B6. Deploy the expansion once against a real engine before believing it.** Flowable
validates the deployed XML against the strict schema and a violation is a **500 at
publish**, not a degradation. M4 hit four, including
`UndeclaredPrefix: Cannot resolve 'xsd:double' as a QName` — a QName's prefix must be
declared, and no studio diagram carries `xmlns:xsd`.

---

## Path C — Removed, composed or converted

Three mechanisms. Pick by *why* it cannot work:

| Why | Treatment | Example |
|---|---|---|
| No model type at any layer | `studio: withdrawn`, refused at publish | link events (#160, #217) |
| No seam reaches it | delivered by composition instead | complex gateway (#218, #155) |
| It runs, but does nothing useful | **converted at design time** to the element that does | manual task, generic task (#167) |

The third is the least obvious: `bpmn:manualTask` and `bpmn:task` both deploy and pass
straight through, so a diagram containing one finishes having skipped the step somebody
was meant to perform. The studio replaces them with a user task on **drop and on
load**, and publish refuses any that survive.

⚠️ **A conversion needs both paths.** Drop-only leaves every *imported* diagram
untouched — which is the population that most needs converting. `loadXml` and the
`importXML` inside `createModeler` are separate call sites.

⚠️ **The trap is namespaces.** bpmn-moddle **silently drops an attribute whose prefix
the document never declares.** Auton8's starter diagram declares `xmlns:flowable`; a
diagram authored elsewhere does not — exactly the diagram a conversion exists for. #167
lost its marker this way and caught it only because the test read the saved XML instead
of trusting the on-screen notice. Declare the namespace on `definitions.$attrs` first.

---

## Facts that bite on every path

**Custom attributes go in the `flowable:` namespace, not `autonate`.**
`writeFlowableAttribute` writes `flowable:<name>` into `businessObject.$attrs`.
`http://autonate.dev/workflows` is the **`targetNamespace` on `<bpmn:definitions>`
only** — never an attribute namespace. Even Auton8-proprietary properties use
`flowable:`, because bpmn-js loads no Flowable moddle extension here, so raw prefixed
attributes in `$attrs` are the only round-trip-safe shape.

**Routing is `$type` *plus* key presence.** `onRequestConfigure` routes on
`selection.type === "bpmn:StartEvent" && ("timerCycleCron" in selection || …)` — both
guards, at every branch. So a describe helper must gate on `$type` **and** omit the
keys for elements that don't have them. Merging a key unconditionally misroutes every
element whose `$type` also matches — an unconditional `timerDuration` sends message
and signal catch events to the timer modal. Silent, and confusing.

**bpmn-js DROPS what its moddle does not model, silently.** Verified in a browser
three times in M4, never by reading:

| stored | after a studio save |
|---|---|
| `<bpmn:script>` child on a `complexGateway` (#218) | **gone** — no `script` property |
| `itemSubjectRef="xsd:double"` on a `dataObject` (#166) | **gone** — moddle resolves it as a *reference*, and a bare QName names nothing |
| `itemSubjectRef="ItemDouble"` → a real `<itemDefinition>` (#166) | survives, but the **engine ignores the type** and every variable is `string` |

So an author's configuration goes in an `autonate:` attribute via `$attrs` — the route
`runAs` uses, and the only one proven to survive — and publish rewrites the deployed
copy (Path B). **This cannot be established by reading**; the vendored bundle is the
authority.

**`writeAutoNateAttribute` mutates `$attrs`; it must never assign it.** moddle defines
`$attrs` on `Base` with only a getter, so `businessObject.$attrs = …` throws *"Cannot
set property $attrs of #<Base> which has only a getter"*. Every element it had been
used on happened to have a writable own property until a complex gateway came along;
the symptom was Apply failing with a **clean console** (the error went to a toast) and
the modal left sitting over the Save button.

**`/publish` validates — since #225.** `ValidateProcess` has **two** call sites,
`/prepare` and `/publish`, and the latter answers 400 before deploying.

- A validation test may be written against `/publish`, but asserting the 4xx is not
  enough — also assert the engine was never called
  (`Assert.DoesNotContain("Deploy:<key>", factory.FlowableStub.Calls)`), or an
  implementation that deploys first and complains after still passes.
- `ValidateProcess` and `ValidateStructureForPublish` are **one set** — both delegate
  to `BuildStructureErrors`. They diverged once, and three promoted rules silently
  stopped running. **Add a new rule to the shared builder, not to one caller.**

**A refusal at publish is not a refusal at save — since #234.** `prepareAndStore` takes
a mode: publish refuses on any error, save refuses only when the XML could not be
normalized at all. So a new publish rule no longer silently becomes a save refusal,
which is what it did for every rule between #225 and #234. If your rule *should* block
a draft save, say so explicitly; the default is that it does not.

---

## When reading is not enough

**Correcting a skill by reading it is not sufficient.** On 2026-09-05 all ten project
skills were cold-tested — an agent given only the skill and one realistic task. Every
one came back with findings, including six of ten corrections made hours earlier by
reading the code carefully. Two had the blast radius of a permission failure exactly
backwards; one claimed a trap that does not reproduce when measured; one worked example
would have produced the silent no-op its own skill exists to prevent — and
`verify-symbols.sh` was **green** against it, because a mechanical path check only ever
reads `SKILL.md`.

- **A cold test is required** before a skill is relied on by work it has not been used
  for, and after any change to its *steps* (as opposed to a path or a symbol).
- **`verify-symbols.sh` is necessary and not sufficient.** It catches rot in claims; it
  cannot catch a step that is coherent, followed, and wrong.
- **A browser or engine check is required** for any claim about what bpmn-js preserves
  or what Flowable accepts. Both are classes where reading gives a confident wrong
  answer.

---

## Definition of done

- [ ] The element is authorable, configurable, deployable and **executes**
- [ ] Configuration round-trips through save and reload
- [ ] Misconfiguration is refused at `/prepare`, naming the element
- [ ] A fixture backs the inventory row
- [ ] A test asserts **behaviour**, not deployment — and for anything that waits, that
      it *resumes*. See `references/testing-bpmn-elements.md`, where most of the value is
- [ ] Path B only: the expansion is idempotent, its generated ids map back, and it has
      been deployed once against a real engine
- [ ] **This skill is corrected for anything it got wrong, in this PR** — and if it
      needed no change, the completion comment says so explicitly
- [ ] `npm run lint` passes without raising `--max-warnings` (currently **98** — a
      ratchet). If your story consumes a warning, lower it in the same commit
- [ ] The tier pins in `tests/tiers.env` move with any test you add. An E2E spec needs
      the `RequiresService=Flowable` trait or it lands in **slim**, where GitHub runs
      it with no engine
- [ ] Full backend suite passes (`cd infra && docker compose -p infra up -d postgres nats nats-init redis`)

## Worked example

`references/worked-example-timer-boundary.md` walks #157 through Path A.
