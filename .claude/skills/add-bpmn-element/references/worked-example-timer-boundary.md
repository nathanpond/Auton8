# Worked example: timer boundary events (#157)

The element: `bpmn:BoundaryEvent` carrying a `bpmn:TimerEventDefinition`, in both
interrupting and non-interrupting form. Chosen as the example because it reuses an
existing editor (timer definitions already work on start events) while adding a new
element shape — the two halves most stories mix.

Verify each path before following it. These were accurate on 2026-09-05.

---

## 1. Support manifest

`WorkflowStudio.tsx` — move `"Timer Boundary"` out of `COMING_SOON_BPMN_TYPES`
(category `Boundary Events`) into `SUPPORTED_BPMN_TYPES`.

`WorkflowBpmnXml.cs` — `boundaryEvent` sits in `UnsupportedRuntimeControlElementNames`.
It cannot simply be removed: that set covers *all* boundary events, and the others
are not supported yet. Narrow it the way `intermediateCatchEvent` already is —
that entry has a carve-out for the timer flavour:

```csharp
// Timer intermediate catch events are first-class — only warn for
// the message/signal/conditional flavors that aren't wired up yet.
if (localName.Equals("intermediateCatchEvent", StringComparison.Ordinal) &&
    element.Element(BpmnNamespace + "timerEventDefinition") is not null)
{
    continue;
}
```

Add the same shape for `boundaryEvent`. **This is the pattern for any element whose
support arrives one event-definition at a time**, and most of M4 is that shape.

⚠️ **This is not sufficient on its own.** `BuildUnsupportedRuntimeWarnings` has a
*second* block further down, matching `localName.EndsWith("EventDefinition")`, whose
carve-outs whitelist by definition type **and parent element type** — currently
`signalEventDefinition`/`timerEventDefinition` on a `startEvent`, and
`timerEventDefinition` on an `intermediateCatchEvent`. A timer *boundary* event needs
a third carve-out there, or it works and still emits a "timer events" warning. An
earlier draft of this example stopped at the first block and would have shipped #157
half-fixed.

## 2. Authoring affordance — zero code

The vendored bpmn-js bundle **already offers both forms** in the boundary-event
replace menu: `timer-boundary` and `non-interrupting-timer-boundary`, with
`cancelActivity: true/false` in the entry attributes. Right-click the boundary event,
use the wrench.

So step 2 is nothing. Do **not** go to `BPMN_MENU_ENTRIES` — it is dead code (see
load-bearing fact 1). Say "no palette change needed, bpmn-js already offers it" in the
story and move on.

## 3. Read back — a NEW helper; neither existing one works

⚠️ **`describeTimerStartEvent` is not reusable here.** It returns `null` unless
`$type === "bpmn:StartEvent"`, so it emits nothing at all for a boundary event. It
also reads only `timeCycle` and `flowable:endDate` — no duration, no date.
`describeTimerIntermediateCatchEvent` reads duration and date but not cycle, and is
gated on `bpmn:IntermediateCatchEvent`. **Neither covers the duration|date|cycle set
#157's first AC requires, and neither fires for a boundary event.**

Write `describeTimerBoundaryEvent(businessObject)`, gated on `bpmn:BoundaryEvent`,
reading all three timer kinds plus `cancelActivity` and `attachedToRef?.id`.

Merge conditionally under **new key names** — `boundaryTimerDuration`,
`boundaryTimerDate`, `boundaryTimerCycle`, `cancelActivity` — so they do not collide
with the existing `timerDuration` / `timerCycleCron` keys that route the other two
timer editors.

Then mirror the fields in all four places (see SKILL.md step 3).

## 4. Write back

New export `updateTimerBoundaryEventProperties(modelerHandle, payload)`, modelled on
`updateTimerStartEventProperties`:

```js
const element = elementRegistry.get(payload.id);
if (!element?.businessObject || element.businessObject.$type !== "bpmn:BoundaryEvent") {
  throw new Error(`Timer boundary event '${payload.id}' is no longer available in the diagram.`);
}
```

Then the same timer-definition lookup and the same **clear-the-alternatives**
discipline: setting `timeDuration` must clear `timeCycle` and `timeDate`.

`cancelActivity` is a standard BPMN attribute, so it goes through `modeling`, not
`$attrs`. Note that Auton8-specific properties do **not** go in
`http://autonate.dev/workflows` either — that is the `targetNamespace` on
`<bpmn:definitions>` only. Custom attributes use `writeFlowableAttribute`, which
writes `flowable:<name>`; see load-bearing fact 2 in SKILL.md.

Because this editor changes a timer definition field, make sure a command reaches the
command stack — prefer `modeling.updateModdleProperties(element, timerEventDefinition,
{ … })`. The existing timer functions get away with direct assignment only because
they also push a `name` update.

## 5. Snapshot fields

`WorkflowElementSnapshot.cs` — the timer fields exist already (`TimerCycleCron`,
`TimerEndDate`, `TimerDuration`, `TimerDate`). Append one:

```csharp
bool? CancelActivity = null);
```

Append only — the record is positional.

## 6. Apply

`WorkflowBpmnXml.cs` — `ApplyTimerBoundaryEventSnapshot`, dispatched by:

```csharp
if (string.Equals(element.Name.LocalName, "boundaryEvent", StringComparison.Ordinal) &&
    element.Element(BpmnNamespace + "timerEventDefinition") is not null)
{
    ApplyTimerBoundaryEventSnapshot(element, snapshot);
}
```

Reuse `ApplyTimerStartEventSnapshot`'s body for the definition itself; add
`cancelActivity`. Setting an attribute to `null` removes it.

## 7. Validation

`BuildTimerBoundaryEventValidationErrors`, registered in `ValidateProcess`.
`BuildTimerStartEventValidationErrors` already validates timer definitions — extract
what is shared rather than copying it, since a divergence between the two would mean
the same malformed duration is accepted in one place and rejected in the other.

Boundary-specific errors:

- a timer boundary event with no `attachedToRef`, or attached to something that no longer exists
- a **cycle** timer on an **interrupting** boundary event — it can only fire once, so the repetition is silently meaningless

## 8. Studio UI — a new modal, not an extension

⚠️ **Do not extend `TimerStartEventModal`.** It is a cron/recurrence picker built
around `parseCron` with a raw-cron override, and has no duration or date mode.
`TimerIntermediateCatchEventModal` has duration and date but no cycle. Neither covers
the required set; extending either means bolting on the half it lacks. Write
`TimerBoundaryEventModal`.

The branch goes in `onRequestConfigure` (a `useCallback`, **not** a selection effect —
reached only through right-click "Configure…"), matching
`selection.type === "bpmn:BoundaryEvent"` **and** one of your new
`boundaryTimer*` keys. Routing is `$type` *plus* key presence; both guards matter.

Clearing is N×N. **Grep for an existing `set*Editor(null)` and match its occurrence
count exactly** — it was 12 when this was written, and it moves. Do not trust a number
written down here.

Add the interrupting toggle with a sentence explaining the consequence. Note
`cancelActivity` also drives bpmn-js's dashed non-interrupting ring at render time, so
a direct moddle assignment that skips the command stack leaves the border stale as
well as the dirty flag.

## 9. Tests

`WorkflowBpmnXmlTests.cs`:
- round-trip: snapshot → XML → snapshot, including `cancelActivity`
- validation: missing `attachedToRef`; cycle timer on an interrupting boundary
- the shared timer-definition tests still pass for start events

Endpoint-level validation tests go against **`/api/workflows/prepare`**, not
`/publish` — `/publish` never validates. E2E with `RequiresService=Flowable`, and note
the assertions from `testing-bpmn-elements.md`:

- **interrupting**: attached activity **cancelled** *and* boundary path taken
- **non-interrupting**: parallel path ran *and* attached task **still active and completable**
- **timer cancelled on completion**: complete the task, wait past the duration, assert the boundary path was **never** taken — the assertion most likely to be omitted, and the defect most likely to reach production

Fixture: a minimal process with a user task carrying a 5-second interrupting timer
boundary, kept for #103's inventory row. There are no `.bpmn` files in the repo yet —
if #103 has not landed you are creating that directory.

**This example was written from reading the code, not from doing the work.** #157
carries an AC to correct it against what was actually required; #174 consolidates.
