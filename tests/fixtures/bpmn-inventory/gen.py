NS = ('xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" '
      'xmlns:flowable="http://flowable.org/bpmn" '
      'xmlns:autonate="http://autonate.dev/workflows" '
      'targetNamespace="http://autonate.dev/workflows"')

def wrap(key, inner, extra_defs="", proc_attrs=""):
    return (f'<?xml version="1.0" encoding="UTF-8"?>\n<definitions {NS}>\n'
            f'{extra_defs}<process id="{key}" name="{key}" isExecutable="true"{proc_attrs}>\n{inner}\n'
            f'</process>\n</definitions>\n')

def sf(i, a, b, extra=""):  return f'<sequenceFlow id="f{i}" sourceRef="{a}" targetRef="{b}"{extra}/>'
S, E = '<startEvent id="s"/>', '<endEvent id="e"/>'
TASK = '<userTask id="t" name="T"/>'

def linear(key, mid, extra_defs="", proc_attrs=""):
    """start -> <mid element id=m> -> end"""
    return wrap(key, f'  {S}\n  {sf(1,"s","m")}\n  {mid}\n  {sf(2,"m","e")}\n  {E}', extra_defs, proc_attrs)

def with_boundary(key, bnd_def, cancel="true", extra_defs=""):
    """A user task hosting a boundary event; the boundary path reaches its own end."""
    inner = (f'  {S}\n  {sf(1,"s","t")}\n  {TASK}\n  {sf(2,"t","e")}\n  {E}\n'
             f'  <boundaryEvent id="m" attachedToRef="t" cancelActivity="{cancel}">{bnd_def}</boundaryEvent>\n'
             f'  {sf(3,"m","e2")}\n  <endEvent id="e2"/>')
    return wrap(key, inner, extra_defs)

SIGDEF  = '<signal id="Sig1" name="sig1"/>\n'
MSGDEF  = '<message id="Msg1" name="msg1"/>\n'
ERRDEF  = '<error id="Err1" errorCode="E1"/>\n'
ESCDEF  = '<escalation id="Esc1" escalationCode="X1"/>\n'
ITEMDEF = '<itemDefinition id="Item1"/>\n'

B = {}
# --- supported ---
B["start_none"]      = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}')
B["start_signal"]    = lambda k: wrap(k, f'  <startEvent id="s"><signalEventDefinition signalRef="Sig1"/></startEvent>\n  {sf(1,"s","e")}\n  {E}', SIGDEF)
B["start_timer"]     = lambda k: wrap(k, f'  <startEvent id="s"><timerEventDefinition><timeDuration>PT1H</timeDuration></timerEventDefinition></startEvent>\n  {sf(1,"s","e")}\n  {E}')
B["catch_timer"]     = lambda k: linear(k, '<intermediateCatchEvent id="m"><timerEventDefinition><timeDuration>PT1M</timeDuration></timerEventDefinition></intermediateCatchEvent>')
B["end_none"]        = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}')
B["end_terminate"]   = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><terminateEventDefinition/></endEvent>')
B["task_generic"]    = lambda k: linear(k, '<task id="m" name="T"/>')
B["task_user"]       = lambda k: linear(k, '<userTask id="m" name="T"/>')
B["task_script"]     = lambda k: linear(k, '<scriptTask id="m" name="T" scriptFormat="javascript"><script>variables.set("x",1);</script></scriptTask>')
B["task_service"]    = lambda k: linear(k, '<serviceTask id="m" name="T" flowable:delegateExpression="${autonateBehaviorDelegate}" flowable:autonateServiceKind="behavior" flowable:behaviorKey="autonate.unlock-account"/>')
B["gw_exclusive"]    = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <exclusiveGateway id="m"/>\n  {sf(2,"m","e")}\n  {E}')
B["gw_inclusive"]    = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <inclusiveGateway id="m"/>\n  {sf(2,"m","e")}\n  {E}')
B["gw_parallel"]     = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <parallelGateway id="m"/>\n  {sf(2,"m","e")}\n  {E}')
B["seq_flow"]        = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}')

# --- start events (coming soon) ---
B["start_message"]     = lambda k: wrap(k, f'  <startEvent id="s"><messageEventDefinition messageRef="Msg1"/></startEvent>\n  {sf(1,"s","e")}\n  {E}', MSGDEF)
B["start_conditional"] = lambda k: wrap(k, f'  <startEvent id="s"><conditionalEventDefinition><condition>${{ok}}</condition></conditionalEventDefinition></startEvent>\n  {sf(1,"s","e")}\n  {E}')
# error/escalation/compensation starts are only legal inside an event subprocess
def _evsub(k, startdef, defs=""):
    inner = (f'  {S}\n  {sf(1,"s","t")}\n  {TASK}\n  {sf(2,"t","e")}\n  {E}\n'
             f'  <subProcess id="m" triggeredByEvent="true">\n'
             f'    <startEvent id="es">{startdef}</startEvent>\n'
             f'    <sequenceFlow id="fx" sourceRef="es" targetRef="ee"/>\n    <endEvent id="ee"/>\n'
             f'  </subProcess>')
    return wrap(k, inner, defs)
B["start_error"]        = lambda k: _evsub(k, '<errorEventDefinition errorRef="Err1"/>', ERRDEF)
B["start_escalation"]   = lambda k: _evsub(k, '<escalationEventDefinition escalationRef="Esc1"/>', ESCDEF)
B["start_compensation"] = lambda k: _evsub(k, '<compensateEventDefinition/>')
# --- intermediate throw ---
B["throw_none"]         = lambda k: linear(k, '<intermediateThrowEvent id="m"/>')
B["throw_message"]      = lambda k: linear(k, '<intermediateThrowEvent id="m"><messageEventDefinition messageRef="Msg1"/></intermediateThrowEvent>', MSGDEF)
B["throw_signal"]       = lambda k: linear(k, '<intermediateThrowEvent id="m"><signalEventDefinition signalRef="Sig1"/></intermediateThrowEvent>', SIGDEF)
B["throw_escalation"]   = lambda k: linear(k, '<intermediateThrowEvent id="m"><escalationEventDefinition escalationRef="Esc1"/></intermediateThrowEvent>', ESCDEF)
B["throw_link"]         = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <intermediateThrowEvent id="m"><linkEventDefinition name="L1"/></intermediateThrowEvent>\n  <intermediateCatchEvent id="c"><linkEventDefinition name="L1"/></intermediateCatchEvent>\n  {sf(2,"c","e")}\n  {E}')
B["throw_compensation"] = lambda k: linear(k, '<intermediateThrowEvent id="m"><compensateEventDefinition/></intermediateThrowEvent>')
# --- intermediate catch ---
B["catch_message"]      = lambda k: linear(k, '<intermediateCatchEvent id="m"><messageEventDefinition messageRef="Msg1"/></intermediateCatchEvent>', MSGDEF)
B["catch_signal"]       = lambda k: linear(k, '<intermediateCatchEvent id="m"><signalEventDefinition signalRef="Sig1"/></intermediateCatchEvent>', SIGDEF)
B["catch_conditional"]  = lambda k: linear(k, '<intermediateCatchEvent id="m"><conditionalEventDefinition><condition>${ok}</condition></conditionalEventDefinition></intermediateCatchEvent>')
B["catch_link"]         = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","th")}\n  <intermediateThrowEvent id="th"><linkEventDefinition name="L1"/></intermediateThrowEvent>\n  <intermediateCatchEvent id="m"><linkEventDefinition name="L1"/></intermediateCatchEvent>\n  {sf(2,"m","e")}\n  {E}')
# --- boundary events ---
B["bnd_message"]     = lambda k: with_boundary(k, '<messageEventDefinition messageRef="Msg1"/>', "true", MSGDEF)
B["bnd_timer"]       = lambda k: with_boundary(k, '<timerEventDefinition><timeDuration>PT1M</timeDuration></timerEventDefinition>')
B["bnd_signal"]      = lambda k: with_boundary(k, '<signalEventDefinition signalRef="Sig1"/>', "true", SIGDEF)
B["bnd_conditional"] = lambda k: with_boundary(k, '<conditionalEventDefinition><condition>${ok}</condition></conditionalEventDefinition>')
B["bnd_error"]       = lambda k: with_boundary(k, '<errorEventDefinition errorRef="Err1"/>', "true", ERRDEF)
B["bnd_escalation"]  = lambda k: with_boundary(k, '<escalationEventDefinition escalationRef="Esc1"/>', "false", ESCDEF)
B["bnd_compensation"]= lambda k: with_boundary(k, '<compensateEventDefinition/>', "false")
# cancel boundary is only legal on a transaction
B["bnd_cancel"]      = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","tx")}\n'
    f'  <transaction id="tx">\n    <startEvent id="ts"/>\n    <sequenceFlow id="tf" sourceRef="ts" targetRef="te"/>\n    <endEvent id="te"><cancelEventDefinition/></endEvent>\n  </transaction>\n'
    f'  {sf(2,"tx","e")}\n  {E}\n'
    f'  <boundaryEvent id="m" attachedToRef="tx"><cancelEventDefinition/></boundaryEvent>\n  {sf(3,"m","e2")}\n  <endEvent id="e2"/>')
# --- end events ---
B["end_message"]     = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><messageEventDefinition messageRef="Msg1"/></endEvent>', MSGDEF)
B["end_signal"]      = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><signalEventDefinition signalRef="Sig1"/></endEvent>', SIGDEF)
B["end_error"]       = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><errorEventDefinition errorRef="Err1"/></endEvent>', ERRDEF)
B["end_escalation"]  = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><escalationEventDefinition escalationRef="Esc1"/></endEvent>', ESCDEF)
B["end_cancel"]      = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","tx")}\n'
    f'  <transaction id="tx">\n    <startEvent id="ts"/>\n    <sequenceFlow id="tf" sourceRef="ts" targetRef="m"/>\n    <endEvent id="m"><cancelEventDefinition/></endEvent>\n  </transaction>\n'
    f'  {sf(2,"tx","e")}\n  {E}\n  <boundaryEvent id="cb" attachedToRef="tx"><cancelEventDefinition/></boundaryEvent>\n  {sf(3,"cb","e2")}\n  <endEvent id="e2"/>')
B["end_compensation"]= lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  <endEvent id="e"><compensateEventDefinition/></endEvent>')
# --- tasks ---
B["task_send"]        = lambda k: linear(k, '<sendTask id="m" name="T"/>')
B["task_receive"]     = lambda k: linear(k, '<receiveTask id="m" name="T"/>')
B["task_manual"]      = lambda k: linear(k, '<manualTask id="m" name="T"/>')
B["task_businessrule"]= lambda k: linear(k, '<businessRuleTask id="m" name="T"/>')
B["task_call"]        = lambda k: linear(k, '<callActivity id="m" name="T" calledElement="someOtherProcess"/>')
# --- subprocesses ---
B["sub_embedded"]    = lambda k: linear(k, '<subProcess id="m"><startEvent id="ss"/><sequenceFlow id="sf1" sourceRef="ss" targetRef="se"/><endEvent id="se"/></subProcess>')
B["sub_event"]       = lambda k: _evsub(k, '<signalEventDefinition signalRef="Sig1"/>', SIGDEF)
B["sub_transaction"] = lambda k: linear(k, '<transaction id="m"><startEvent id="ts"/><sequenceFlow id="tf" sourceRef="ts" targetRef="te"/><endEvent id="te"/></transaction>')
B["sub_adhoc"]       = lambda k: linear(k, '<adHocSubProcess id="m" ordering="Parallel"><userTask id="at" name="A"/><completionCondition>${true}</completionCondition></adHocSubProcess>')
# --- gateways ---
B["gw_eventbased"]   = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <eventBasedGateway id="m"/>\n'
    f'  {sf(2,"m","c1")}\n  <intermediateCatchEvent id="c1"><timerEventDefinition><timeDuration>PT1M</timeDuration></timerEventDefinition></intermediateCatchEvent>\n'
    f'  {sf(3,"c1","e")}\n  {E}')
B["gw_complex"]      = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","m")}\n  <complexGateway id="m"/>\n  {sf(2,"m","e")}\n  {E}')
# --- activity markers ---
B["mk_loop"]           = lambda k: linear(k, '<userTask id="m" name="T"><standardLoopCharacteristics><loopCondition>${false}</loopCondition></standardLoopCharacteristics></userTask>')
B["mk_mi_parallel"]    = lambda k: linear(k, '<userTask id="m" name="T"><multiInstanceLoopCharacteristics isSequential="false"><loopCardinality>2</loopCardinality></multiInstanceLoopCharacteristics></userTask>')
B["mk_mi_sequential"]  = lambda k: linear(k, '<userTask id="m" name="T"><multiInstanceLoopCharacteristics isSequential="true"><loopCardinality>2</loopCardinality></multiInstanceLoopCharacteristics></userTask>')
B["mk_compensation"]   = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","t")}\n  {TASK}\n  {sf(2,"t","e")}\n  {E}\n'
    f'  <boundaryEvent id="cb" attachedToRef="t"><compensateEventDefinition/></boundaryEvent>\n'
    f'  <userTask id="m" name="Undo" isForCompensation="true"/>\n'
    f'  <association id="a1" sourceRef="cb" targetRef="m" associationDirection="One"/>')
# --- collaboration ---
B["col_pool"] = lambda k: ('<?xml version="1.0" encoding="UTF-8"?>\n<definitions ' + NS + '>\n'
    f'<collaboration id="c"><participant id="p1" name="P" processRef="{k}"/></collaboration>\n'
    f'<process id="{k}" name="{k}" isExecutable="true">\n  {S}\n  {sf(1,"s","e")}\n  {E}\n</process>\n</definitions>\n')
B["col_lane"] = lambda k: wrap(k, f'  <laneSet id="ls"><lane id="l1" name="L"><flowNodeRef>s</flowNodeRef><flowNodeRef>e</flowNodeRef></lane></laneSet>\n  {S}\n  {sf(1,"s","e")}\n  {E}')
B["col_messageflow"] = lambda k: ('<?xml version="1.0" encoding="UTF-8"?>\n<definitions ' + NS + '>\n' + MSGDEF +
    f'<collaboration id="c"><participant id="p1" name="A" processRef="{k}"/><participant id="p2" name="B"/>'
    f'<messageFlow id="mf" sourceRef="p1" targetRef="p2" messageRef="Msg1"/></collaboration>\n'
    f'<process id="{k}" name="{k}" isExecutable="true">\n  {S}\n  {sf(1,"s","e")}\n  {E}\n</process>\n</definitions>\n')
# --- data ---
B["data_object"] = lambda k: wrap(k, f'  <dataObject id="do1" name="D" itemSubjectRef="Item1"/>\n  <dataObjectReference id="m" name="D" dataObjectRef="do1"/>\n  {S}\n  {sf(1,"s","e")}\n  {E}', ITEMDEF)
B["data_store"]  = lambda k: ('<?xml version="1.0" encoding="UTF-8"?>\n<definitions ' + NS + '>\n'
    '<dataStore id="ds1" name="DS"/>\n'
    f'<process id="{k}" name="{k}" isExecutable="true">\n  <dataStoreReference id="m" name="DS" dataStoreRef="ds1"/>\n  {S}\n  {sf(1,"s","e")}\n  {E}\n</process>\n</definitions>\n')
B["data_input"]  = lambda k: wrap(k, f'  <ioSpecification id="io"><dataInput id="m" name="In"/><inputSet id="is"><dataInputRefs>m</dataInputRefs></inputSet><outputSet id="os"/></ioSpecification>\n  {S}\n  {sf(1,"s","e")}\n  {E}')
B["data_output"] = lambda k: wrap(k, f'  <ioSpecification id="io"><dataOutput id="m" name="Out"/><inputSet id="is"/><outputSet id="os"><dataOutputRefs>m</dataOutputRefs></outputSet></ioSpecification>\n  {S}\n  {sf(1,"s","e")}\n  {E}')
# --- artifacts ---
B["art_annotation"]  = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}\n  <textAnnotation id="m"><text>note</text></textAnnotation>')
B["art_group"]       = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}\n  <group id="m"/>')
B["art_association"] = lambda k: wrap(k, f'  {S}\n  {sf(1,"s","e")}\n  {E}\n  <textAnnotation id="ta"><text>note</text></textAnnotation>\n  <association id="m" sourceRef="s" targetRef="ta" associationDirection="None"/>')

import elements
missing=[key for _,key in elements.SUPPORTED+elements.COMING_SOON if key not in B]
print("  builders:", len(B), " missing:", missing or "none")

