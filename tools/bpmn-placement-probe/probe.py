#!/usr/bin/env python3
"""Ask Flowable what it actually accepts, per (position, definition, container).

AC1 of #324: "Verify against the running engine FIRST and record it on this
issue: for each element and each container, what Flowable actually does. The
four previous rules were each written from reasoning and each was wrong about a
cell."

So this writes nothing down from reasoning. It generates a minimal BPMN document
per cell, deploys it, records the verdict and the validation code, and deletes
the deployment BY ID -- never by prefix, never a wildcard: this engine is shared.
"""
import base64, itertools, json, sys, urllib.error, urllib.request

BASE = "http://localhost:8080/flowable-rest/service"
AUTH = base64.b64encode(b"rest-admin:test").decode()

# (name, root declaration, eventDefinition child)
DEFINITIONS = [
    ("none", "", ""),
    ("message", '<message id="Msg_1" name="m"/>', '<messageEventDefinition messageRef="Msg_1"/>'),
    ("timer", "", '<timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition>'),
    ("signal", '<signal id="Sig_1" name="s"/>', '<signalEventDefinition signalRef="Sig_1"/>'),
    ("conditional", "", '<conditionalEventDefinition><condition>${ok}</condition></conditionalEventDefinition>'),
    ("error", '<error id="Err_1" name="e" errorCode="E"/>', '<errorEventDefinition errorRef="Err_1"/>'),
    ("escalation", '<escalation id="Esc_1" name="x" escalationCode="X"/>', '<escalationEventDefinition escalationRef="Esc_1"/>'),
    ("compensate", "", '<compensateEventDefinition/>'),
    ("terminate", "", '<terminateEventDefinition/>'),
    ("link", "", '<linkEventDefinition name="L"/>'),
]

POSITIONS = ["start", "intermediateCatch", "intermediateThrow", "boundary", "end"]
CONTAINERS = ["process", "eventSubProcess", "embeddedSubProcess", "adHocSubProcess", "transaction"]


def element_for(position, definition_xml, attached_to):
    """The element under test, in the position under test."""
    if position == "start":
        return f'<startEvent id="Ev_1">{definition_xml}</startEvent>'
    if position == "intermediateCatch":
        return f'<intermediateCatchEvent id="Ev_1">{definition_xml}</intermediateCatchEvent>'
    if position == "intermediateThrow":
        return f'<intermediateThrowEvent id="Ev_1">{definition_xml}</intermediateThrowEvent>'
    if position == "boundary":
        return (f'<boundaryEvent id="Ev_1" attachedToRef="{attached_to}">'
                f'{definition_xml}</boundaryEvent>')
    if position == "end":
        return f'<endEvent id="Ev_1">{definition_xml}</endEvent>'
    raise ValueError(position)


def document(position, name, root_xml, definition_xml, container):
    """A minimal, otherwise-valid process carrying exactly one element under test."""
    # The host activity a boundary event attaches to, and the thing that keeps
    # every container non-empty so an unrelated "must have a start event" or
    # "is empty" refusal cannot be mistaken for our cell's verdict.
    host = '<userTask id="Host_1" name="host"/>'

    if container == "process":
        # At process level the element sits directly in the process, beside a
        # start event so the process is startable when our element is not one.
        extra = "" if position == "start" else '<startEvent id="Start_0"/>'
        body = f"{extra}{host}{element_for(position, definition_xml, 'Host_1')}"
        inner = ""
    else:
        tag = {
            "eventSubProcess": '<subProcess id="Sub_1" triggeredByEvent="true">',
            "embeddedSubProcess": '<subProcess id="Sub_1">',
            "adHocSubProcess": '<adHocSubProcess id="Sub_1">',
            "transaction": '<transaction id="Sub_1">',
        }[container]
        close = "</adHocSubProcess>" if container == "adHocSubProcess" else (
            "</transaction>" if container == "transaction" else "</subProcess>")
        # Inside a container the element under test sits with a host activity;
        # a non-start container also needs its own start event.
        needs_start = position != "start"
        inner_start = '<startEvent id="Start_1"/>' if needs_start else ""
        inner = (f"{tag}{inner_start}{host}"
                 f"{element_for(position, definition_xml, 'Host_1')}{close}")
        body = f'<startEvent id="Start_0"/>{inner}'

    return f"""<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
             xmlns:flowable="http://flowable.org/bpmn"
             targetNamespace="http://autonate.dev/probe">
  {root_xml}
  <process id="Probe_1" name="probe" isExecutable="true">
    {body}
  </process>
</definitions>"""


def deploy(name, xml):
    boundary = "----probe"
    part = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{name}.bpmn20.xml"\r\n'
        "Content-Type: text/xml\r\n\r\n"
        f"{xml}\r\n"
        f"--{boundary}--\r\n"
    ).encode()
    request = urllib.request.Request(
        f"{BASE}/repository/deployments", data=part, method="POST",
        headers={"Authorization": f"Basic {AUTH}",
                 "Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return True, json.loads(response.read()).get("id"), ""
    except urllib.error.HTTPError as error:
        return False, None, error.read().decode("utf-8", "replace")[:400]


def delete(deployment_id):
    """By id, only ever by id."""
    request = urllib.request.Request(
        f"{BASE}/repository/deployments/{deployment_id}?cascade=true",
        method="DELETE", headers={"Authorization": f"Basic {AUTH}"})
    try:
        urllib.request.urlopen(request, timeout=60).read()
        return True
    except Exception:
        return False


def code_of(body):
    marker = "Problem: '"
    at = body.find(marker)
    if at < 0:
        return "(no problem code)"
    return body[at + len(marker):].split("'")[0]


def main():
    rows, created = [], []
    for position, (name, root_xml, definition_xml), container in itertools.product(
            POSITIONS, DEFINITIONS, CONTAINERS):
        if position == "start" and name in ("terminate", "link"):
            continue                      # not constructible as a start event
        if position == "end" and name in ("timer", "conditional", "link"):
            continue                      # no end-event form in the schema
        xml = document(position, name, root_xml, definition_xml, container)
        ok, deployment_id, body = deploy(f"probe-{position}-{name}-{container}", xml)
        if deployment_id:
            created.append(deployment_id)
        rows.append({"position": position, "definition": name, "container": container,
                     "accepted": ok, "code": "" if ok else code_of(body)})
        print(f"{'OK  ' if ok else 'FAIL'} {position:18} {name:12} {container:20} "
              f"{'' if ok else code_of(body)}", flush=True)

    for deployment_id in created:
        delete(deployment_id)
    print(f"\ncleaned up {len(created)} deployments by id", file=sys.stderr)

    with open("placement-matrix.json", "w") as handle:
        json.dump(rows, handle, indent=1)
    print(f"{len(rows)} cells written to placement-matrix.json", file=sys.stderr)


if __name__ == "__main__":
    main()
