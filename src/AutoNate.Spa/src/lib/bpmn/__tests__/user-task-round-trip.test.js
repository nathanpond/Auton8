import { describe, it, expect } from "vitest";
import { updateUserTaskProperties, describeElementById } from "../workflow.js";
import { fakeModeler, businessObject, element } from "./fake-modeler.js";

/**
 * Assignment and form properties, written and read back (#323).
 *
 * All six travel `writeFlowableAttribute` and come back through
 * `readFlowableString` / `readFlowableList`. The list ones matter separately:
 * they are serialised to a comma string on the way in and split on the way out,
 * so a round trip is the only thing that proves the two halves agree.
 */
const task = {
  id: "u1",
  name: "Approve",
  assignee: "ana",
  candidateUsers: ["ben", "cat"],
  candidateGroups: ["reviewers"],
  dueDate: "2026-01-01T00:00:00Z",
  userFormMode: "modal",
  userFormShortCode: "approval-form"
};

function userTask(id = "u1") {
  return businessObject("bpmn:UserTask", id);
}

describe("user task properties round-trip", () => {
  it.each([
    ["assignee", "ana"],
    ["dueDate", "2026-01-01T00:00:00Z"],
    ["userFormMode", "modal"],
    ["userFormShortCode", "approval-form"]
  ])("writes %s and reads it back", (field, expected) => {
    const bo = userTask();
    const { handle } = fakeModeler([element(bo)]);

    updateUserTaskProperties(handle, task);

    expect(describeElementById(handle, "u1")[field]).toBe(expected);
  });

  it.each([
    ["candidateUsers", ["ben", "cat"]],
    ["candidateGroups", ["reviewers"]]
  ])("writes %s as a list and reads the whole list back", (field, expected) => {
    const bo = userTask();
    const { handle } = fakeModeler([element(bo)]);

    updateUserTaskProperties(handle, task);

    // The whole list, not just its presence: a serialiser that kept only the
    // first entry would pass a contains-check.
    expect(describeElementById(handle, "u1")[field]).toEqual(expected);
  });

  it("does not persist the simple form mode, which is the default", () => {
    const bo = userTask();
    const { handle } = fakeModeler([element(bo)]);

    updateUserTaskProperties(handle, { ...task, userFormMode: "simple" });

    const described = describeElementById(handle, "u1");
    expect(described.userFormMode).toBeNull();
    // And the short code goes with it -- a form reference with no form mode is
    // a value nothing reads.
    expect(described.userFormShortCode).toBeNull();
  });

  it("refuses an element that is not a user task rather than writing to it", () => {
    const bo = businessObject("bpmn:ServiceTask", "u1");
    const { handle } = fakeModeler([element(bo)]);

    expect(() => updateUserTaskProperties(handle, task)).toThrow(/no longer available/);
    expect(bo.$attrs["flowable:assignee"]).toBeUndefined();
  });
});
