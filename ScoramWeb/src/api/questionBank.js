import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === "") return;
    // Arrays (multi-select filters) become repeated keys -- ?examIds=a&examIds=b -- which ASP.NET
    // Core model-binds straight into a List<T> parameter with no custom parsing needed. An empty
    // array is treated the same as "not provided" (falls through, matches everything), same as a
    // blank string above.
    if (Array.isArray(value)) {
      if (value.length === 0) return;
      value.forEach((v) => query.append(key, v));
    } else {
      query.set(key, value);
    }
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/question-bank/search?search=&subjectIds=&topicIds=&examIds=&years=&languages=&page=&pageSize=
// Public -- no auth needed to search the Question Bank, same as regular question search.
// Each filter accepts either a single value or an array of values (multi-select) -- e.g.
// examIds: examId (one exam) or examIds: [examId1, examId2] (either exam).
export function searchQuestionBank(
  { search, subjectIds, topicIds, examIds, years, languages, page = 1, pageSize = 20 } = {},
  opts = {}
) {
  return apiFetch(
    `/api/question-bank/search${toQueryString({ search, subjectIds, topicIds, examIds, years, languages, page, pageSize })}`,
    opts
  );
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
