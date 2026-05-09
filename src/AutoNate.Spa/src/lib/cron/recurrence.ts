// Outlook-style recurrence picker model translated to / parsed from a Quartz
// 6-field cron expression (the dialect Flowable accepts on
// `<bpmn:timeCycle flowable:type="cron">`).
//
// The picker is the source of truth — `generateCron` is the forward direction.
// `parseCron` recovers picker state from cron text written by this module
// (round-tripping the supported shapes); arbitrary cron expressions return
// `null` and the UI falls back to the raw-cron escape hatch.

export type TimerMode = "daily" | "weekly" | "monthly" | "yearly";
export type WeekDay = "MON" | "TUE" | "WED" | "THU" | "FRI" | "SAT" | "SUN";
export type MonthlyKind = "dayOfMonth" | "ordinalWeekday";
export type Ordinal = "1" | "2" | "3" | "4" | "L";

export const WEEK_DAYS: readonly WeekDay[] = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

export type RecurrenceState = {
  mode: TimerMode;
  hour: string;
  minute: string;

  dailyEveryN: string;
  dailyWeekdaysOnly: boolean;

  weeklyEveryN: string;
  weeklyDays: WeekDay[];

  monthlyKind: MonthlyKind;
  monthlyEveryN: string;
  monthlyDayOfMonth: string;
  monthlyOrdinal: Ordinal;
  monthlyOrdinalDay: WeekDay;

  yearlyMonth: string;
  yearlyDay: string;
};

export type CronGenerationResult =
  | { ok: true; cron: string; warnings: string[] }
  | { ok: false; error: string };

export function defaultRecurrenceState(): RecurrenceState {
  return {
    mode: "daily",
    hour: "9",
    minute: "0",
    dailyEveryN: "1",
    dailyWeekdaysOnly: false,
    weeklyEveryN: "1",
    weeklyDays: ["MON"],
    monthlyKind: "dayOfMonth",
    monthlyEveryN: "1",
    monthlyDayOfMonth: "1",
    monthlyOrdinal: "1",
    monthlyOrdinalDay: "MON",
    yearlyMonth: "1",
    yearlyDay: "1"
  };
}

const ORDINAL_TO_NUMBER: Record<Ordinal, string> = { "1": "1", "2": "2", "3": "3", "4": "4", L: "L" };
// Quartz day-of-week numerics for the `<dow>L` form: 1=SUN, 2=MON, …, 7=SAT.
// We only need this for `last weekday of month` since `MON#1` works with literals.
const QUARTZ_DOW_NUMERIC: Record<WeekDay, string> = {
  SUN: "1", MON: "2", TUE: "3", WED: "4", THU: "5", FRI: "6", SAT: "7"
};

const MONTH_NAMES = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

export function generateCron(state: RecurrenceState): CronGenerationResult {
  const hour = parseClampedInt(state.hour, 0, 23);
  if (hour === null) return fail("Hour must be between 0 and 23.");
  const minute = parseClampedInt(state.minute, 0, 59);
  if (minute === null) return fail("Minute must be between 0 and 59.");
  const warnings: string[] = [];

  switch (state.mode) {
    case "daily": {
      if (state.dailyWeekdaysOnly) {
        return ok(`0 ${minute} ${hour} ? * MON-FRI`, warnings);
      }
      const everyN = parseClampedInt(state.dailyEveryN, 1, 31);
      if (everyN === null) return fail("'Every N days' must be between 1 and 31.");
      if (everyN === 1) {
        return ok(`0 ${minute} ${hour} * * ?`, warnings);
      }
      warnings.push(
        "Every N days uses day-of-month stepping; the schedule resets on the 1st of each month, " +
          "so the first interval after a month roll may be shorter than N days."
      );
      return ok(`0 ${minute} ${hour} */${everyN} * ?`, warnings);
    }

    case "weekly": {
      const everyN = parseClampedInt(state.weeklyEveryN, 1, 52);
      if (everyN === null) return fail("'Every N weeks' must be between 1 and 52.");
      const days = normalizeWeekDays(state.weeklyDays);
      if (days.length === 0) return fail("Select at least one day of the week.");
      if (everyN > 1) {
        return fail(
          "Cron cannot express weekly intervals greater than 1. Use the Advanced section to enter a raw cron expression, or set 'Every N weeks' to 1."
        );
      }
      return ok(`0 ${minute} ${hour} ? * ${days.join(",")}`, warnings);
    }

    case "monthly": {
      const everyN = parseClampedInt(state.monthlyEveryN, 1, 12);
      if (everyN === null) return fail("'Every N months' must be between 1 and 12.");
      const monthField = everyN === 1 ? "*" : `1/${everyN}`;
      if (state.monthlyKind === "dayOfMonth") {
        const dom = parseClampedInt(state.monthlyDayOfMonth, 1, 31);
        if (dom === null) return fail("'Day of month' must be between 1 and 31.");
        return ok(`0 ${minute} ${hour} ${dom} ${monthField} ?`, warnings);
      }
      const ord = ORDINAL_TO_NUMBER[state.monthlyOrdinal];
      const day = state.monthlyOrdinalDay;
      if (ord === "L") {
        const numeric = QUARTZ_DOW_NUMERIC[day];
        return ok(`0 ${minute} ${hour} ? ${monthField} ${numeric}L`, warnings);
      }
      return ok(`0 ${minute} ${hour} ? ${monthField} ${day}#${ord}`, warnings);
    }

    case "yearly": {
      const month = parseClampedInt(state.yearlyMonth, 1, 12);
      if (month === null) return fail("Month must be between 1 and 12.");
      const day = parseClampedInt(state.yearlyDay, 1, 31);
      if (day === null) return fail("Day must be between 1 and 31.");
      return ok(`0 ${minute} ${hour} ${day} ${month} ?`, warnings);
    }
  }
}

export function parseCron(cron: string): RecurrenceState | null {
  const trimmed = cron.trim();
  if (!trimmed) return null;
  const parts = trimmed.split(/\s+/);
  if (parts.length !== 6) return null;
  const [sec, min, hr, dom, mon, dow] = parts;
  if (sec !== "0") return null;
  const hour = matchInt(hr);
  const minute = matchInt(min);
  if (hour === null || minute === null) return null;

  const base = defaultRecurrenceState();
  base.hour = String(hour);
  base.minute = String(minute);

  // Daily, every 1 → `0 m H * * ?`
  if (dom === "*" && mon === "*" && dow === "?") {
    return { ...base, mode: "daily", dailyEveryN: "1", dailyWeekdaysOnly: false };
  }

  // Daily, weekdays-only → `0 m H ? * MON-FRI`
  if (dom === "?" && mon === "*" && dow === "MON-FRI") {
    return { ...base, mode: "daily", dailyWeekdaysOnly: true };
  }

  // Daily every N (N>1) → `0 m H */N * ?`
  const dailyEveryNMatch = dom.match(/^\*\/(\d+)$/);
  if (dailyEveryNMatch && mon === "*" && dow === "?") {
    return { ...base, mode: "daily", dailyEveryN: dailyEveryNMatch[1] };
  }

  // Weekly every 1 (with day list) → `0 m H ? * MON,WED`
  if (dom === "?" && mon === "*") {
    const days = parseWeekDayList(dow);
    if (days && days.length > 0) {
      return {
        ...base,
        mode: "weekly",
        weeklyEveryN: "1",
        weeklyDays: days
      };
    }
  }

  // Monthly day-of-month → `0 m H D */N|* ?`
  const monthFieldEvery = parseMonthEveryField(mon);
  if (dow === "?" && monthFieldEvery !== null) {
    const dayInt = matchInt(dom);
    if (dayInt !== null && dayInt >= 1 && dayInt <= 31) {
      return {
        ...base,
        mode: "monthly",
        monthlyKind: "dayOfMonth",
        monthlyEveryN: String(monthFieldEvery),
        monthlyDayOfMonth: String(dayInt)
      };
    }
  }

  // Monthly ordinal weekday — `0 m H ? */N|* <DAY>#<n>` or `<dow>L`
  if (dom === "?" && monthFieldEvery !== null) {
    const ordinalMatch = dow.match(/^(MON|TUE|WED|THU|FRI|SAT|SUN)#([1-4])$/);
    if (ordinalMatch) {
      return {
        ...base,
        mode: "monthly",
        monthlyKind: "ordinalWeekday",
        monthlyEveryN: String(monthFieldEvery),
        monthlyOrdinal: ordinalMatch[2] as Ordinal,
        monthlyOrdinalDay: ordinalMatch[1] as WeekDay
      };
    }
    const lastMatch = dow.match(/^([1-7])L$/);
    if (lastMatch) {
      const numeric = lastMatch[1];
      const day = (Object.entries(QUARTZ_DOW_NUMERIC) as [WeekDay, string][]).find(
        ([, n]) => n === numeric
      );
      if (day) {
        return {
          ...base,
          mode: "monthly",
          monthlyKind: "ordinalWeekday",
          monthlyEveryN: String(monthFieldEvery),
          monthlyOrdinal: "L",
          monthlyOrdinalDay: day[0]
        };
      }
    }
  }

  // Yearly → `0 m H D M ?`
  if (dow === "?") {
    const dayInt = matchInt(dom);
    const monthInt = matchInt(mon);
    if (
      dayInt !== null &&
      monthInt !== null &&
      dayInt >= 1 &&
      dayInt <= 31 &&
      monthInt >= 1 &&
      monthInt <= 12
    ) {
      return {
        ...base,
        mode: "yearly",
        yearlyMonth: String(monthInt),
        yearlyDay: String(dayInt)
      };
    }
  }

  return null;
}

export function describeRecurrence(state: RecurrenceState): string {
  const time = `${pad(state.hour)}:${pad(state.minute)}`;
  switch (state.mode) {
    case "daily":
      if (state.dailyWeekdaysOnly) return `Every weekday at ${time}`;
      return state.dailyEveryN === "1" ? `Daily at ${time}` : `Every ${state.dailyEveryN} days at ${time}`;
    case "weekly": {
      const days = state.weeklyDays.join(", ");
      return state.weeklyEveryN === "1"
        ? `Weekly on ${days} at ${time}`
        : `Every ${state.weeklyEveryN} weeks on ${days} at ${time}`;
    }
    case "monthly": {
      const cadence = state.monthlyEveryN === "1" ? "Every month" : `Every ${state.monthlyEveryN} months`;
      if (state.monthlyKind === "dayOfMonth") {
        return `${cadence} on day ${state.monthlyDayOfMonth} at ${time}`;
      }
      const ordinalLabel =
        state.monthlyOrdinal === "L"
          ? "the last"
          : `the ${ordinalWord(state.monthlyOrdinal)}`;
      return `${cadence} on ${ordinalLabel} ${state.monthlyOrdinalDay} at ${time}`;
    }
    case "yearly": {
      const monthIndex = parseClampedInt(state.yearlyMonth, 1, 12) ?? 1;
      return `Every year on ${MONTH_NAMES[monthIndex - 1]} ${state.yearlyDay} at ${time}`;
    }
  }
}

function ordinalWord(ordinal: Ordinal): string {
  switch (ordinal) {
    case "1":
      return "first";
    case "2":
      return "second";
    case "3":
      return "third";
    case "4":
      return "fourth";
    case "L":
      return "last";
  }
}

function pad(value: string): string {
  const n = parseClampedInt(value, 0, 99);
  if (n === null) return value;
  return n.toString().padStart(2, "0");
}

function ok(cron: string, warnings: string[]): CronGenerationResult {
  return { ok: true, cron, warnings };
}

function fail(error: string): CronGenerationResult {
  return { ok: false, error };
}

function parseClampedInt(value: string, min: number, max: number): number | null {
  const trimmed = value.trim();
  if (!/^[0-9]+$/.test(trimmed)) return null;
  const n = parseInt(trimmed, 10);
  if (!Number.isFinite(n) || n < min || n > max) return null;
  return n;
}

function matchInt(value: string): number | null {
  if (!/^[0-9]+$/.test(value)) return null;
  return parseInt(value, 10);
}

function parseMonthEveryField(field: string): number | null {
  if (field === "*") return 1;
  const m = field.match(/^1\/(\d+)$/);
  if (m) {
    const n = parseInt(m[1], 10);
    if (Number.isFinite(n) && n >= 1) return n;
  }
  return null;
}

function parseWeekDayList(field: string): WeekDay[] | null {
  if (field === "?" || field === "*") return null;
  const tokens = field.split(",");
  const days: WeekDay[] = [];
  for (const token of tokens) {
    if (!WEEK_DAYS.includes(token as WeekDay)) return null;
    days.push(token as WeekDay);
  }
  // Preserve the canonical ordering so `parseCron(generateCron(s)) === s`
  // round-trips even when the user picks days out of order.
  return normalizeWeekDays(days);
}

function normalizeWeekDays(days: WeekDay[]): WeekDay[] {
  const seen = new Set<WeekDay>();
  const ordered: WeekDay[] = [];
  for (const day of WEEK_DAYS) {
    if (days.includes(day) && !seen.has(day)) {
      ordered.push(day);
      seen.add(day);
    }
  }
  return ordered;
}
