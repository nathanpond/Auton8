import { describe, it, expect } from "vitest";
import { updateLaneProperties, describeElementById } from "../workflow.js";
import { fakeModeler, businessObject, moddleElement, element } from "./fake-modeler.js";

/**
 * A lane's group, written and read back (#171), and the lane a task reports
 * itself in -- by `flowNodeRef`, which is what the deployed copy reads, not by
 * where the shape happens to sit.
 */
function laneDiagram({ innerGroup = null } = {}) {
  const process = businessObject("bpmn:Process", "p1");
  const task = businessObject("bpmn:UserTask", "u1", { name: "Approve" });
  const loose = businessObject("bpmn:UserTask", "u2", { name: "Loose" });
  const lane = businessObject("bpmn:Lane", "l1", { name: "Finance" });
  lane.set("flowNodeRef", [task]);
  const laneSet = moddleElement("bpmn:LaneSet", { lanes: [lane] });
  process.set("laneSets", [laneSet]);
  task.$parent = process;
  loose.$parent = process;
  laneSet.$parent = process;
  lane.$parent = laneSet;

  let inner = null;
  if (innerGroup !== null) {
    // Nested: the outer lane lists the task too, as bpmn-js does for every lane
    // whose bounds contain the shape.
    inner = businessObject("bpmn:Lane", "l1a", { name: "Finance / Payables" });
    inner.set("flowNodeRef", [task]);
    inner.$attrs["autonate:groupId"] = innerGroup;
    const childLaneSet = moddleElement("bpmn:LaneSet", { lanes: [inner] });
    lane.set("childLaneSet", childLaneSet);
    childLaneSet.$parent = lane;
    inner.$parent = childLaneSet;
  }

  const elements = [element(task), element(loose), element(lane)];
  if (inner) elements.push(element(inner));
  const { handle } = fakeModeler(elements);
  return { handle, task, lane, inner };
}

describe("lane properties round-trip", () => {
  it("writes groupId and reads it back on the lane", () => {
    const { handle } = laneDiagram();
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    const described = describeElementById(handle, "l1");
    expect(described.laneGroupId).toBe("g-finance");
    expect(described.name).toBe("Finance");
  });

  it("clearing the group removes the attribute rather than writing an empty one", () => {
    const { handle, lane } = laneDiagram();
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: null });
    expect(describeElementById(handle, "l1").laneGroupId).toBeNull();
    // Absent, not "": publish reads presence, and an empty id would be a lane
    // pointing at a group called nothing.
    expect(Object.keys(lane.$attrs)).not.toContain("autonate:groupId");
  });

  it("a user task reports the lane that lists it, with the lane's group", () => {
    const { handle } = laneDiagram();
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    expect(describeElementById(handle, "u1").lane).toEqual({
      id: "l1",
      name: "Finance",
      groupId: "g-finance"
    });
  });

  it("a user task no lane lists reports no lane, however the canvas looks", () => {
    const { handle } = laneDiagram();
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    expect(describeElementById(handle, "u2").lane).toBeNull();
  });

  it("the innermost lane listing the task wins", () => {
    const { handle } = laneDiagram({ innerGroup: "g-payables" });
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    const described = describeElementById(handle, "u1");
    expect(described.lane.id).toBe("l1a");
    expect(described.lane.groupId).toBe("g-payables");
  });

  it("a group-less inner lane defers to the outer lane that names a group, as the deploy does (#656)", () => {
    const { handle } = laneDiagram({ innerGroup: "" });
    updateLaneProperties(handle, { id: "l1", name: "Finance", groupId: "g-finance" });
    const described = describeElementById(handle, "u1");
    // The task's assignment comes from Finance, so that is the lane reported --
    // not the innermost one, which contributes nothing.
    expect(described.lane.id).toBe("l1");
    expect(described.lane.groupId).toBe("g-finance");
  });

  it("when no lane names a group, the innermost lane is still reported so the note can say so", () => {
    const { handle } = laneDiagram({ innerGroup: "" });
    expect(describeElementById(handle, "u1").lane).toEqual({ id: "l1a", name: "Finance / Payables", groupId: null });
  });

  it("a node that is not in a lane carries no laneGroupId of its own", () => {
    const { handle } = laneDiagram();
    expect(describeElementById(handle, "u1").laneGroupId).toBeNull();
  });

  it("refuses an element that is not a lane rather than writing to it", () => {
    const { handle, task } = laneDiagram();
    expect(() => updateLaneProperties(handle, { id: "u1", name: "x", groupId: "g" })).toThrow(/no longer available/);
    expect(Object.keys(task.$attrs)).not.toContain("autonate:groupId");
  });
});
