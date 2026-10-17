// Date-range presets for the dashboard / analytics / reports. All dates are ADMIN-LOCAL calendar dates
// (yyyy-MM-dd); the browser's UTC offset is sent alongside so the server buckets days the way the admin
// sees them (the previous dashboard grouped by UTC midnight, which shifted late-evening IST activity).

export const PRESETS = [
  { key: "today", label: "Today" },
  { key: "yesterday", label: "Yesterday" },
  { key: "7d", label: "Last 7 days" },
  { key: "30d", label: "Last 30 days" },
  { key: "90d", label: "Last 90 days" },
  { key: "thisMonth", label: "This month" },
  { key: "prevMonth", label: "Previous month" },
  { key: "custom", label: "Custom range" },
];

const pad = (n) => String(n).padStart(2, "0");
export const toIsoDate = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const addDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);

// getTimezoneOffset() is minutes BEHIND UTC (IST = -330); the API wants minutes AHEAD of UTC.
export const browserOffsetMinutes = () => -new Date().getTimezoneOffset();

export function resolvePreset(key, custom = {}) {
  const today = new Date();
  const t = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  let from = t;
  let to = t;
  switch (key) {
    case "yesterday": from = to = addDays(t, -1); break;
    case "7d": from = addDays(t, -6); break;
    case "30d": from = addDays(t, -29); break;
    case "90d": from = addDays(t, -89); break;
    case "thisMonth": from = new Date(t.getFullYear(), t.getMonth(), 1); break;
    case "prevMonth":
      from = new Date(t.getFullYear(), t.getMonth() - 1, 1);
      to = new Date(t.getFullYear(), t.getMonth(), 0);
      break;
    case "custom":
      return { key, from: custom.from, to: custom.to, tzOffsetMinutes: browserOffsetMinutes() };
    default: break;
  }
  return { key, from: toIsoDate(from), to: toIsoDate(to), tzOffsetMinutes: browserOffsetMinutes() };
}

export function describeRange(range) {
  if (!range?.from) return "";
  return range.from === range.to ? range.from : `${range.from} → ${range.to}`;
}

// Custom ranges are validated client-side too (the server clamps/rejects independently).
export function validateCustom(from, to, maxDays = 366) {
  if (!from || !to) return "Choose both a start and an end date.";
  if (from > to) return "The start date must be on or before the end date.";
  const days = (new Date(to) - new Date(from)) / 86400000 + 1;
  if (days > maxDays) return `Pick a range of at most ${maxDays} days.`;
  const today = toIsoDate(new Date());
  if (to > today) return "The end date cannot be in the future.";
  return null;
}
