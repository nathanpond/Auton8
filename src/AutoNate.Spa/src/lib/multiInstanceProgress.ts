// #173. What a collapsed multi-instance row says, as a pure function.
//
// Kept out of the component deliberately. The SPA's vitest runs under
// `environment: "node"` with no DOM and no testing-library, so anything living
// inside a component is reachable only from the E2E suite -- which needs a
// running engine and does not run on the merge gate. The wording and the
// arithmetic are the part most likely to go quietly wrong, so they live where a
// slim-tier test can reach them, and the E2E covers what only a browser can
// answer: focus, ARIA and the announcement actually reaching the tree.

import type { MultiInstanceProgress } from "@/types/flowable";

export type MultiInstanceSummary = {
  // "2 of 5 complete". The primary, always-present label.
  completionLabel: string;
  // 0-100, for the bar. The bar is decoration; the label is the fact.
  percent: number;
  // Sequential loops only: which run is in flight and how many have not started.
  // Null for a parallel loop, where "which one is running" is not a question --
  // they all are.
  sequentialLabel: string | null;
  // The attention flag, as WORDS. The AC requires that a failed or stalled
  // instance be distinguishable without expanding and NOT by colour alone, so
  // this string is the signal and any colour is the reinforcement.
  attentionLabel: string | null;
  // One sentence for assistive technology, carrying everything above. A screen
  // reader user gets no badge layout, so the parts are joined into prose rather
  // than left to be inferred from adjacency.
  announcement: string;
};

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function summarizeMultiInstance(
  progress: MultiInstanceProgress,
  activityLabel: string
): MultiInstanceSummary {
  const total = Math.max(progress.total, 0);
  const completed = clamp(progress.completed, 0, total);
  const active = clamp(progress.active, 0, total);
  const failed = clamp(progress.failed, 0, total);

  const completionLabel = `${completed} of ${total} complete`;
  const percent = total === 0 ? 0 : Math.round((completed / total) * 100);

  // Sequential runs one at a time, so the instance in flight is the one after
  // everything already finished. Stated only while something IS in flight --
  // once the loop is done there is no current instance, and "running 6 of 5"
  // is what the naive arithmetic produces.
  const notStarted = Math.max(total - completed - active, 0);
  const sequentialLabel =
    progress.isSequential && active > 0
      ? `Running instance ${completed + 1} of ${total}` +
        (notStarted > 0 ? ` · ${notStarted} not started` : "")
      : null;

  // Two distinguishable conditions, and neither is inferred from colour.
  //
  // "Stalled" is deliberately narrow: nothing running, not everything finished.
  // It is not a claim that the engine is wedged -- it is the observable fact,
  // worded as what was measured rather than as a diagnosis.
  let attentionLabel: string | null = null;
  if (failed > 0) {
    attentionLabel = failed === 1 ? "1 failed" : `${failed} failed`;
  } else if (active === 0 && completed < total) {
    attentionLabel = "none running";
  }

  const announcement = [
    `${activityLabel}: ${completionLabel}`,
    sequentialLabel,
    attentionLabel
  ]
    .filter((part): part is string => Boolean(part))
    .join(". ") + ".";

  return { completionLabel, percent, sequentialLabel, attentionLabel, announcement };
}
