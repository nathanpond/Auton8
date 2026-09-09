# Testing a BPMN element

The mistake this file exists to prevent: **asserting that a diagram deployed.**

Deployment succeeding is the *current* behaviour for every unsupported element in
Auton8 — they deploy with a warning and do nothing. A test asserting deployment
therefore passes both before and after your work, and proves nothing.

## The rule

Assert the **observable consequence**, one step further out than feels necessary.

| Element | Weak assertion (passes when broken) | Real assertion |
|---|---|---|
| Timer boundary, interrupting | boundary path was taken | attached activity is **cancelled** *and* boundary path taken |
| Timer boundary, non-interrupting | parallel path ran | parallel path ran *and* attached task is **still active and completable** |
| Multi-instance | 5 instances were created | the **collected element values** are the 5 collection items |
| Event-based gateway | the message path was taken | the message path was taken *and* the timer subscription is **gone** |
| Signals, process-scoped | the same-process instance woke | it woke *and* a **different definition's** instance did not |
| Link events | the process deployed | execution reached the **end event past the catch link** |
| Lanes | the task has candidate groups | a group member sees the task *and* a **non-member does not** |
| Compensation | the handler ran | handlers ran in **reverse order**, and only for **completed** activities |
| Manual task | completing returned 200 | the **next activity** became active |
| Job retry | retry returned 200 | the **process advanced past** the failed step |
| Complex gateway | the route the script chose was taken | **both** routes asserted — one-way passes for a gateway that always takes the first flow, which is what this element does unexpanded |
| Complex gateway, bad route | the activity failed | the message names **both** what came back and what was allowed |
| Publish-time refusal | a 4xx came back | 4xx **and** the engine was never called (`Assert.DoesNotContain("Deploy:<key>", …)`) — otherwise "deploy first, complain after" passes |
| Any publish gate | the invalid diagram is refused | plus a **valid** diagram still publishes — a gate that refuses everything passes the first half |
| Compensation ordering | the handlers ran | the recorded **trail** is `h2;h1;` — see below, timestamps tie |
| Ad-hoc subprocess | the chosen activity started | it started, the **other did not**, and starting it **again runs it again** |
| Data object type | the variable exists | its **type** is `double` — the wrong form deploys happily and yields `string` |
| Execution-view mapping | the gateway is highlighted | **no generated id** leaks into the highlight set (`__autonateRoute…`), including generated **sequence flows** |
| Version pinning | the diagram renders | republish with a change, then assert the running instance still renders **v1** |

The pattern: a test that only checks the positive half usually passes against an
implementation that does the thing unconditionally. The negative half — what should
*not* have happened — is what distinguishes correct from approximately correct.

## Synthesizing failure

Any AC demonstrating a failure case must say how to produce it, or execution will
invent one. For BPMN work the reliable techniques:

- **Refusal tests** — build the invalid diagram deliberately (link throw with the catch deleted; two catches sharing a name; event-based gateway pointing at a user task). Assert both the refusal *and* that the message names the element.
- **False-positive guards** — the paired test that must still publish. For a validator warning on unset variables, publish a condition referencing a variable a preceding script *does* set and assert **no** warning. A validator that cries wolf gets ignored, and only this test catches that.
- **Before/after on the same diagram** — for `#168` (retry points), run the same failing two-step process with and without the marking; the *difference* is the feature. Asserting only the marked case passes against a no-op attribute.
- **Regression by reverting** — for a security or silent-no-op fix, run the new test against the pre-change build and watch it pass. If it does not pass before your change, it is not testing what you think.
- **Mutate the check, not the fixture** — the cheapest sensitivity test is to break the production line the test exists for (`if (true) return result;`, `if (false)` on a guard, delete one call from the expansion pipeline) and confirm **exactly** the intended tests fail and no others. This catches the test that passes for the wrong reason, which reverting does not: in M4 an "undeclared error is not catchable" test kept passing with the declaration check deleted, because its fixture 404'd before that code ever ran.

## Two traps that cost real time in M4

**Timestamps tie, so never assert order by sorting them.** Compensation handlers all
completed inside the same millisecond; sorting `historic-activity-instances` by
`startTime` returned them in an order that *looked* like proof and was really just
list order. Have each step **append to one variable** and assert the string
(`trail == "h2;h1;"`). The same applies to anything that runs in one transaction.

**A negative assertion that runs too early is indistinguishable from success.**
"The boundary did not fire", "the other route was not taken", "no handler ran" are
all true of a process that has not started yet. Wait for a **positive** signal first,
then assert the negative alongside it — and prefer waiting on the thing that proves
the process got where you think it did.

## Traits and the CI contract

Anything touching a real engine carries `RequiresService=Flowable`. CI excludes those
by trait (159/169 E2E run by design), and `ci.yml` reconciles the discovered test
count against what the shards actually ran — a filter that matches nothing would
otherwise read as a faster, greener build.

Unit-level tests (`WorkflowBpmnXmlTests`, validation, serialisation round-trip) need
no engine and no trait. Prefer them: they are faster and they cover the validation
branches, which is where most of the per-element logic lives.

## Timing

Timer tests need elapsed time. Keep durations short (seconds), prefer a duration over
a cycle for determinism, and check whether Flowable 8.0.0's test support exposes clock
control before resorting to real waits — a suite full of `sleep` is a suite people
start skipping.

## Running the suite

The backend suite needs Postgres, NATS and Redis on the `infra` compose project:

```
cd infra && docker compose -p infra up -d postgres nats nats-init redis
```

Full run is roughly 8 minutes. Interrupted runs strand `autonate_test_*` databases
(#119) — worth knowing when a machine starts behaving oddly.

## How to observe the consequence

The assertions above demand you check what actually happened, which needs a way to
see it. Every existing workflow E2E test is Playwright/UI, so the path of least
resistance is asserting a status pill in the executions grid — which is much closer
to "it deployed" than this file wants.

Use the history endpoint instead: **`GET /api/executions/{processInstanceId}/history`**
(`src/AutoNate.Web/Endpoints/ExecutionEndpoints.cs`). It gives you the activity ids
the instance actually passed through, which is what turns "the process advanced" from
an inference into an assertion. Poll it rather than sleeping.

For validation assertions, post to **`/api/workflows/prepare`** and read `errors` /
`warnings`. `POST /api/workflows/{id}/publish` never calls `ValidateProcess` — an
invalid diagram posted there returns 200 and deploys. A test written against
`/publish` to prove "misconfiguration is refused" passes while proving nothing, which
is the failure this whole skill is about.

---

## Asserting that an activity was cancelled (#177)

**Do not assert on a history row's `DeleteReason`.** Flowable 8.0.0 does not populate
it when a boundary event cancels an activity — the cancelled row carries an `endTime`
and a null `deleteReason`, which is indistinguishable from one that completed:

```
work       type=userTask      end=19:38:17  deleteReason=None
timeout    type=boundaryEvent end=19:38:17  deleteReason=None
```

`CancelledActivityIds` on the diagram detail **is** correct as of #177, so assert on
that. It derives cancellation from the diagram — an activity is cancelled when an
*interrupting* boundary event attached to it has ended — rather than from a field the
engine leaves empty.

Two traps worth knowing:

- **Assert with the instance still RUNNING.** Cancellation used to be read from the *process instance's* `DeleteReason`, so a test on a cancelled instance passed for two years while a boundary-cancelled activity rendered as completed. A test that tears the process down cannot see the bug.
- **Assert the complement.** `CompletedActivityIds` is built by *excluding* the cancelled set, so an activity wrongly absent from one silently appears in the other. Assert both: in cancelled, and **not** in completed.
