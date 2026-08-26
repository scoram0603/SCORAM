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

// GET /api/admin/bulk-import/{jobId}/questions -- the real Question rows a *committed* import
// created, for expanding a "Recent imports" history entry to review/edit that specific batch.
export function getImportJobQuestions(token, jobId) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/questions`, { token });
}

// POST /api/admin/bulk-import/{jobId}/rollback -- only works while the paper is still Draft
export function rollbackImport(token, jobId) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/rollback`, { method: "POST", token });
}
