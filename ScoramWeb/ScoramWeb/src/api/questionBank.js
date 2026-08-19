import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/question-bank/search?search=&subjectId=&topicId=&examId=&year=&page=&pageSize=
// Public -- no auth needed to search the Question Bank, same as regular question search.
export function searchQuestionBank({ search, subjectId, topicId, examId, year, page = 1, pageSize = 20 } = {}, opts = {}) {
  return apiFetch(`/api/question-bank/search${toQueryString({ search, subjectId, topicId, examId, year, page, pageSize })}`, opts);
}

// GET /api/question-bank/{id}
export function getQuestionBankQuestion(id) {
  return apiFetch(`/api/question-bank/${id}`);
}

// GET /api/question-bank/subjects -- active subjects only, for the filter dropdown
export function getQuestionBankSubjects() {
  return apiFetch("/api/question-bank/subjects");
}

// GET /api/question-bank/topics?subjectId=... -- Topic dropdown depends on the chosen Subject
export function getQuestionBankTopics(subjectId) {
  return apiFetch(`/api/question-bank/topics${toQueryString({ subjectId })}`);
}

// GET /api/question-bank/exams -- only exams actually used in the Question Bank
export function getQuestionBankExams() {
  return apiFetch("/api/question-bank/exams");
}

// GET /api/question-bank/years
export function getQuestionBankYears() {
  return apiFetch("/api/question-bank/years");
}
