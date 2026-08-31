import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === "") return;
    // Arrays (multi-select filters, e.g. examIds for "My Exams") become repeated keys -- see
    // api/questionBank.js's toQueryString, which this now matches.
    if (Array.isArray(value)) {
      if (value.length === 0) return;
      value.forEach((v) => query.append(key, v));
    } else {
      query.set(key, value);
    }
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/mocktests?examName=&examIds=&testType=&page=&pageSize=
export function listMockTests(params = {}, opts = {}) {
  return apiFetch(`/api/mocktests${toQueryString(params)}`, opts);
}

// GET /api/mocktests/{id}/summary -- Title/Duration/NegativeMarking/QuestionCount/Instructions only,
// no question payload. Use this for the Pre-Exam Instructions screen; use getMockTest (below) only
// once actually attempting.
export function getMockTestSummary(id, opts = {}) {
  return apiFetch(`/api/mocktests/${id}/summary`, opts);
}

// POST /api/mocktests/{id}/start -- returns the same attempt if one is already InProgress (resume),
// otherwise creates a fresh one. See api/testAttempts.js for answer/submit/resume once you have an
// attemptId back from this, and for the shared "my attempts" (Practice + Mock) history list.
export function startMockTest(id) {
  return apiFetch(`/api/mocktests/${id}/start`, { method: "POST", auth: true });
}