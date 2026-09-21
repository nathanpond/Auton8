import { describe, it, expect } from "vitest";
import { summarizeMultiInstance } from "../multiInstanceProgress";
import type { MultiInstanceProgress } from "@/types/flowable";

const progress = (over: Partial<MultiInstanceProgress> = {}): MultiInstanceProgress => ({
  total: 5,
  completed: 2,
  active: 3,
  failed: 0,
  isSequential: false,
  ...over
});

describe("summarizeMultiInstance", () => {
  it("states the count as words, not only as a bar", () => {
    const s = summarizeMultiInstance(progress(), "Review");
    expect(s.completionLabel).toBe("2 of 5 complete");
    expect(s.percent).toBe(40);
  });

  // A parallel loop has no "current" instance -- they are all current. Saying
  // "running instance 3 of 5" there would be a confident wrong answer.
  it("offers no sequential position for a parallel loop", () => {
    expect(summarizeMultiInstance(progress(), "Review").sequentialLabel).toBeNull();
  });

  it("names the running instance and what has not started, for a sequential loop", () => {
    const s = summarizeMultiInstance(
      progress({ isSequential: true, completed: 2, active: 1 }),
      "Review"
    );
    expect(s.sequentialLabel).toBe("Running instance 3 of 5 · 2 not started");
  });

  // The complement: once the loop is done there IS no current instance, and the
  // naive arithmetic produces "running instance 6 of 5".
  it("names no running instance once a sequential loop has finished", () => {
    const s = summarizeMultiInstance(
      progress({ isSequential: true, completed: 5, active: 0 }),
      "Review"
    );
    expect(s.sequentialLabel).toBeNull();
    expect(s.completionLabel).toBe("5 of 5 complete");
  });

  // The AC: distinguishable without expanding, and not by colour alone. The
  // assertion is on the WORDS, because a colour change would satisfy a test
  // that only checked that something differed.
  it("flags a failure in words", () => {
    expect(summarizeMultiInstance(progress({ failed: 1 }), "Review").attentionLabel)
      .toBe("1 failed");
    expect(summarizeMultiInstance(progress({ failed: 3 }), "Review").attentionLabel)
      .toBe("3 failed");
  });

  it("flags an activity with nothing running and work left", () => {
    expect(
      summarizeMultiInstance(progress({ completed: 2, active: 0, failed: 0 }), "Review")
        .attentionLabel
    ).toBe("none running");
  });

  // The complement, and the one that matters most: a healthy in-flight loop must
  // raise NOTHING. An attention flag on every row is the same as no flag at all.
  it("flags nothing while a loop is running normally", () => {
    expect(summarizeMultiInstance(progress(), "Review").attentionLabel).toBeNull();
  });

  it("flags nothing for a loop that finished cleanly", () => {
    expect(
      summarizeMultiInstance(progress({ completed: 5, active: 0 }), "Review").attentionLabel
    ).toBeNull();
  });

  // A screen reader user gets no badges and no layout, so everything the sighted
  // reader assembles from adjacency has to be in one sentence.
  it("announces the activity, the count and the attention flag in one sentence", () => {
    const s = summarizeMultiInstance(
      progress({ isSequential: true, completed: 2, active: 1, failed: 1 }),
      "Review invoice"
    );
    expect(s.announcement).toBe(
      "Review invoice: 2 of 5 complete. Running instance 3 of 5 · 2 not started. 1 failed."
    );
  });

  // Nothing divides by zero, and nothing claims completion it cannot have. A
  // total of zero is what a loop over an empty collection reports.
  it("survives a loop with no instances", () => {
    const s = summarizeMultiInstance(
      { total: 0, completed: 0, active: 0, failed: 0, isSequential: false },
      "Review"
    );
    expect(s.percent).toBe(0);
    expect(s.completionLabel).toBe("0 of 0 complete");
    expect(s.attentionLabel).toBeNull();
  });
});
