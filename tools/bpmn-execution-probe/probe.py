#!/usr/bin/env python3
"""Start an instance per element and ask whether the element actually did anything.

#325 AC2: a test that STARTS a process instance per element and asserts the
element had its effect -- a task appeared, a variable was written, a token moved
-- rather than asserting the deployment succeeded.

The distinction is the whole story. Flowable accepted `standardLoopCharacteristics`
at deployment and then ran the activity exactly once; Manual Task and Task
(Generic) deploy and pass straight through creating nothing. All three were found
by a person, because every instrument stopped at "it deployed".

Deployments are deleted BY ID. This engine is shared.
"""
import base64, json, sys, time, urllib.error, urllib.parse, urllib.request

BASE = "http://localhost:8080/flowable-rest/service"
AUTH = base64.b64encode(b"rest-admin:test").decode()


def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(
        BASE + path, data=data, method=method,
        headers={"Authorization": f"Basic {AUTH}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode("utf-8", "replace")[:300]


def deploy(name, xml):
    boundary = "----probe"
    part = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{name}.bpmn20.xml"\r\n'
        "Content-Type: text/xml\r\n\r\n" f"{xml}\r\n" f"--{boundary}--\r\n").encode()
    request = urllib.request.Request(
        f"{BASE}/repository/deployments", data=part, method="POST",
        headers={"Authorization": f"Basic {AUTH}",
                 "Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read()).get("id"), ""
    except urllib.error.HTTPError as error:
        return None, error.read().decode("utf-8", "replace")[:300]


def observe(effect, instance_id, activity_id):
    """Did the element DO the thing, as opposed to merely existing?"""
    if effect == "task-appears":
        _, body = call("GET", f"/runtime/tasks?processInstanceId={instance_id}&size=50")
        tasks = (body or {}).get("data", [])
        if tasks:
            return (True, f"{len(tasks)} runtime task(s) on this instance")

        # A CALL ACTIVITY's task belongs to the CALLED instance, not the calling
        # one, so a query on this instance alone returns zero -- which reads
        # exactly like "the element did nothing", the verdict this probe exists to
        # make trustworthy. The child is found explicitly rather than through
        # `processInstanceIdWithChildren`, which the GET route silently ignores
        # (measured: it turned two proved elements into two false negatives).
        _, kids = call("GET", f"/runtime/process-instances?superProcessInstanceId={instance_id}&size=20")
        for child in (kids or {}).get("data", []):
            _, body = call("GET", f"/runtime/tasks?processInstanceId={child['id']}&size=50")
            child_tasks = (body or {}).get("data", [])
            if child_tasks:
                return (True, f"{len(child_tasks)} runtime task(s) in the called instance")

        return (False, "no runtime task on this instance or any child")

    if effect == "variable-written":
        _, body = call("GET", f"/runtime/process-instances/{instance_id}/variables")
        live = {v["name"]: v.get("value") for v in (body or [])} if isinstance(body, list) else {}
        if "proof" in live:
            return (True, f"proof={live['proof']!r} on the running instance")
        # A completed instance has no runtime variables; look in history.
        _, body = call("GET", f"/history/historic-variable-instances?processInstanceId={instance_id}&size=50")
        done = {v["variable"]["name"]: v["variable"].get("value") for v in (body or {}).get("data", [])}
        return ("proof" in done, f"proof={done.get('proof')!r} in history" if "proof" in done
                else f"no 'proof' variable; saw {sorted(done)}")

    if effect == "instance-waits":
        status, _ = call("GET", f"/runtime/process-instances/{instance_id}")
        if status != 200:
            return (False, "the instance is gone -- it ran straight through instead of waiting")
        _, body = call("GET", f"/runtime/executions?processInstanceId={instance_id}&size=50")
        waiting = [e for e in (body or {}).get("data", []) if e.get("activityId")]
        where = sorted({e["activityId"] for e in waiting})
        return (len(waiting) > 0, f"parked at {where}")

    if effect == "instance-ends":
        _, body = call("GET", f"/history/historic-process-instances?processInstanceId={instance_id}")
        rows = (body or {}).get("data", [])
        ended = bool(rows) and rows[0].get("endTime")
        return (bool(ended), f"endTime={rows[0].get('endTime') if rows else None}")

    return (False, f"no observer for effect {effect!r}")


def wrap(key, roots, body):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
             xmlns:flowable="http://flowable.org/bpmn"
             xmlns:autonate="http://autonate.dev/workflows"
             targetNamespace="http://autonate.dev/probe">
  {roots}
  <process id="{key}" name="probe" isExecutable="true">
    {body}
  </process>
</definitions>"""


# `javascript`, not groovy: Auton8's script host runs scripts in its own sandbox,
# and the engine refuses any other format by name (M3). `variables.set` is the
# sandbox's API -- `execution.setVariable` is the JVM one and is not exposed.
# The probe learned both of these from the engine rather than from the docs.
SCRIPT = ('<scriptTask id="Ev_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor">'
          "<script>variables.set('proof', 'ran');</script></scriptTask>")


def diagram(name, key):
    """A minimal process whose ONLY interesting element is the one under test."""
    flow = '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/><sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>'
    shell = f'<startEvent id="Start_1"/>{{elem}}<endEvent id="End_1"/>{flow}'

    if name == "User Task":       return wrap(key, "", shell.format(elem='<userTask id="Ev_1" name="approve"/>'))
    if name == "Script Task":     return wrap(key, "", shell.format(elem=SCRIPT))
    if name == "Receive Task":    return wrap(key, "", shell.format(elem='<receiveTask id="Ev_1" name="wait"/>'))
    if name == "Start Event (None)":  return wrap(key, "", '<startEvent id="Ev_1"/><endEvent id="End_1"/><sequenceFlow id="f1" sourceRef="Ev_1" targetRef="End_1"/>')
    if name == "End Event (None)":    return wrap(key, "", '<startEvent id="Start_1"/><endEvent id="Ev_1"/><sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>')
    if name == "Sequence Flow":       return wrap(key, "", '<startEvent id="Start_1"/><endEvent id="End_1"/><sequenceFlow id="Ev_1" sourceRef="Start_1" targetRef="End_1"/>')
    if name == "End Event (Terminate)":
        # The point of terminate is CANCELLING its siblings. A parallel branch is
        # parked on a user task; if terminate works, the instance ends anyway.
        return wrap(key, "", (
            '<startEvent id="Start_1"/><parallelGateway id="Gw_1"/>'
            '<userTask id="Parked_1" name="parked"/>'
            '<endEvent id="Ev_1"><terminateEventDefinition/></endEvent>'
            '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Gw_1"/>'
            '<sequenceFlow id="f2" sourceRef="Gw_1" targetRef="Parked_1"/>'
            '<sequenceFlow id="f3" sourceRef="Gw_1" targetRef="Ev_1"/>'))
    if name == "Intermediate Throw (None)":
        return wrap(key, "", shell.format(elem='<intermediateThrowEvent id="Ev_1"/>'))
    if name == "Intermediate Catch (Timer)":
        return wrap(key, "", shell.format(elem='<intermediateCatchEvent id="Ev_1"><timerEventDefinition><timeDuration>PT1H</timeDuration></timerEventDefinition></intermediateCatchEvent>'))
    if name == "Intermediate Catch (Message)":
        return wrap(key, '<message id="Msg_1" name="m"/>', shell.format(elem='<intermediateCatchEvent id="Ev_1"><messageEventDefinition messageRef="Msg_1"/></intermediateCatchEvent>'))
    if name == "Intermediate Catch (Signal)":
        return wrap(key, '<signal id="Sig_1" name="s"/>', shell.format(elem='<intermediateCatchEvent id="Ev_1"><signalEventDefinition signalRef="Sig_1"/></intermediateCatchEvent>'))
    if name == "Intermediate Catch (Conditional)":
        return wrap(key, "", shell.format(elem='<intermediateCatchEvent id="Ev_1"><conditionalEventDefinition><condition>${ok == true}</condition></conditionalEventDefinition></intermediateCatchEvent>'))
    if name == "Event-Based Gateway":
        return wrap(key, '<message id="Msg_1" name="m"/>', (
            '<startEvent id="Start_1"/><eventBasedGateway id="Ev_1"/>'
            '<intermediateCatchEvent id="C_1"><messageEventDefinition messageRef="Msg_1"/></intermediateCatchEvent>'
            '<intermediateCatchEvent id="C_2"><timerEventDefinition><timeDuration>PT1H</timeDuration></timerEventDefinition></intermediateCatchEvent>'
            '<endEvent id="End_1"/>'
            '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>'
            '<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="C_1"/>'
            '<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="C_2"/>'
            '<sequenceFlow id="f4" sourceRef="C_1" targetRef="End_1"/>'
            '<sequenceFlow id="f5" sourceRef="C_2" targetRef="End_1"/>'))
    if name in ("Exclusive Gateway (XOR)", "Inclusive Gateway (OR)", "Parallel Gateway (AND)"):
        tag = {"Exclusive Gateway (XOR)": "exclusiveGateway",
               "Inclusive Gateway (OR)": "inclusiveGateway",
               "Parallel Gateway (AND)": "parallelGateway"}[name]
        cond = ('<conditionExpression xsi:type="tFormalExpression" '
                'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">${true}</conditionExpression>')
        branch = "" if tag == "parallelGateway" else cond
        # TWO outgoing flows: Flowable refuses an exclusive gateway with one
        # (flowable-exclusive-gateway-condition-not-allowed-on-single-seq-flow),
        # which the probe found by being refused.
        other = "" if tag == "parallelGateway" else cond.replace("${true}", "${false}")
        return wrap(key, "", (
            f'<startEvent id="Start_1"/><{tag} id="Ev_1"/>{SCRIPT.replace("Ev_1", "S_1")}'
            f'<userTask id="Other_1" name="other"/><endEvent id="End_1"/><endEvent id="End_2"/>'
            '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>'
            f'<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="S_1">{branch}</sequenceFlow>'
            f'<sequenceFlow id="f4" sourceRef="Ev_1" targetRef="Other_1">{other}</sequenceFlow>'
            '<sequenceFlow id="f3" sourceRef="S_1" targetRef="End_1"/>'
            '<sequenceFlow id="f5" sourceRef="Other_1" targetRef="End_2"/>'))
    if name == "Sub-Process (Embedded)":
        return wrap(key, "", (
            '<startEvent id="Start_1"/>'
            '<subProcess id="Ev_1"><startEvent id="IS_1"/><userTask id="IT_1" name="inner"/>'
            '<endEvent id="IE_1"/><sequenceFlow id="if1" sourceRef="IS_1" targetRef="IT_1"/>'
            '<sequenceFlow id="if2" sourceRef="IT_1" targetRef="IE_1"/></subProcess>'
            '<endEvent id="End_1"/>'
            '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>'
            '<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>'))
    if name == "Ad-Hoc Sub-Process":
        return wrap(key, "", (
            '<startEvent id="Start_1"/>'
            '<adHocSubProcess id="Ev_1" ordering="Parallel">'
            '<userTask id="IT_1" name="inner"/>'
            '<completionCondition xsi:type="tFormalExpression" '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">${done == true}</completionCondition>'
            '</adHocSubProcess><endEvent id="End_1"/>'
            '<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>'
            '<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>'))
    if name == "Service Task (Behavior)":
        # The real shape, learned from the engine's refusals: the delegate
        # expression is on the do-not-rename list and `autonateServiceKind` is
        # REQUIRED.
        #
        # `autonate.noop` WAS WRONG and this is the correction (#535). The comment
        # here said it "is a behaviour that actually exists"; nothing registers it
        # -- not the app, not a fixture -- and the probe's own recorded result says
        # so: the callback to /api/workflow-behaviors/autonate.noop/execute
        # returned HTTP 404. `autonate.unlock-account` is registered
        # unconditionally by Program.cs, and with a `userId` that resolves to
        # nobody it reports `unlockResult = userNotFound`, which only a behaviour
        # that really ran can produce.
        return wrap(key, "", shell.format(
            elem='<serviceTask id="Ev_1" name="behave" '
                 'flowable:delegateExpression="${autonateBehaviorDelegate}" '
                 'flowable:autonateServiceKind="behavior" '
                 'flowable:behaviorKey="autonate.unlock-account"/>'))
    if name == "Call Activity":
        # Needs a callee, which is a second definition in the same deployment.
        return f"""<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
             xmlns:flowable="http://flowable.org/bpmn"
             xmlns:autonate="http://autonate.dev/workflows"
             targetNamespace="http://autonate.dev/probe">
  <process id="{key}c" name="callee" isExecutable="true">
    <startEvent id="CS_1"/><userTask id="CT_1" name="inner"/><endEvent id="CE_1"/>
    <sequenceFlow id="cf1" sourceRef="CS_1" targetRef="CT_1"/>
    <sequenceFlow id="cf2" sourceRef="CT_1" targetRef="CE_1"/>
  </process>
  <process id="{key}" name="probe" isExecutable="true">
    <startEvent id="Start_1"/>
    <callActivity id="Ev_1" name="call" calledElement="{key}c"/>
    <endEvent id="End_1"/>
    <sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>
    <sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>
  </process>
</definitions>"""
    return None


def main():
    evidence = json.load(open("../../src/shared/bpmn-execution-evidence.json"))
    created, results = [], []

    for row in evidence["elements"]:
        effect = row["declaredEffect"]
        if not effect:
            continue
        key = "px" + hex(abs(hash(row["name"])))[2:10]
        xml = diagram(row["name"], key)
        if xml is None:
            results.append({**row, "verdict": "no-diagram",
                            "detail": "the probe has no minimal diagram for this element yet"})
            print(f"SKIP  {row['name']:34} no diagram", flush=True)
            continue

        deployment, error = deploy(key, xml)
        if not deployment:
            results.append({**row, "verdict": "deploy-refused", "detail": error})
            print(f"DEPL! {row['name']:34} {error[:90]}", flush=True)
            continue
        created.append(deployment)

        status, started = call("POST", "/runtime/process-instances", {"processDefinitionKey": key})
        if status not in (200, 201) or not isinstance(started, dict):
            results.append({**row, "verdict": "start-refused", "detail": str(started)[:200]})
            print(f"STRT! {row['name']:34} {str(started)[:90]}", flush=True)
            continue

        time.sleep(0.4)                       # async jobs (timers) need a beat
        proved, detail = observe(effect, started["id"], "Ev_1")
        results.append({**row, "verdict": "proved" if proved else "NOT-PROVED", "detail": detail})
        print(f"{'OK   ' if proved else 'FAIL '}{row['name']:34} {effect:17} {detail[:70]}", flush=True)

    for deployment in created:
        call("DELETE", f"/repository/deployments/{deployment}?cascade=true")
    print(f"\ncleaned up {len(created)} deployments by id", file=sys.stderr)

    json.dump(results, open("execution-results.json", "w"), indent=1)
    bad = [r for r in results if r["verdict"] != "proved"]
    print(f"\n{len(results)} elements attempted, {len(results) - len(bad)} PROVED, {len(bad)} not:", file=sys.stderr)
    for r in bad:
        print(f"  {r['verdict']:15} {r['name']}", file=sys.stderr)


if __name__ == "__main__":
    main()
