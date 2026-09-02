import { useMemo, useState } from "react";
import { Alert, NativeSelect, Stack, Text, TextInput } from "@mantine/core";

// Cron picker for the v1 backend's "*/N * * * *" parser
// (DatasetRefreshScheduler / PipelineRunWorker). Anything else is
// silently treated as manual-only on the server, so the picker
// surfaces presets that produce the supported form and warns users
// when a custom value won't actually trigger. Each value is the raw
// cron string round-tripped through useState by the parent.
//
// Used by:
//   - DatasetsPage (Refresh cron, create + edit modals)
//   - PipelinesPage (Schedule cron, create modal)
//   - PipelineEditor (Schedule cron, Settings modal)
//
// Audit fix archived-12.

export type CronExpressionBuilderProps = {
  label?: string;
  value: string;
  onChange: (next: string) => void;
  // Optional helper line under the label. The next-run preview /
  // unsupported-form warning render under that.
  description?: string;
};

type Preset = { label: string; value: string };

const PRESETS: Preset[] = [
  { label: "Manual only (no schedule)", value: "" },
  { label: "Every minute", value: "*/1 * * * *" },
  { label: "Every 5 minutes", value: "*/5 * * * *" },
  { label: "Every 15 minutes", value: "*/15 * * * *" },
  { label: "Every 30 minutes", value: "*/30 * * * *" },
  { label: "Hourly", value: "*/60 * * * *" },
  { label: "Custom (advanced)", value: "__custom__" }
];

export default function CronExpressionBuilder({
  label = "Schedule",
  value,
  onChange,
  description
}: CronExpressionBuilderProps) {
  // Tracks whether the user explicitly clicked "Custom (advanced)" so
  // we keep the TextInput visible even when their typed value happens
  // to round-trip into one of the preset strings. Initialize from the
  // incoming value: if it doesn't match a preset (and isn't empty),
  // it's already "custom".
  const initiallyCustom = useMemo(() => {
    const trimmed = value.trim();
    if (trimmed === "") return false;
    return !PRESETS.some((p) => p.value === trimmed && p.value !== "__custom__");
  }, []); // intentionally only first render — controlled by the selector after that
  const [customMode, setCustomMode] = useState(initiallyCustom);

  // What option the NativeSelect shows. If the user is in customMode
  // we surface "Custom (advanced)" regardless of whether the typed
  // value matches a preset. Otherwise the option mirrors the value
  // (so external state resets land correctly).
  const matchedPreset = useMemo(() => {
    if (customMode) return "__custom__";
    const trimmed = value.trim();
    if (trimmed === "") return "";
    const exact = PRESETS.find((p) => p.value === trimmed && p.value !== "__custom__");
    return exact ? exact.value : "__custom__";
  }, [value, customMode]);

  const supportedMinutes = useMemo(() => parseSupportedInterval(value), [value]);
  const nextRuns = useMemo(
    () => (supportedMinutes ? computeNextRuns(supportedMinutes, new Date(), 3) : []),
    [supportedMinutes]
  );

  function handlePresetChange(nextPreset: string) {
    if (nextPreset === "__custom__") {
      setCustomMode(true);
      // Seed an editable value when switching from "Manual only" so
      // the TextInput has something to start from.
      if (value.trim() === "") onChange("*/5 * * * *");
      return;
    }
    setCustomMode(false);
    onChange(nextPreset);
  }

  return (
    <Stack gap={4}>
      <NativeSelect
        label={label}
        description={description}
        data={PRESETS.map((p) => ({ value: p.value, label: p.label }))}
        value={matchedPreset}
        onChange={(e) => handlePresetChange(e.currentTarget.value)}
      />
      {matchedPreset === "__custom__" ? (
        <TextInput
          label="Custom cron expression"
          description="Five fields separated by spaces. v1 only triggers schedules of the form `*/N * * * *`."
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          placeholder="*/5 * * * *"
          aria-label="Custom cron expression"
        />
      ) : null}
      {value.trim() === "" ? null : supportedMinutes !== null ? (
        <Alert color="blue" variant="light" title="Next runs">
          <Stack gap={2}>
            <Text size="xs" c="dimmed">
              Every {supportedMinutes} minute{supportedMinutes === 1 ? "" : "s"}. Upcoming:
            </Text>
            {nextRuns.map((d, i) => (
              <Text key={i} size="xs" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
                {d.toLocaleString()}
              </Text>
            ))}
          </Stack>
        </Alert>
      ) : (
        <Alert color="yellow" variant="light" title="Won't trigger">
          v1 only recognizes schedules of the form <code>*/N * * * *</code> (every N minutes
          in the minute field). This cron will be stored but the scheduler will treat it as
          manual-only.
        </Alert>
      )}
    </Stack>
  );
}

// Returns the N from "*/N * * * *" if the cron matches the supported
// form, otherwise null. Mirrors DatasetRefreshScheduler.TryParseMinutesInterval
// so the preview matches the server's actual firing semantics.
export function parseSupportedInterval(cron: string): number | null {
  const trimmed = cron.trim();
  if (trimmed === "") return null;
  const parts = trimmed.split(/\s+/);
  if (parts.length !== 5) return null;
  const [minute, hour, dom, month, dow] = parts;
  if (hour !== "*" || dom !== "*" || month !== "*" || dow !== "*") return null;
  if (!minute.startsWith("*/")) return null;
  const n = Number.parseInt(minute.slice(2), 10);
  if (!Number.isFinite(n) || n <= 0) return null;
  return n;
}

// Next N firings for a `*/everyNMinutes` cron. Mirrors the backend's
// `nowUtc - lastRefreshedAtUtc >= TimeSpan.FromMinutes(N)` check: the
// upcoming firing is the next minute whose minute-of-hour is a multiple
// of N. Times are returned in local-tz because the SPA renders them
// inline (the server stores LastRefreshedAtUtc; the comparison is
// timezone-agnostic).
export function computeNextRuns(everyNMinutes: number, now: Date, count: number): Date[] {
  const out: Date[] = [];
  const base = new Date(now);
  base.setSeconds(0, 0);
  // Bump to the start of the next minute so the first fire is in the
  // future (a midnight-aligned preview running at exactly :00:00.000
  // would otherwise list "now" as the next firing).
  base.setMinutes(base.getMinutes() + 1);
  let cursor = new Date(base);
  // Walk forward until the minute-of-hour aligns; cap the search at
  // 60 iterations so even with N=60 we don't loop indefinitely on
  // pathological inputs.
  for (let step = 0; step < 60 && cursor.getMinutes() % everyNMinutes !== 0; step++) {
    cursor.setMinutes(cursor.getMinutes() + 1);
  }
  for (let i = 0; i < count; i++) {
    out.push(new Date(cursor));
    cursor = new Date(cursor.getTime() + everyNMinutes * 60_000);
  }
  return out;
}
