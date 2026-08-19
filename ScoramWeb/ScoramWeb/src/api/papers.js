import { apiFetch } from "./client";

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
