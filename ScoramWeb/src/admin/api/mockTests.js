import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

export function listMockTestsAdmin(token, params = {}) {
  return apiFetch(`/api/admin/mocktests${toQueryString(params)}`, { token });
}

export function getMockTestAdmin(token, id) {
  return apiFetch(`/api/admin/mocktests/${id}`, { token });
}

export function createMockTest(token, payload) {
  return apiFetch("/api/mocktests", { method: "POST", token, body: payload });
}

export function updateMockTest(token, id, payload) {
  return apiFetch(`/api/admin/mocktests/${id}`, { method: "PUT", token, body: payload });
}

export function updateMockTestStatus(token, id, status) {
  return apiFetch(`/api/admin/mocktests/${id}/status`, { method: "PATCH", token, body: { status } });
}

export function duplicateMockTest(token, id) {
  return apiFetch(`/api/admin/mocktests/${id}/duplicate`, { method: "POST", token });
}

// refs: [{ questionId?, questionBankQuestionId? }]
export function addMockTestQuestions(token, id, refs) {
  return apiFetch(`/api/admin/mocktests/${id}/questions`, { method: "POST", token, body: refs });
}

export function removeMockTestQuestion(token, id, mockTestQuestionId) {
  return apiFetch(`/api/admin/mocktests/${id}/questions/${mockTestQuestionId}`, { method: "DELETE", token });
}

export function reorderMockTestQuestions(token, id, orderedQuestionIds) {
  return apiFetch(`/api/admin/mocktests/${id}/questions/reorder`, { method: "PUT", token, body: orderedQuestionIds });
}

export function getMockTestAttempts(token, id, params = {}) {
  return apiFetch(`/api/admin/mocktests/${id}/attempts${toQueryString(params)}`, { token });
}

export function getMockTestResultsSummary(token, id) {
  return apiFetch(`/api/admin/mocktests/${id}/results`, { token });
}
