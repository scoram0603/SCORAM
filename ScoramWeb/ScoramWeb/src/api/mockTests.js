import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/mocktests?examName=&testType=&page=&pageSize=
export function listMockTests(params = {}, opts = {}) {
  return apiFetch(`/api/mocktests${toQueryString(params)}`, opts);
}

// GET /api/mocktests/{id} — question list WITHOUT the answer key
export function getMockTestDetail(id, opts = {}) {
  return apiFetch(`/api/mocktests/${id}`, opts);
}

// POST /api/mocktests/{id}/attempts — auto-graded, returns full breakdown
export function submitAttempt(id, payload) {
  return apiFetch(`/api/mocktests/${id}/attempts`, { method: "POST", body: payload, auth: true });
}

// GET /api/mocktests/attempts/mine?page=&pageSize=
export function getMyAttempts(params = {}, opts = {}) {
  return apiFetch(`/api/mocktests/attempts/mine${toQueryString(params)}`, { ...opts, auth: true });
}

// GET /api/mocktests/attempts/{attemptId} — full per-question breakdown of a past attempt
export function getAttemptDetail(attemptId, opts = {}) {
  return apiFetch(`/api/mocktests/attempts/${attemptId}`, { ...opts, auth: true });
}

// ---------- SCORAM_TESTS: new start -> auto-save -> submit flow ----------

// POST /api/mocktests/{id}/start -- returns the same attempt if one is already InProgress (resume),
// otherwise creates a fresh one. See api/testAttempts.js for answer/submit/resume once you have an
// attemptId back from this.
export function startMockTest(id) {
  return apiFetch(`/api/mocktests/${id}/start`, { method: "POST", auth: true });
}
