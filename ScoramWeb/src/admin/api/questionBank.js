import { apiFetch, apiFetchForm } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// ---------- Questions ----------

// GET /api/admin/question-bank?search=&subjectId=&topicId=&examId=&year=&includeInactive=&page=&pageSize=
export function listQuestionBankQuestions(token, params = {}) {
  return apiFetch(`/api/admin/question-bank${toQueryString(params)}`, { token });
}

export function getQuestionBankQuestionAdmin(token, id) {
  return apiFetch(`/api/admin/question-bank/${id}`, { token });
}

// POST /api/admin/question-bank -- returns 409 with { existingQuestionId, existingQuestionText }
// when a near-duplicate already exists and confirmCreateDespiteDuplicate wasn't set (section 13).
export function createQuestionBankQuestion(token, payload) {
  return apiFetch("/api/admin/question-bank", { method: "POST", token, body: payload });
}

export function updateQuestionBankQuestion(token, id, payload) {
  return apiFetch(`/api/admin/question-bank/${id}`, { method: "PUT", token, body: payload });
}

// Soft delete (sets IsActive = false) -- see QuestionBankAdminController.Delete.
export function deleteQuestionBankQuestion(token, id) {
  return apiFetch(`/api/admin/question-bank/${id}`, { method: "DELETE", token });
}

// POST /api/admin/question-bank/{id}/images (multipart) -- images: { questionImage?, optionAImage?,
// optionBImage?, optionCImage?, optionDImage?, explanationImage? } (File objects, only include the
// ones actually changing); removeFlags: { removeQuestionImage?, removeOptionAImage?, ... } (booleans,
// only include ones being explicitly cleared). Works identically for a hand-typed or bulk-imported
// question -- see QuestionBankAdminController.UpdateImages.
export function uploadQuestionBankImages(token, id, images = {}, removeFlags = {}) {
  const formData = new FormData();
  const fileKeys = { questionImage: "QuestionImage", optionAImage: "OptionAImage", optionBImage: "OptionBImage", optionCImage: "OptionCImage", optionDImage: "OptionDImage", explanationImage: "ExplanationImage" };
  const removeKeys = { removeQuestionImage: "RemoveQuestionImage", removeOptionAImage: "RemoveOptionAImage", removeOptionBImage: "RemoveOptionBImage", removeOptionCImage: "RemoveOptionCImage", removeOptionDImage: "RemoveOptionDImage", removeExplanationImage: "RemoveExplanationImage" };
  Object.entries(fileKeys).forEach(([jsKey, formKey]) => {
    if (images[jsKey]) formData.append(formKey, images[jsKey]);
  });
  Object.entries(removeKeys).forEach(([jsKey, formKey]) => {
    if (removeFlags[jsKey]) formData.append(formKey, "true");
  });
  return apiFetchForm(`/api/admin/question-bank/${id}/images`, { method: "POST", token, formData });
}

// ---------- Subjects ----------

export function listSubjects(token, includeInactive = true) {
  return apiFetch(`/api/admin/question-bank/subjects${toQueryString({ includeInactive })}`, { token });
}

export function createSubject(token, name) {
  return apiFetch("/api/admin/question-bank/subjects", { method: "POST", token, body: { name } });
}

export function toggleSubjectActive(token, id) {
  return apiFetch(`/api/admin/question-bank/subjects/${id}/toggle-active`, { method: "PATCH", token });
}

// ---------- Topics ----------

export function listTopics(token, { subjectId, includeInactive = true } = {}) {
  return apiFetch(`/api/admin/question-bank/topics${toQueryString({ subjectId, includeInactive })}`, { token });
}

export function createTopic(token, subjectId, name) {
  return apiFetch("/api/admin/question-bank/topics", { method: "POST", token, body: { subjectId, name } });
}

export function toggleTopicActive(token, id) {
  return apiFetch(`/api/admin/question-bank/topics/${id}/toggle-active`, { method: "PATCH", token });
}

// ---------- Dashboard ----------

// GET /api/admin/question-bank/stats
export function getQuestionBankStats(token) {
  return apiFetch("/api/admin/question-bank/stats", { token });
}
