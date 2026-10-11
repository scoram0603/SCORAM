import { apiFetch } from "./client";

// SCORAM Smart Progress -- see ScoramAPI/SMART_PROGRESS.md (metric definitions + endpoints) and
// DTOs/SmartProgressDTOs.cs (JSON shapes, camelCase). Every call is per-student (auth: true) and
// takes an optional { signal } so screens can abort a stale request when filters change.
// Nothing here (or anywhere in the UI) recomputes accuracy/status -- the server's fields are rendered as-is.

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === "" || value === false) return;
    query.set(key, value === true ? "true" : value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// Shared filters: examId (guid), kind (Mock|Practice|PreviousYearPaper|Quiz), days (1-365), myExamsOnly (bool)
function filterParams({ examId, kind, days, myExamsOnly } = {}) {
  return { examId, kind, days, myExamsOnly };
}

// ---- Progress dashboard ----

export function getProgressOverview(filters, { signal } = {}) {
  return apiFetch(`/api/progress/overview${toQueryString(filterParams(filters))}`, { auth: true, signal });
}

export function getProgressSubjects(filters, { includeTopics = true, signal } = {}) {
  return apiFetch(`/api/progress/subjects${toQueryString({ ...filterParams(filters), includeTopics })}`, { auth: true, signal });
}

// Use subject/topic = "Unclassified" for the unclassified bucket.
export function getTopicDetail({ subject, topic, ...filters }, { signal } = {}) {
  return apiFetch(`/api/progress/topics/detail${toQueryString({ subject, topic, ...filterParams(filters) })}`, { auth: true, signal });
}

export function getProgressTrends(filters, { signal } = {}) {
  return apiFetch(`/api/progress/trends${toQueryString(filterParams(filters))}`, { auth: true, signal });
}

// ---- Per-assessment analytics ----

export function getAttemptAnalytics(attemptId, { signal } = {}) {
  return apiFetch(`/api/progress/assessments/${attemptId}/analytics`, { auth: true, signal });
}

// PATCH /api/tests/attempts/{attemptId}/timings -- body { timings: [{ studentAnswerId, timeSpentSeconds }] }
// (running totals, not deltas). Only accepted while the attempt is InProgress. Fire-and-forget callers.
export function patchAttemptTimings(attemptId, timings) {
  return apiFetch(`/api/tests/attempts/${attemptId}/timings`, { method: "PATCH", auth: true, body: { timings } });
}

// ---- Mistake Book / revision ----

// params: filters + subject, topic, search, status(Open|New|Learning|Corrected|Dismissed|All),
// type(Incorrect|Skipped|Repeated|Marked|All), dueOnly, sort(recent|oldest|mostWrong), page, pageSize(<=50)
export function getMistakes(params, { signal } = {}) {
  return apiFetch(`/api/revision/mistakes${toQueryString(params)}`, { auth: true, signal });
}

export function getRevisionSummary({ signal } = {}) {
  return apiFetch("/api/revision/mistakes/summary", { auth: true, signal });
}

export function getMistakeDetail(questionRef, { signal } = {}) {
  return apiFetch(`/api/revision/mistakes/detail${toQueryString({ ref: questionRef })}`, { auth: true, signal });
}

export function getRevisionSession({ count, subject, topic, dueOnly } = {}, { signal } = {}) {
  return apiFetch(`/api/revision/session${toQueryString({ count, subject, topic, dueOnly })}`, { auth: true, signal });
}

// body: { questionRef, selectedOption, timeSpentSeconds?, clientRequestId? } -- the ONLY place the
// correct option / explanation is revealed during revision.
export function postRevisionAnswer(body, { signal } = {}) {
  return apiFetch("/api/revision/answers", { method: "POST", auth: true, body, signal });
}

// PUT /api/revision/mistakes/dismiss?ref= -- body { dismissed } -- 204. dismissed:false restores.
export function setMistakeDismissed(questionRef, dismissed) {
  return apiFetch(`/api/revision/mistakes/dismiss${toQueryString({ ref: questionRef })}`, {
    method: "PUT",
    auth: true,
    body: { dismissed },
  });
}

// ---- Study plan ----

export function getTodayStudyPlan({ signal } = {}) {
  return apiFetch("/api/study-plan/today", { auth: true, signal });
}

export function getStudyPlanHistory({ days = 14, signal } = {}) {
  return apiFetch(`/api/study-plan/history${toQueryString({ days })}`, { auth: true, signal });
}

// PATCH /api/study-plan/tasks/{id} -- body { dismissed } -- dismiss / restore only. Completion is
// derived by the server from real activity; there is deliberately no "complete" call.
export function updateStudyTask(taskId, dismissed) {
  return apiFetch(`/api/study-plan/tasks/${taskId}`, { method: "PATCH", auth: true, body: { dismissed } });
}
