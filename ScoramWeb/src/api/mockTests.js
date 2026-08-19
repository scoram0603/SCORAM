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

// POST /api/mocktests/{id}/start -- returns the same attempt if one is already InProgress (resume),
// otherwise creates a fresh one. See api/testAttempts.js for answer/submit/resume once you have an
// attemptId back from this, and for the shared "my attempts" (Practice + Mock) history list.
export function startMockTest(id) {
  return apiFetch(`/api/mocktests/${id}/start`, { method: "POST", auth: true });
}