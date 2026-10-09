import { apiFetch, apiFetchForm } from "../../api/client";

function qs(params = {}) {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (Array.isArray(v)) v.forEach((x) => q.append(k, x));
    else if (v !== undefined && v !== null && v !== "") q.set(k, v);
  });
  const s = q.toString();
  return s ? `?${s}` : "";
}

// ---- Shared stimuli -- /api/admin/shared-stimuli (list returns { items, total, page, pageSize }) ----
export const listStimuli = (token, p) => apiFetch(`/api/admin/shared-stimuli${qs(p)}`, { token });
export const getStimulus = (token, id) => apiFetch(`/api/admin/shared-stimuli/${id}`, { token });
export const createStimulus = (token, body) => apiFetch("/api/admin/shared-stimuli", { method: "POST", token, body });
export const updateStimulus = (token, id, body) => apiFetch(`/api/admin/shared-stimuli/${id}`, { method: "PUT", token, body });
export const archiveStimulus = (token, id) => apiFetch(`/api/admin/shared-stimuli/${id}/archive`, { method: "POST", token });
export const restoreStimulus = (token, id) => apiFetch(`/api/admin/shared-stimuli/${id}/restore`, { method: "POST", token });
export const getStimulusQuestions = (token, id) => apiFetch(`/api/admin/shared-stimuli/${id}/questions`, { token });

function uploadTo(path) {
  return (token, file) => {
    const formData = new FormData();
    formData.append("file", file);
    return apiFetchForm(path, { formData, token }); // -> { url }
  };
}
export const uploadStimulusImage = uploadTo("/api/admin/shared-stimuli/upload-image");

// ---- Paper <-> stimulus links ----
export const listStimulusLinks = (token, paperId) => apiFetch(`/api/admin/papers/${paperId}/stimulus-links`, { token });
export const attachStimulus = (token, paperId, stimulusId, targets) =>
  apiFetch(`/api/admin/papers/${paperId}/stimulus-links/attach`, { method: "POST", token, body: { stimulusId, targets } });
export const detachStimulus = (token, paperId, stimulusId, targets) =>
  apiFetch(`/api/admin/papers/${paperId}/stimulus-links/detach`, { method: "POST", token, body: { stimulusId, targets } });
export const replaceStimulus = (token, paperId, fromStimulusId, toStimulusId, targets) =>
  apiFetch(`/api/admin/papers/${paperId}/stimulus-links/replace`, { method: "POST", token, body: { fromStimulusId, toStimulusId, targets } });

// ---- Paper instructions ----
const ins = (paperId) => `/api/admin/papers/${paperId}/instructions`;
export const listInstructions = (token, paperId, includeArchived = true) => apiFetch(`${ins(paperId)}${qs({ includeArchived })}`, { token });
export const createInstruction = (token, paperId, body) => apiFetch(ins(paperId), { method: "POST", token, body });
export const updateInstruction = (token, paperId, id, body) => apiFetch(`${ins(paperId)}/${id}`, { method: "PUT", token, body });
export const archiveInstruction = (token, paperId, id) => apiFetch(`${ins(paperId)}/${id}/archive`, { method: "POST", token });
export const restoreInstruction = (token, paperId, id) => apiFetch(`${ins(paperId)}/${id}/restore`, { method: "POST", token });
export const reorderInstructions = (token, paperId, orderedIds) => apiFetch(`${ins(paperId)}/reorder`, { method: "PUT", token, body: { orderedIds } });
export const deleteInstruction = (token, paperId, id) => apiFetch(`${ins(paperId)}/${id}`, { method: "DELETE", token });

// ---- What a student will see (instructions, stimuli, questions in order) ----
export const getContentPreview = (token, paperId) => apiFetch(`/api/admin/papers/${paperId}/content-preview`, { token });

// ---- Bulk question editor (papers/questions lists return { items, totalCount, page, pageSize }) ----
const bq = "/api/admin/bulk-question-editor";
export const bulkListPapers = (token, p) => apiFetch(`${bq}/papers${qs(p)}`, { token });
export const bulkListQuestions = (token, p) => apiFetch(`${bq}/questions${qs(p)}`, { token });
export const bulkFilterOptions = (token, paperIds) => apiFetch(`${bq}/filter-options${qs({ paperIds })}`, { token });
export const bulkPreview = (token, body) => apiFetch(`${bq}/preview`, { method: "POST", token, body });
export const bulkApply = (token, body) => apiFetch(`${bq}/apply`, { method: "POST", token, body });
export const uploadBulkImage = uploadTo(`${bq}/upload-image`);
