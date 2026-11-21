import { apiFetch } from "./client";
import { catalogKey, loadCatalog } from "./catalogCache";

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
  { search, subjectIds, topicIds, examSubjectIds, examTopicIds, examIds, years, languages, page = 1, pageSize = 20 } = {},
  opts = {}
) {
  return apiFetch(
    `/api/question-bank/search${toQueryString({ search, subjectIds, topicIds, examSubjectIds, examTopicIds, examIds, years, languages, page, pageSize })}`,
    { ...opts, optionalAuth: true } // sends the student token so the API can apply My Exams
  );
}

// GET /api/question-bank/{id}
export function getQuestionBankQuestion(id) {
  return apiFetch(`/api/question-bank/${id}`);
}

// GET /api/question-bank/subjects[?examId=] -- active subjects only, for the filter dropdown.
// With examId, an exam that has its own (exam-wise) subject list returns THAT list: each item then has
// isExamSpecific=true and its id is an EXAM subject id (send it as examSubjectIds to searchQuestionBank).
// Without examId -- or for an exam with no exam-wise list -- it is the global list, exactly as before.
export function getQuestionBankSubjects(examId) {
  return apiFetch(`/api/question-bank/subjects${toQueryString({ examId })}`, { optionalAuth: true });
}

// GET /api/question-bank/topics?subjectId=...[&examId=] -- Topic dropdown depends on the chosen Subject.
// When examId is sent for an exam-wise exam, subjectId is the EXAM subject id and the topic ids are EXAM topic ids.
export function getQuestionBankTopics(subjectId, examId) {
  return apiFetch(`/api/question-bank/topics${toQueryString({ subjectId, examId })}`, { optionalAuth: true });
}

// GET /api/question-bank/exams -- only exams actually used in the Question Bank
export function getQuestionBankExams() {
  return apiFetch("/api/question-bank/exams", { optionalAuth: true });
}

// GET /api/question-bank/years
export function getQuestionBankYears() {
  return apiFetch("/api/question-bank/years", { optionalAuth: true });
}

// ---- Shared catalog cache (see catalogCache.js) ----
// Keys for the lookups the Question Bank and Practice Tests screens both need. They are user-scoped:
// the server filters these lists by the signed-in student's My Exams. Call them at render time so the
// key always carries the current student; pair them with useCatalog(key, fetcher).
export const qbCatalogKeys = {
  exams: () => catalogKey("qb:exams"),
  years: () => catalogKey("qb:years"),
  subjects: (examId) => catalogKey("qb:subjects", examId ?? ""),
  topics: (subjectId, examId) => catalogKey("qb:topics", `${subjectId}|${examId ?? ""}`),
  // The global (unfiltered) question total behind the "Questions" stat card.
  total: () => catalogKey("qb:total"),
};

// Cached, de-duplicated topic list for one subject (used by effects that merge several subjects).
export function loadQuestionBankTopics(subjectId, examId) {
  return loadCatalog(qbCatalogKeys.topics(subjectId, examId), () => getQuestionBankTopics(subjectId, examId));
}

// The unfiltered total: one request with a single-row page, only used when no unfiltered page of
// results has already told us the total.
export function fetchQuestionBankTotal() {
  return searchQuestionBank({ page: 1, pageSize: 1 }).then((d) => d.totalCount);
}
