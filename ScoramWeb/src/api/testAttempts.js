import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/tests/attempts/{attemptId} -- InProgress -> same shape as start (resume);
// Submitted/AutoSubmitted -> full graded result. The TestRunner component checks which shape came
// back rather than the caller having to know in advance.
export function getAttempt(attemptId, opts = {}) {
  return apiFetch(`/api/tests/attempts/${attemptId}`, { ...opts, auth: true });
}

// PATCH /api/tests/attempts/answers/{studentAnswerId} -- auto-save. selectedOption: null explicitly
// clears the answer ("Clear Response") -- always pass it, even as null, don't omit the key.
export function saveAnswer(studentAnswerId, { selectedOption, isMarkedForReview } = {}) {
  return apiFetch(`/api/tests/attempts/answers/${studentAnswerId}`, {
    method: "PATCH",
    auth: true,
    body: { selectedOption: selectedOption ?? null, isMarkedForReview },
  });
}

// POST /api/tests/attempts/{attemptId}/submit -- idempotent; calling it again on an
// already-submitted attempt just returns the existing result instead of erroring.
export function submitTestAttempt(attemptId, timeTakenSeconds) {
  return apiFetch(`/api/tests/attempts/${attemptId}/submit`, {
    method: "POST",
    auth: true,
    body: { timeTakenSeconds },
  });
}

// GET /api/tests/attempts/mine?status=&page=&pageSize= -- "My Tests": Practice + Mock mixed
export function getMyTestAttempts(params = {}, opts = {}) {
  return apiFetch(`/api/tests/attempts/mine${toQueryString(params)}`, { ...opts, auth: true });
}
