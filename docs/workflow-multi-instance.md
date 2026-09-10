# Repeating a step (multi-instance)

A multi-instance marker makes one step run many times — once per item in a list,
or a fixed number of times. In the studio it is the "Repeat this step" panel on
any activity carrying the marker; apply the marker from the element's replace
menu first, then open the panel.

This page documents what each field does and, where it matters, what Flowable
8.0.0 actually does with it — several of these were established by running them
against the engine rather than read out of the specification.

## The fields

| Field | Written as | What it does |
|---|---|---|
| List to repeat over | `flowable:collection` | One run per item. |
| Name for each item | `flowable:elementVariable` | What each run calls its item. |
| Or a fixed number of runs | `bpmn:loopCardinality` | That many runs, no list. |
| Stop early when | `bpmn:completionCondition` | Ends the loop, cancelling what is still running. |
| Collect each run's result from / Into | `flowable:variableAggregation` | Gathers one variable per run into a list. |
| Run them one at a time | `isSequential` | Off means every run starts at once. |

### A list, or a count — not both

`flowable:collection` and `loopCardinality` are alternatives. **Flowable reads
the collection and ignores the count when both are set**, so a diagram carrying
both is refused at publish naming the step. Without that refusal an author asks
for exactly three runs, gets one per item, and nothing anywhere says why.

An empty list is not an error: the step completes immediately without creating
any runs, and the process carries on.

The count may be an expression — `${approverCount}` — resolved by the engine when
the step is reached.

### Reading the item

Scripts read process variables through the `variables` API, so a run's item is
`variables.get('item')` and not a bare `item`. The sandbox binds no bare
identifiers; `item` on its own is a `ReferenceError`.

### Stopping early

"Stop early when" is a condition the engine evaluates after each run finishes.
Flowable exposes `nrOfInstances`, `nrOfCompletedInstances` and
`nrOfActiveInstances` to it, so "two approvals are enough" is
`${nrOfCompletedInstances >= 2}`.

**The runs still outstanding are cancelled, not left behind.** A user task
belonging to a cancelled run disappears from its assignee's list the moment the
condition is met — this is the half of the feature that is easy to implement away
by accident, so it is asserted directly in
`MultiInstanceExecutionTests.A_completion_condition_ends_the_loop_early_and_cancels_the_rest`.

### Collecting the results

Fill both halves or neither; one alone is refused at publish, because a source
with nowhere to go collects nothing and a target with no source collects nothing,
and in each case the author has filled in a field the engine would ignore.

Given "collect `score` into `scores`", each run's `score` is gathered into a list
called `scores` on the process, one entry per run. It is available once the step
completes.

One caveat worth knowing: a script's `variables.set('score', …)` also writes
`score` through to the process, so reading `score` on the parent afterwards gives
whichever run finished last. **Read the aggregated list, not the source
variable.**

### Assignment

A multi-instance user task produces one task per run, each independently
assignable and completable. Assigning by the element variable —
`flowable:assignee="${approver}"` with `elementVariable="approver"` — gives each
run its own assignee, and completing one leaves the others untouched.

## What is not supported

- **The loop marker** (`standardLoopCharacteristics`) is withdrawn. Flowable
  8.0.0 accepts it at deployment and then never repeats the activity, so a
  diagram using it is refused at publish naming the step rather than deployed to
  run once and look broken. Use a multi-instance marker, or a gateway loop.
- **Multiple aggregated variables.** The panel collects one variable per step,
  because that is the question authors have. A hand-written
  `flowable:variableAggregation` with several `flowable:variable` children is
  left alone by publish and works.

## Where this lives in the code

The studio stores "stop early when", the fixed count and the two aggregation
fields as `autonate:` attributes on `bpmn:multiInstanceLoopCharacteristics`, and
publish rebuilds the real BPMN children from them. That is not tidiness: bpmn-js
is vendored with no Flowable moddle extension, so a `<bpmn:loopCardinality>`
child or a `<flowable:variableAggregation>` extension element written into the
diagram is **dropped on the author's next save**, silently and with their
configuration inside it. The attribute survives the round trip; the child is put
back on the way to the engine.

Order matters when it is rebuilt. The schema sequence is `extensionElements`,
`loopCardinality`, …, `completionCondition`, and getting it wrong fails the
deployment with `cvc-complex-type.2.4.d` naming an element the author never
typed. `MultiInstanceExpansionTests` pins the order.
