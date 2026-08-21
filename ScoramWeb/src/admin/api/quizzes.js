import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

export function listQuizzesAdmin(token, params = {}) {
  return apiFetch(`/api/admin/quizzes${toQueryString(params)}`, { token });
}

export function getQuizAdmin(token, id) {
  return apiFetch(`/api/admin/quizzes/${id}`, { token });
}

export function createQuiz(token, payload) {
  return apiFetch("/api/admin/quizzes", { method: "POST", token, body: payload });
}

export function updateQuiz(token, id, payload) {
  return apiFetch(`/api/admin/quizzes/${id}`, { method: "PUT", token, body: payload });
}

export function updateQuizStatus(token, id, status) {
  return apiFetch(`/api/admin/quizzes/${id}/status`, { method: "PATCH", token, body: { status } });
}

// questionBankQuestionIds: [guid, ...]
export function addQuizQuestions(token, id, questionBankQuestionIds) {
  return apiFetch(`/api/admin/quizzes/${id}/questions`, { method: "POST", token, body: { questionBankQuestionIds } });
}

export function removeQuizQuestion(token, id, quizQuestionId) {
  return apiFetch(`/api/admin/quizzes/${id}/questions/${quizQuestionId}`, { method: "DELETE", token });
}

export function duplicateQuiz(token, id) {
  return apiFetch(`/api/admin/quizzes/${id}/duplicate`, { method: "POST", token });
}

// orderedQuizQuestionIds: [guid, ...] in the desired final order
export function reorderQuizQuestions(token, id, orderedQuizQuestionIds) {
  return apiFetch(`/api/admin/quizzes/${id}/questions/reorder`, { method: "PUT", token, body: orderedQuizQuestionIds });
}

export function getQuizAttempts(token, id, params = {}) {
  return apiFetch(`/api/admin/quizzes/${id}/attempts${toQueryString(params)}`, { token });
}

export function getQuizResultsSummary(token, id) {
  return apiFetch(`/api/admin/quizzes/${id}/results`, { token });
}
