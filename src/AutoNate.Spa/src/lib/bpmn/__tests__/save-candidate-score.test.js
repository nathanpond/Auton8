import { describe, it, expect } from "vitest";
import { scoreBpmnXml } from "../workflow.js";

/**
 * The save picks the best of several XML candidates by score (#171). Two of
 * the candidates rebuild the process from its flow elements and drop lanes,
 * pools and message flows on the floor -- so the scorer has to count those,
 * or the rebuild ties the real output and a lane the author drew is gone on
 * save. Measured before the fix: a two-lane pool saved with no laneSet.
 */
const flowOnly = `
  <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
    <bpmn:process id="p">
      <bpmn:startEvent id="s"/><bpmn:userTask id="t"/><bpmn:endEvent id="e"/>
      <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t"/>
      <bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e"/>
    </bpmn:process>
  </bpmn:definitions>`;

const withLanes = flowOnly.replace(
  '<bpmn:process id="p">',
  '<bpmn:collaboration id="c"><bpmn:participant id="pool" processRef="p"/></bpmn:collaboration><bpmn:process id="p">'
  + '<bpmn:laneSet id="ls"><bpmn:lane id="l1"><bpmn:flowNodeRef>t</bpmn:flowNodeRef></bpmn:lane><bpmn:lane id="l2"/></bpmn:laneSet>'
);

describe("the save candidate score", () => {
  it("ranks the same diagram higher when it still carries its lanes and pool", () => {
    expect(scoreBpmnXml(withLanes)).toBeGreaterThan(scoreBpmnXml(flowOnly));
  });

  it("scores a lane-less rebuild exactly as the flow elements it kept, so a tie cannot favour it", () => {
    // The two candidates that drop lanes score the flow content only; the one
    // that kept everything wins by the structure it kept, never by chance.
    const structure = scoreBpmnXml(withLanes) - scoreBpmnXml(flowOnly);
    // collaboration, participant, laneSet, two lanes: five constructs, ten each.
    expect(structure).toBe(50);
  });

  it("scores nothing for an empty candidate", () => {
    expect(scoreBpmnXml("")).toBe(0);
    expect(scoreBpmnXml(null)).toBe(0);
  });
});
