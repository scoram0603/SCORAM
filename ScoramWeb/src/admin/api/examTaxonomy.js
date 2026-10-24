import { apiFetch } from "../../api/client";

// Exam-wise Subject & Topic Management (admin) -- api/admin/exam-taxonomy/*.
// Subjects/topics here are PER EXAM: renaming/deleting one only affects the selected exam, and the same
// question can sit under different subjects/topics in different exams. Every call is permission-checked
// server-side (ManageSubjects; question mapping also accepts ManageQuestionBank).
//
// Failed calls throw ApiError; err.data is the server's { message, code, data } body, so callers can branch on
// err.data.code (DUPLICATE_SUBJECT, SUBJECT_IN_USE, TOPIC_IN_USE, TAXONOMY_NOT_INITIALIZED, IMPACT_CHANGED, ...).

function qs(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "" && value !== false) query.set(key, value);
  });
  const s = query.toString();
  return s ? `?${s}` : "";
}

const base = "/api/admin/exam-taxonomy";

export const listTaxonomyExams = (token, search) => apiFetch(`${base}/exams${qs({ search })}`, { token });
// -> { exam, initialized, totalQuestions, unassignedQuestions, subjects: [...] }
export const getExamTaxonomy = (token, examId, params = {}) => apiFetch(`${base}/${examId}${qs(params)}`, { token });

export const previewInitialize = (token, examId) => apiFetch(`${base}/${examId}/initialize/preview`, { token });
export const initializeExam = (token, examId, payload) => apiFetch(`${base}/${examId}/initialize`, { method: "POST", token, body: payload });

// subjects
export const createExamSubject = (token, examId, payload) => apiFetch(`${base}/${examId}/subjects`, { method: "POST", token, body: payload });
export const renameExamSubject = (token, examId, id, payload) => apiFetch(`${base}/${examId}/subjects/${id}`, { method: "PUT", token, body: payload });
export const setExamSubjectActive = (token, examId, id, payload) => apiFetch(`${base}/${examId}/subjects/${id}/active`, { method: "PATCH", token, body: payload });
export const getExamSubjectUsage = (token, examId, id) => apiFetch(`${base}/${examId}/subjects/${id}/usage`, { token });
export const previewExamSubjectDelete = (token, examId, id) => apiFetch(`${base}/${examId}/subjects/${id}/delete-preview`, { token });
// mode: "delete" (only when unused) | "unassign" (clear its questions' classification first, never delete them)
export const deleteExamSubject = (token, examId, id, { mode, confirmName, expectedQuestions }) =>
  apiFetch(`${base}/${examId}/subjects/${id}${qs({ mode, confirm: true, confirmName, expectedQuestions })}`, { method: "DELETE", token });

// topics
export const listExamTopics = (token, examId, subjectId, params = {}) => apiFetch(`${base}/${examId}/subjects/${subjectId}/topics${qs(params)}`, { token });
export const createExamTopic = (token, examId, subjectId, payload) => apiFetch(`${base}/${examId}/subjects/${subjectId}/topics`, { method: "POST", token, body: payload });
export const renameExamTopic = (token, examId, id, payload) => apiFetch(`${base}/${examId}/topics/${id}`, { method: "PUT", token, body: payload });
export const setExamTopicActive = (token, examId, id, payload) => apiFetch(`${base}/${examId}/topics/${id}/active`, { method: "PATCH", token, body: payload });
export const getExamTopicUsage = (token, examId, id) => apiFetch(`${base}/${examId}/topics/${id}/usage`, { token });
export const previewExamTopicDelete = (token, examId, id) => apiFetch(`${base}/${examId}/topics/${id}/delete-preview`, { token });
export const deleteExamTopic = (token, examId, id, { mode, confirmName, expectedQuestions }) =>
  apiFetch(`${base}/${examId}/topics/${id}${qs({ mode, confirm: true, confirmName, expectedQuestions })}`, { method: "DELETE", token });

// questions
// params: { examSubjectId, examTopicId, unassigned, search, page, pageSize }
export const listExamQuestions = (token, examId, params = {}) => apiFetch(`${base}/${examId}/questions${qs(params)}`, { token });
export const mapExamQuestions = (token, examId, payload) => apiFetch(`${base}/${examId}/questions/map`, { method: "POST", token, body: payload });
export const unmapExamQuestions = (token, examId, payload) => apiFetch(`${base}/${examId}/questions/unmap`, { method: "POST", token, body: payload });
export const getQuestionClassifications = (token, questionId) => apiFetch(`${base}/question/${questionId}/classifications`, { token });

// Merge -- offered when a rename collides with an existing active name in the same exam.
export const previewMergeExamSubject = (token, examId, id, targetId) => apiFetch(`${base}/${examId}/subjects/${id}/merge-preview${qs({ targetId })}`, { token });
export const mergeExamSubject = (token, examId, id, payload) => apiFetch(`${base}/${examId}/subjects/${id}/merge`, { method: "POST", token, body: payload });
export const previewMergeExamTopic = (token, examId, id, targetId) => apiFetch(`${base}/${examId}/topics/${id}/merge-preview${qs({ targetId })}`, { token });
export const mergeExamTopic = (token, examId, id, payload) => apiFetch(`${base}/${examId}/topics/${id}/merge`, { method: "POST", token, body: payload });

// Every exam-specific subject across all exams (read-only) -- shown inside Admin > Subjects.
export const listAllExamSubjects = (token, search) => apiFetch(`${base}/all-subjects${qs({ search })}`, { token });
