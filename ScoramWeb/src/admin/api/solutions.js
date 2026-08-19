import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/questions/{questionId}/solutions -- Official/Teacher solutions authored by staff,
// auto-approved. (ModerateSolutions permission required server-side)
export function createSolutionAsAdmin(token, questionId, { title, solutionType, solutionText, imageUrl }) {
  return apiFetch(`/api/admin/questions/${questionId}/solutions`, {
    method: "POST",
    token,
    body: { title, solutionType, solutionText, imageUrl: imageUrl || null },
  });
}

// GET /api/admin/solutions/pending?page=&pageSize=  (ModerateSolutions permission required server-side)
export function listPendingSolutions(token, { page, pageSize } = {}) {
  return apiFetch(`/api/admin/solutions/pending${toQueryString({ page, pageSize })}`, { token });
}

// PATCH /api/admin/solutions/{id}/approve
export function approveSolution(token, id) {
  return apiFetch(`/api/admin/solutions/${id}/approve`, { method: "PATCH", token });
}

// DELETE /api/admin/solutions/{id}?reason= -- covers both "reject a pending submission" and
// removing something already approved that turned out to be wrong/inappropriate.
export function removeSolution(token, id, reason) {
  return apiFetch(`/api/admin/solutions/${id}${toQueryString({ reason })}`, { method: "DELETE", token });
}

// PATCH /api/admin/solutions/{id}/priority
export function setSolutionPriority(token, id, priority) {
  return apiFetch(`/api/admin/solutions/${id}/priority`, { method: "PATCH", token, body: priority });
}
