import { useCallback, useEffect, useRef, useState } from "react";

// One step in the drill stack — the user clicked `label` (a bucket name
// rendered on the chart) which corresponds to filtering `fieldKey ==
// value` on the underlying data. We keep both `value` (for the filter
// clause sent to the backend) and `label` (for the breadcrumb) so the
// runtime never has to re-derive one from the other.
export type DrillStep = {
  groupBy: string;
  // fieldKey is the backend-visible filter field. Equals groupBy for
  // built-ins like "status"; for custom fields, the "field:" prefix is
  // already stripped by `groupByToFilterClause`.
  fieldKey: string;
  value: unknown;
  label: string;
};

export type DrillState = {
  path: DrillStep[];
  // Current group-by axis. `initial` while the path is empty; otherwise
  // the next unused entry in `drillBy`. Null once the chain is exhausted —
  // the chart still renders the last drilled bucket but clicks are noop.
  currentGroupBy: string | null;
  // True when the user can still drill further from the current view
  // (path is shorter than the chain and a next axis exists).
  canDrill: boolean;
  push: (step: DrillStep) => void;
  popTo: (index: number) => void;
  reset: () => void;
};

// `resetKey` is a stable string the caller derives from the widget config
// (data source + initial group-by + drillBy + chart type). When it
// changes, the path is dropped — drilling from a stale hierarchy can't
// be expressed in the new one, so showing it would lie about the data.
export function useDrillState(
  initial: string,
  drillBy: string[],
  resetKey: string
): DrillState {
  const [path, setPath] = useState<DrillStep[]>([]);
  const prevResetKey = useRef(resetKey);

  // Sync reset on key change via useEffect so reads during the same
  // render see the fresh path (rather than the one written below).
  useEffect(() => {
    if (prevResetKey.current !== resetKey) {
      prevResetKey.current = resetKey;
      setPath([]);
    }
  }, [resetKey]);

  const push = useCallback((step: DrillStep) => {
    setPath((prev) => [...prev, step]);
  }, []);

  const popTo = useCallback((index: number) => {
    setPath((prev) => (index < 0 ? [] : prev.slice(0, index)));
  }, []);

  const reset = useCallback(() => setPath([]), []);

  // currentGroupBy: position 0 = initial; positions 1..N = drillBy[i-1].
  // Past the chain end → null (drilled all the way, can't go further).
  const currentGroupBy =
    path.length === 0 ? initial : path.length - 1 < drillBy.length ? drillBy[path.length - 1] : null;

  const canDrill = path.length < drillBy.length && currentGroupBy !== null;

  return { path, currentGroupBy, canDrill, push, popTo, reset };
}
