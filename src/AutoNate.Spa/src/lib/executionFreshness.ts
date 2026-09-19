/**
 * How to describe the executions view's currency (#109).
 *
 * The decisions live here, away from the component, for a practical reason:
 * vitest runs with `environment: "node"` in this project, so a component that
 * computes its own state cannot be tested at all. A component that renders a
 * state someone else computed can be — and the computation is the part with
 * edges worth pinning.
 */

/** What the server reports (`GET /api/executions/freshness`, #594). */
export interface ExecutionFreshness {
  asOfUtc: string | null;
  lastPolledAtUtc: string | null;
  isUpdating: boolean;
  freshnessTargetSeconds: number;
  pollIntervalSeconds: number;
}

/**
 * Three states, not two.
 *
 * `not-updating` is deliberately separate from `stale`: "the data is a minute
 * old" and "the data has stopped arriving" are different things to someone
 * watching a process, and collapsing them tells an operator with a stalled feed
 * that the system is merely a little behind.
 */
export type FreshnessState = "fresh" | "stale" | "not-updating";

export interface FreshnessView {
  state: FreshnessState;
  /** Seconds since the data was current, or null when nothing is loaded. */
  ageSeconds: number | null;
  /** How often to re-ask. Never more often than the server polls — see below. */
  refetchIntervalMs: number;
}

/**
 * Lower bound on how often the client may re-ask, in seconds.
 *
 * A server that reported `pollIntervalSeconds: 0` — misconfiguration, or a
 * future change — would otherwise turn this into a busy loop against the API.
 */
const MIN_REFETCH_SECONDS = 5;

export function describeFreshness(
  freshness: ExecutionFreshness | undefined,
  nowMs: number
): FreshnessView {
  // Nothing loaded yet: say nothing rather than guess. Reporting "fresh" here
  // would make the moment before the first response look like the best case.
  if (!freshness) {
    return { state: "not-updating", ageSeconds: null, refetchIntervalMs: MIN_REFETCH_SECONDS * 1000 };
  }

  // THE INTERVAL COMES FROM THE SERVER, not from the client (AC6). The indicator
  // cannot be made to look fresher by asking more often, because asking more
  // often is not something this module will do.
  const refetchSeconds = Math.max(MIN_REFETCH_SECONDS, freshness.pollIntervalSeconds);
  const refetchIntervalMs = refetchSeconds * 1000;

  const ageSeconds =
    freshness.asOfUtc === null
      ? null
      : Math.max(0, Math.floor((nowMs - Date.parse(freshness.asOfUtc)) / 1000));

  // "Not updating" wins over "stale". A stalled feed whose rows happen to be
  // recent is still stalled, and that is the condition worth surfacing -- the
  // rows will not get any newer.
  if (!freshness.isUpdating) {
    return { state: "not-updating", ageSeconds, refetchIntervalMs };
  }

  if (ageSeconds !== null && ageSeconds > freshness.freshnessTargetSeconds) {
    return { state: "stale", ageSeconds, refetchIntervalMs };
  }

  return { state: "fresh", ageSeconds, refetchIntervalMs };
}

/**
 * The sentence a user reads. Distinct wording per state, because the second
 * criterion asks for a view older than the target to "say so distinctly, rather
 * than only showing a timestamp a user has to interpret".
 */
export function freshnessMessage(view: FreshnessView): string {
  switch (view.state) {
    case "not-updating":
      return view.ageSeconds === null
        ? "Not updating — no data has arrived yet."
        : `Not updating — this view stopped refreshing ${formatAge(view.ageSeconds)} ago.`;
    case "stale":
      return `Showing data from ${formatAge(view.ageSeconds ?? 0)} ago.`;
    default:
      return view.ageSeconds === null || view.ageSeconds < 5
        ? "Up to date."
        : `Up to date as of ${formatAge(view.ageSeconds)} ago.`;
  }
}

function formatAge(seconds: number): string {
  if (seconds < 60) return `${seconds} second${seconds === 1 ? "" : "s"}`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? "" : "s"}`;
  const hours = Math.floor(minutes / 60);
  return `${hours} hour${hours === 1 ? "" : "s"}`;
}
