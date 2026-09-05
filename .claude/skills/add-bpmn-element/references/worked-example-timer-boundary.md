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

## 2. Palette

`workflow.js` → `BPMN_MENU_ENTRIES`. Boundary events attach to an activity rather
than being dropped on empty canvas, so check how bpmn-js's own context pad offers
them before adding a palette entry — a palette entry that cannot be dropped anywhere
useful is worse than none. If the context pad already offers it, the manifest change
may be all step 2 needs, and the story should say so.

## 3. Read back

`describeBusinessObject()` already calls `describeTimerStartEvent(businessObject)`.
That helper reads `eventDefinitions` looking for `bpmn:TimerEventDefinition` — it is
shape-compatible with a boundary event, because both carry the definition the same
way.

Add `cancelActivity` (the interrupting flag, `true` by default per the BPMN
specification) to the description, and reuse the existing timer fields rather than
adding parallel ones.

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

## 8. Studio UI

`TimerStartEventModal` exists and is close. Either extend it to handle both shapes or
add `TimerBoundaryEventModal` beside it — extending is preferable, since two modals
editing the same timer definitions will drift.

The branch goes in `onRequestConfigure` (a `useCallback`, **not** a selection effect —
it is reached only through the right-click "Configure…" menu), matching
`selection.type === "bpmn:BoundaryEvent"` plus `"timerDuration" in selection`. Routing
is key *presence*, so `describeTimerStartEvent` must keep omitting the keys for
non-timer elements.

Clearing is **N×N**: clear every sibling in your branch, *and* add
`setTimerBoundaryEditor(null)` to all ten existing branches including
`selectWorkflow`.

Add the interrupting toggle with a sentence explaining the consequence — an author
who does not know the difference will pick the default and be surprised.

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
