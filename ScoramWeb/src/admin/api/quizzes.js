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
