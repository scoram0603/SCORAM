// Helpers for opening an admin notice (a "SystemAnnouncement" notification) from the bell.

export const NOTICE_TYPE = "SystemAnnouncement";

// Admin notices carry a WHITELISTED destination key in EntityId (see AdminNotificationsController).
// Older rows stored website paths that do not exist ("/notifications", "/mock-tests", "/papers"), so the
// website maps the key to a real route instead of trusting the stored path.
const DESTINATIONS = {
  Home: { path: "/", label: "Go to Home" },
  Quizzes: { path: "/quizzes", label: "Open Quizzes" },
  MockTests: { path: "/tests/mock", label: "Open Mock Tests" },
  Pyp: { path: "/pyq", label: "Open Previous Year Papers" },
  Tests: { path: "/tests", label: "Open Tests" },
  Progress: { path: "/progress", label: "Open Progress" },
};

export function isNotice(notification) {
  return notification?.type === NOTICE_TYPE;
}

/** { path, label } for the optional action button, or null when the notice has no useful destination. */
export function noticeDestination(notification) {
  if (!isNotice(notification)) return null;
  if (notification.entityType !== "Screen") return null;
  return DESTINATIONS[notification.entityId] || null;
}

// The API sends UTC timestamps without a zone suffix; without one `new Date()` would read them as local time.
export function parseServerDate(value) {
  if (!value) return null;
  const text = String(value);
  const hasZone = /[zZ]$|[+-]\d{2}:?\d{2}$/.test(text);
  const date = new Date(hasZone ? text : `${text}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function formatNoticeTime(value) {
  const date = parseServerDate(value);
  if (!date) return "";
  return date.toLocaleString(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
}
