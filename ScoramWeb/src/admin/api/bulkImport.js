import { apiFetch, apiFetchForm } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/papers/{paperId}/bulk-import/preview -- parses + validates, writes nothing to
// Questions yet. Returns { jobId, fileName, format, totalRows, validCount, invalidCount, rows }.
export function previewBulkImport(token, paperId, file) {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetchForm(`/api/admin/papers/${paperId}/bulk-import/preview`, { formData, token });
}

// POST /api/admin/bulk-import/{jobId}/commit -- rowNumbers omitted = commit every valid row;
// pass a subset for a partial import.
export function commitBulkImport(token, jobId, rowNumbers) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/commit`, {
    method: "POST",
    token,
    body: { rowNumbers: rowNumbers || null },
  });
}

// GET /api/admin/bulk-import/history?paperId=&page=&pageSize=
export function getImportHistory(token, { paperId, page, pageSize } = {}) {
  return apiFetch(`/api/admin/bulk-import/history${toQueryString({ paperId, page, pageSize })}`, { token });
}

// PATCH /api/admin/bulk-import/{jobId}/rows/{rowNumber} -- corrects a row's fields during review,
// before commit. Returns the updated row with fresh isValid/errors from server-side re-validation.
export function updatePreviewRow(token, jobId, rowNumber, row) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/rows/${rowNumber}`, {
    method: "PATCH",
    token,
    body: {
      rowNumber,
      questionNumber: row.questionNumber,
      subject: row.subject,
      topic: row.topic,
      difficultyLevel: row.difficultyLevel,
      questionText: row.questionText,
      optionA: row.optionA,
      optionB: row.optionB,
      optionC: row.optionC,
      optionD: row.optionD,
      correctOption: row.correctOption,
      explanation: row.explanation,
      sourceReference: row.sourceReference,
    },
  });
}

// POST /api/admin/bulk-import/{jobId}/rows/{rowNumber}/images -- adds, replaces, or removes one or
// more of this row's images during preview, before commit. Works for a row from any format, not
// just one that already came from a ZIP -- this is how a CSV/Excel/JSON row gets its first image.
// `images` is { questionImage, optionAImage, optionBImage, optionCImage, optionDImage,
// explanationImage } (File objects, only the ones being added/replaced); `removeImages` is the
// same keys as booleans, for clearing an image without replacing it.
export function updateRowImages(token, jobId, rowNumber, images = {}, removeImages = {}) {
  const formData = new FormData();
  if (images.questionImage) formData.append("QuestionImage", images.questionImage);
  if (images.optionAImage) formData.append("OptionAImage", images.optionAImage);
  if (images.optionBImage) formData.append("OptionBImage", images.optionBImage);
  if (images.optionCImage) formData.append("OptionCImage", images.optionCImage);
  if (images.optionDImage) formData.append("OptionDImage", images.optionDImage);
  if (images.explanationImage) formData.append("ExplanationImage", images.explanationImage);
  formData.append("RemoveQuestionImage", removeImages.questionImage ? "true" : "false");
  formData.append("RemoveOptionAImage", removeImages.optionAImage ? "true" : "false");
  formData.append("RemoveOptionBImage", removeImages.optionBImage ? "true" : "false");
  formData.append("RemoveOptionCImage", removeImages.optionCImage ? "true" : "false");
  formData.append("RemoveOptionDImage", removeImages.optionDImage ? "true" : "false");
  formData.append("RemoveExplanationImage", removeImages.explanationImage ? "true" : "false");
  return apiFetchForm(`/api/admin/bulk-import/${jobId}/rows/${rowNumber}/images`, { formData, token });
}

// GET /api/admin/bulk-import/{jobId}/questions -- the real Question rows a *committed* import
// created, for expanding a "Recent imports" history entry to review/edit that specific batch.
export function getImportJobQuestions(token, jobId) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/questions`, { token });
}

// POST /api/admin/bulk-import/{jobId}/rollback -- works regardless of the paper's status now
// (Draft/PendingReview/Published), unless students have already attempted the paper (409). Returns
// { jobId, questionsRemoved, paperStatus, examCleanupCandidateId }. examCleanupCandidateId is set
// only when the paper's exam was created solely for this paper AND this rollback left it with zero
// questions -- BulkImportPanel shows a confirm dialog and calls cleanupEmptyExam() before deleting it,
// this endpoint never deletes anything by itself.
export function rollbackImport(token, jobId) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/rollback`, { method: "POST", token });
}
