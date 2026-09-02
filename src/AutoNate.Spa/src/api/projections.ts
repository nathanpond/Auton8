import { api } from "./client";

export type ProjectionFeedHealth = {
  feedName: string;
  eventsObservedTotal: number;
  lastEventObservedAtUtc: string | null;
  watermarkUtc: string | null;
};

export type ProjectionHealthSnapshot = {
  name: string;
  version: number;
  sourceType: string;
  paused: boolean;
  eventsAppliedTotal: number;
  eventsAppliedSinceStart: number;
  applyFailuresTotal: number;
  lastAppliedAtUtc: string | null;
  lastFailureAtUtc: string | null;
  lastFailureMessage: string | null;
  feeds: ProjectionFeedHealth[];
};

export type ProjectionActionResult = {
  ok: boolean;
  message: string;
  snapshot: ProjectionHealthSnapshot | null;
};

export async function listProjections(signal?: AbortSignal): Promise<ProjectionHealthSnapshot[]> {
  const { data } = await api.get<ProjectionHealthSnapshot[]>("/api/admin/projections/", { signal });
  return data;
}

export async function pauseProjection(name: string): Promise<ProjectionActionResult> {
  const { data } = await api.post<ProjectionActionResult>(
    `/api/admin/projections/${encodeURIComponent(name)}/pause`);
  return data;
}

export async function resumeProjection(name: string): Promise<ProjectionActionResult> {
  const { data } = await api.post<ProjectionActionResult>(
    `/api/admin/projections/${encodeURIComponent(name)}/resume`);
  return data;
}

export async function rebuildProjection(name: string): Promise<ProjectionActionResult> {
  const { data } = await api.post<ProjectionActionResult>(
    `/api/admin/projections/${encodeURIComponent(name)}/rebuild`);
  return data;
}

// Watermark reset is per FEED, not per projection: the watermark row keyed by
// feed name is what a replay reads, so clearing it makes the feed re-observe
// from the beginning. Documented in docs/projection-framework/operations.md as
// the recovery step for a corrupted or retention-truncated cache; it had no
// caller outside that doc's curl example (archived-47).
export async function resetFeedWatermark(feedName: string): Promise<ProjectionActionResult> {
  const { data } = await api.post<ProjectionActionResult>(
    `/api/admin/projections/feeds/${encodeURIComponent(feedName)}/reset-watermark`);
  return data;
}
