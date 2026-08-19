import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

export function listPracticeTestTemplatesAdmin(token, params = {}) {
  return apiFetch(`/api/admin/practice-tests${toQueryString(params)}`, { token });
}

export function getPracticeTestTemplateAdmin(token, id) {
  return apiFetch(`/api/admin/practice-tests/${id}`, { token });
}

export function createPracticeTestTemplate(token, payload) {
  return apiFetch("/api/admin/practice-tests", { method: "POST", token, body: payload });
}

export function updatePracticeTestTemplate(token, id, payload) {
  return apiFetch(`/api/admin/practice-tests/${id}`, { method: "PUT", token, body: payload });
}

export function updatePracticeTestTemplateStatus(token, id, status) {
  return apiFetch(`/api/admin/practice-tests/${id}/status`, { method: "PATCH", token, body: { status } });
}

export function getPracticeTestTemplateAttempts(token, id, params = {}) {
  return apiFetch(`/api/admin/practice-tests/${id}/attempts${toQueryString(params)}`, { token });
}
