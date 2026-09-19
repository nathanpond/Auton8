import { describe, it, expect } from "vitest";
import {
  describeFreshness,
  freshnessMessage,
  type ExecutionFreshness
} from "../executionFreshness";

const NOW = Date.parse("2026-09-19T12:00:00.000Z");

function payload(over: Partial<ExecutionFreshness> = {}): ExecutionFreshness {
  return {
    asOfUtc: "2026-09-19T11:59:55.000Z",
    lastPolledAtUtc: "2026-09-19T11:59:55.000Z",
    isUpdating: true,
    freshnessTargetSeconds: 30,
    pollIntervalSeconds: 60,
    ...over
  };
}

describe("describeFreshness", () => {
  it("reports fresh when the feed is updating and the data is inside the target", () => {
    expect(describeFreshness(payload(), NOW).state).toBe("fresh");
  });

  it("reports stale when the data is older than the target but the feed is updating", () => {
    const view = describeFreshness(
      payload({ asOfUtc: "2026-09-19T11:58:00.000Z" }),
      NOW
    );
    expect(view.state).toBe("stale");
    expect(view.ageSeconds).toBe(120);
  });

  // The distinction this story exists for. Collapsing these two would tell an
  // operator with a stalled feed that the system is merely a little behind.
  it("reports not-updating separately from stale, even when the rows are recent", () => {
    const view = describeFreshness(payload({ isUpdating: false }), NOW);
    expect(view.state).toBe("not-updating");
    // The data itself is five seconds old -- inside the target. Only the feed
    // has stopped, and that is what must surface.
    expect(view.ageSeconds).toBe(5);
  });

  it("prefers not-updating over stale when both are true", () => {
    const view = describeFreshness(
      payload({ isUpdating: false, asOfUtc: "2026-09-19T11:00:00.000Z" }),
      NOW
    );
    expect(view.state).toBe("not-updating");
  });

  // Reporting "fresh" before the first response would make the moment before
  // anything is known look like the best case.
  it("reports not-updating when nothing has loaded yet", () => {
    const view = describeFreshness(undefined, NOW);
    expect(view.state).toBe("not-updating");
    expect(view.ageSeconds).toBeNull();
  });

  it("carries a null age through when the server has no rows to date", () => {
    const view = describeFreshness(payload({ asOfUtc: null }), NOW);
    expect(view.ageSeconds).toBeNull();
    expect(view.state).toBe("fresh");
  });
});

describe("the refetch interval", () => {
  // AC6: nothing polls more aggressively than the configured interval just to
  // make the indicator look better. The number comes from the server, so the
  // client cannot choose to ask more often.
  it("takes the server's poll interval rather than a client-chosen one", () => {
    expect(describeFreshness(payload({ pollIntervalSeconds: 60 }), NOW).refetchIntervalMs)
      .toBe(60_000);
    expect(describeFreshness(payload({ pollIntervalSeconds: 300 }), NOW).refetchIntervalMs)
      .toBe(300_000);
  });

  // The complement: a server reporting an absurdly small interval -- a
  // misconfiguration, or a future change -- must not turn this into a busy loop.
  it("never polls faster than the floor, whatever the server says", () => {
    expect(describeFreshness(payload({ pollIntervalSeconds: 0 }), NOW).refetchIntervalMs)
      .toBe(5_000);
    expect(describeFreshness(payload({ pollIntervalSeconds: -10 }), NOW).refetchIntervalMs)
      .toBe(5_000);
  });
});

describe("freshnessMessage", () => {
  // AC2: a view older than the target "says so distinctly, rather than only
  // showing a timestamp a user has to interpret".
  it("gives each state its own wording rather than a bare timestamp", () => {
    const fresh = freshnessMessage(describeFreshness(payload(), NOW));
    const stale = freshnessMessage(
      describeFreshness(payload({ asOfUtc: "2026-09-19T11:58:00.000Z" }), NOW)
    );
    const stopped = freshnessMessage(describeFreshness(payload({ isUpdating: false }), NOW));

    expect(fresh).toMatch(/up to date/i);
    expect(stale).toMatch(/showing data from/i);
    expect(stopped).toMatch(/not updating/i);

    // And they are actually different sentences -- a single message reused
    // across states would satisfy each assertion above on its own.
    expect(new Set([fresh, stale, stopped]).size).toBe(3);
  });

  it("reads in minutes and hours rather than raw seconds", () => {
    expect(
      freshnessMessage(describeFreshness(payload({ asOfUtc: "2026-09-19T10:00:00.000Z" }), NOW))
    ).toMatch(/2 hours/);
    expect(
      freshnessMessage(describeFreshness(payload({ asOfUtc: "2026-09-19T11:55:00.000Z" }), NOW))
    ).toMatch(/5 minutes/);
  });
});
