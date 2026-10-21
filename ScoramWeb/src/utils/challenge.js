// STUDY PARTNER CHALLENGE helpers -- pure functions, no React. The server owns every number (progress,
// winner); this file only turns them into text and validates what the student typed.

export const MAX_CHALLENGE_MINUTES = 30 * 24 * 60; // 30 days
export const MAX_CHALLENGE_QUESTIONS = 1000;
export const UNIT_MINUTES = { minutes: 1, hours: 60, days: 1440 };

/** "15 min", "2 h", "1 h 30 min", "10 days", "1 day 4 h". */
export function formatMinutes(total) {
  const m = Math.max(0, Math.round(Number(total) || 0));
  if (m < 60) return `${m} min`;
  if (m < 1440) return m % 60 === 0 ? `${m / 60} h` : `${Math.floor(m / 60)} h ${m % 60} min`;
  const d = Math.floor(m / 1440);
  const h = Math.floor((m % 1440) / 60);
  const dayText = `${d} ${d === 1 ? "day" : "days"}`;
  return h === 0 ? dayText : `${dayText} ${h} h`;
}

/** Remaining time until `endDate`: "2 h 10 min left", "45 min left", "Ended". */
export function timeLeft(endDate, now = Date.now()) {
  if (!endDate) return "";
  const ms = new Date(endDate).getTime() - now;
  if (Number.isNaN(ms)) return "";
  if (ms <= 0) return "Ended";
  const mins = Math.max(1, Math.ceil(ms / 60000));
  return `${formatMinutes(mins)} left`;
}

/** Turn the form's raw text into { minutes } or { error }. Whole numbers only; 1 minute .. 30 days. */
export function parseDuration(value, unit) {
  const text = String(value ?? "").trim();
  if (!/^\d+$/.test(text)) return { error: "Enter the time as a whole number." };
  const minutes = Number(text) * (UNIT_MINUTES[unit] ?? 0);
  if (!minutes || minutes < 1) return { error: "Time must be at least 1 minute." };
  if (minutes > MAX_CHALLENGE_MINUTES) return { error: "A challenge can last 30 days at most." };
  return { minutes };
}

export function parseQuestions(value) {
  const text = String(value ?? "").trim();
  if (!/^\d+$/.test(text)) return { error: "Enter the number of questions." };
  const n = Number(text);
  if (n < 1 || n > MAX_CHALLENGE_QUESTIONS) return { error: `Questions must be between 1 and ${MAX_CHALLENGE_QUESTIONS}.` };
  return { count: n };
}

/** Split minutes into the largest unit that divides evenly -- used to pre-fill a rematch. */
export function splitMinutes(total) {
  const m = Math.max(1, Math.round(Number(total) || 1));
  if (m % 1440 === 0) return { value: String(m / 1440), unit: "days" };
  if (m % 60 === 0) return { value: String(m / 60), unit: "hours" };
  return { value: String(m), unit: "minutes" };
}

export const durationOf = (c) => c.durationMinutes ?? (c.durationDays ?? 0) * 1440;

/** This student's and the other student's progress objects (either can be null before the start). */
export function sides(c) {
  return c.iAmCreator
    ? { mine: c.creatorProgress ?? null, theirs: c.partnerProgress ?? null, other: c.partner }
    : { mine: c.partnerProgress ?? null, theirs: c.creatorProgress ?? null, other: c.creator };
}

/** One-line caption under a progress bar, e.g. "32/50 questions", "3/5 days", "best 12/40 in one attempt". */
export function progressCaption(c, p) {
  if (!p) return "Not started";
  switch (c.type) {
    case "Streak": return `${p.activeDays}/${c.targetDays} days`;
    case "Speed": return p.fastestSeconds != null
      ? `${Math.floor(p.fastestSeconds / 60)}m ${p.fastestSeconds % 60}s`
      : `best ${p.bestAttemptAnswered}/${c.questionCount} in one attempt`;
    case "Accuracy": return `${p.answered}/${c.questionCount} questions · ${Number(p.accuracy).toFixed(1)}%`;
    default: return `${p.answered}/${c.questionCount} questions`;
  }
}

/** Where the student goes to keep going: attempts of every kind inside the window count. */
export function continuePath(c) {
  return c.exam?.id ? `/tests/practice?examId=${encodeURIComponent(c.exam.id)}` : "/tests";
}

export const isRunning = (c) => c.status === "InProgress" || c.status === "Accepted";
