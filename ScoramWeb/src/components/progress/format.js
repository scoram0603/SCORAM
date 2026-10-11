// Presentation-only helpers for the Smart Progress screens. NOTHING here computes a metric -- it only
// formats server fields. A null accuracy is always "—", never "0%".

// The backend's Analytics:MinAttemptsForAnalysis default. The API does not expose the (configurable)
// threshold, so it is named once here for the "n of 5 answered" copy.
export const MIN_ATTEMPTS_FOR_ANALYSIS = 5;

export const KIND_LABELS = {
  Mock: "Mock Test",
  Practice: "Practice Test",
  PreviousYearPaper: "Previous Year Paper",
  Quiz: "Quiz",
};

export const KIND_OPTIONS = [
  { value: "", label: "All assessment types" },
  { value: "PreviousYearPaper", label: "Previous Year Papers" },
  { value: "Practice", label: "Practice Tests" },
  { value: "Mock", label: "Mock Tests" },
  { value: "Quiz", label: "Quizzes" },
];

export const PERIOD_OPTIONS = [
  { value: "", label: "All time" },
  { value: "7", label: "Last 7 days" },
  { value: "30", label: "Last 30 days" },
  { value: "90", label: "Last 90 days" },
  { value: "365", label: "Last 12 months" },
];

export function kindLabel(kind) {
  return KIND_LABELS[kind] || kind || "Assessment";
}

function trimNumber(n, digits = 1) {
  return Number(Number(n).toFixed(digits)).toString();
}

export function isNum(v) {
  return v !== null && v !== undefined && v !== "" && Number.isFinite(Number(v));
}

// null -> "—" (never 0%)
export function fmtPct(v) {
  return isNum(v) ? `${trimNumber(v)}%` : "—";
}

export function fmtNum(v) {
  return isNum(v) ? trimNumber(v, 2) : "—";
}

// Percentage-POINT difference, always with a sign and the unit spelled out. Null -> null.
export function fmtPoints(v) {
  if (!isNum(v)) return null;
  const r = Number(Number(v).toFixed(1));
  if (r === 0) return "0 percentage points";
  const unit = Math.abs(r) === 1 ? "percentage point" : "percentage points";
  return `${r > 0 ? "+" : "−"}${Math.abs(r)} ${unit}`;
}

export function fmtSeconds(v) {
  if (!isNum(v)) return "—";
  const total = Math.round(Number(v));
  if (total < 60) return `${total}s`;
  const m = Math.floor(total / 60);
  const s = total % 60;
  return s ? `${m}m ${s}s` : `${m}m`;
}

// The API returns UTC DateTimes that may serialise without a zone suffix; treat those as UTC.
export function parseServerDate(value) {
  if (!value) return null;
  const str = String(value);
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(str);
  const d = new Date(hasZone ? str : `${str}Z`);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function fmtDate(value, withTime = false) {
  const d = parseServerDate(value);
  if (!d) return "—";
  return d.toLocaleString("en-IN", {
    day: "numeric",
    month: "short",
    year: "numeric",
    ...(withTime ? { hour: "numeric", minute: "2-digit" } : {}),
  });
}

// "2026-10-10" (IST day string from the API) -> readable, without timezone shifting.
export function fmtDay(yyyyMmDd) {
  if (!yyyyMmDd) return "—";
  const [y, m, d] = String(yyyyMmDd).split("-").map(Number);
  if (!y || !m || !d) return String(yyyyMmDd);
  return new Date(y, m - 1, d).toLocaleDateString("en-IN", { weekday: "short", day: "numeric", month: "short" });
}

export function plural(n, one, many) {
  return `${n} ${n === 1 ? one : many || `${one}s`}`;
}

// Deep link to the existing Practice Test generator (pages/PracticeTests.jsx reads these params).
// Returns null when the server did not map the topic to a Question Bank subject/topic -- the caller
// must then hide the action and explain, never guess.
export function practiceLink(target) {
  if (!target || (!target.subjectId && !target.topicId)) return null;
  const q = new URLSearchParams();
  if (target.subjectId) q.set("subjectId", target.subjectId);
  if (target.topicId) q.set("topicId", target.topicId);
  if (target.examId) q.set("examId", target.examId);
  return `/tests/practice?${q.toString()}`;
}

export function topicLink(subject, topic, filters = {}) {
  const q = new URLSearchParams();
  q.set("subject", subject || "Unclassified");
  q.set("topic", topic || "Unclassified");
  if (filters.examId) q.set("examId", filters.examId);
  if (filters.kind) q.set("kind", filters.kind);
  if (filters.days) q.set("days", filters.days);
  if (filters.myExamsOnly) q.set("myExamsOnly", "true");
  return `/progress/topic?${q.toString()}`;
}

export function newRequestId() {
  try {
    if (typeof crypto !== "undefined" && crypto.randomUUID) return crypto.randomUUID();
  } catch {
    // fall through
  }
  const hex = (n) => Array.from({ length: n }, () => Math.floor(Math.random() * 16).toString(16)).join("");
  return `${hex(8)}-${hex(4)}-4${hex(3)}-a${hex(3)}-${hex(12)}`;
}
