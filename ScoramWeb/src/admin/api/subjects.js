import { apiFetch } from "../../api/client";

// Subject Management (admin > Subjects) -- api/admin/subjects/*. Every call here is checked
// server-side for the ManageSubjects permission; the sidebar/route guards are only convenience.
//
// Failed calls throw ApiError; err.data is the server's { message, code, data } body, so callers
// can branch on err.data.code (DUPLICATE_SUBJECT, SUBJECT_IN_USE, IMPACT_CHANGED,
// CONCURRENT_MODIFICATION, ...) and read err.data.data (e.g. usage counts) when present.

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET ?search=&status=all|active|inactive&sortBy=&sortDir=&page=&pageSize=
export function listManagedSubjects(token, params = {}) {
  return apiFetch(`/api/admin/subjects${toQueryString(params)}`, { token });
}

// Subject + full usage breakdown + its topics.
export function getManagedSubject(token, id) {
  return apiFetch(`/api/admin/subjects/${id}`, { token });
}

export function createManagedSubject(token, payload) {
  return apiFetch("/api/admin/subjects", { method: "POST", token, body: payload });
}

// Rename. payload: { name, version, confirm }
export function renameManagedSubject(token, id, payload) {
  return apiFetch(`/api/admin/subjects/${id}`, { method: "PUT", token, body: payload });
}

// payload: { isActive, version }
export function setManagedSubjectActive(token, id, payload) {
  return apiFetch(`/api/admin/subjects/${id}/active`, { method: "PATCH", token, body: payload });
}

// payload: { sourceIds: [], targetId }
export function previewSubjectMerge(token, payload) {
  return apiFetch("/api/admin/subjects/merge/preview", { method: "POST", token, body: payload });
}

// payload: { sourceIds, targetId, confirm, confirmName, expectedTotalAffected }
export function mergeSubjects(token, payload) {
  return apiFetch("/api/admin/subjects/merge", { method: "POST", token, body: payload });
}

// payload: { sourceId, targetId }
export function previewSubjectReassign(token, payload) {
  return apiFetch("/api/admin/subjects/reassign/preview", { method: "POST", token, body: payload });
}

// payload: { sourceId, targetId, confirm, expectedTotalAffected }
export function reassignSubjectContent(token, payload) {
  return apiFetch("/api/admin/subjects/reassign", { method: "POST", token, body: payload });
}

export function previewSubjectDelete(token, id) {
  return apiFetch(`/api/admin/subjects/${id}/delete-preview`, { token });
}

export function deleteManagedSubject(token, id, confirmName) {
  return apiFetch(`/api/admin/subjects/${id}${toQueryString({ confirm: true, confirmName })}`, { method: "DELETE", token });
}
