import { apiFetch } from "./client";

function qs(params) {
  const clean = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== "" && v != null));
  const s = new URLSearchParams(clean).toString();
  return s ? `?${s}` : "";
}

// GET /api/papers -- the main filterable/sortable browse grid (reference: "PYP Practice" page).
export function browsePapers(params = {}, { signal } = {}) {
  return apiFetch(`/api/papers${qs(params)}`, { signal });
}

// GET /api/papers/filter-options?examId=&year= -- which Tier/Date/Shift/Paper-label/Language values
// actually exist right now, so the filter row only shows a dropdown for a filter that's meaningful.
export function getPaperFilterOptions(params = {}, { signal } = {}) {
  return apiFetch(`/api/papers/filter-options${qs(params)}`, { signal });
}

// GET /api/papers/my-attempts?status=InProgress|Completed -- "Continue Attempting" / "Completed
// Papers" tabs.
export function getMyPaperAttempts(status, { signal } = {}) {
  return apiFetch(`/api/papers/my-attempts${qs({ status })}`, { signal, auth: true });
}

// GET /api/papers/years?examId=
export function getPaperYears(examId, { signal } = {}) {
  return apiFetch(`/api/papers/years?examId=${examId}`, { signal });
}

// GET /api/papers/languages?examId=&year=
export function getPaperLanguages(examId, year, { signal } = {}) {
  return apiFetch(`/api/papers/languages?examId=${examId}&year=${year}`, { signal });
}

// GET /api/papers/sets?examId=&year=&language= -- usually one result; more than one means this
// Exam/Year/Language has multiple question Sets (Set A / Set B / ...).
export function getPaperSets(examId, year, language, { signal } = {}) {
  return apiFetch(`/api/papers/sets?examId=${examId}&year=${year}&language=${language}`, { signal });
}

// GET /api/papers/{id} -- single paper's info (question count / duration / negative marking /
// IsComplete) for the "Paper Information" screen before Start Paper.
export function getPaper(id, { signal } = {}) {
  return apiFetch(`/api/papers/${id}`, { signal });
}

// POST /api/papers/{id}/start -- begin (or resume) a Previous Year Paper Practice attempt. Returns
// the same TestAttemptStartResponseDto shape as startMockTest/startPracticeTest, so the result
// navigates straight into the existing /tests/attempt/:attemptId TestRunner.
export function startPaper(id) {
  return apiFetch(`/api/papers/${id}/start`, { method: "POST", auth: true });
}
