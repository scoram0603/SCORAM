import { apiFetch, apiFetchForm, API_BASE_URL } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/question-bank/bulk/excel or /bulk/json -- parses + validates, writes nothing yet.
// Returns { jobId, fileName, format, totalRows, validCount, invalidCount, duplicateCount, rows }.
// "language" (optional -- "Hindi" | "English") is the admin's Default Language pick for this whole
// upload; see QuestionBankAdminController.Preview's own comment for exactly how it's applied.
export function previewQuestionBankImport(token, file, format, language) {
  const formData = new FormData();
  formData.append("file", file);
  if (language) formData.append("language", language);
  const path = format === "json" ? "/api/admin/question-bank/bulk/json" : "/api/admin/question-bank/bulk/excel";
  return apiFetchForm(path, { formData, token });
}

// POST /api/admin/question-bank/bulk/{jobId}/commit -- rowNumbers omitted = commit every valid row
// (new questions + duplicates merged into their existing question's exam/year mappings).
export function commitQuestionBankImport(token, jobId, rowNumbers) {
  return apiFetch(`/api/admin/question-bank/bulk/${jobId}/commit`, {
    method: "POST",
    token,
    body: { rowNumbers: rowNumbers || null },
  });
}

// PATCH /api/admin/question-bank/bulk/{jobId}/rows/{rowNumber} -- corrects a row's fields during
// review, before commit. Returns the updated row with fresh isValid/errors/duplicate info from
// server-side re-validation.
export function updatePreviewRow(token, jobId, rowNumber, row) {
  return apiFetch(`/api/admin/question-bank/bulk/${jobId}/rows/${rowNumber}`, {
    method: "PATCH",
    token,
    body: {
      rowNumber,
      questionText: row.questionText,
      optionA: row.optionA,
      optionB: row.optionB,
      optionC: row.optionC,
      optionD: row.optionD,
      correctOption: row.correctOption,
      explanation: row.explanation,
      subject: row.subject,
      topic: row.topic,
      sourceReference: row.sourceReference,
      language: row.language,
      rawExamYears: row.rawExamYears,
    },
  });
}

export function getQuestionBankImportStatus(token, jobId) {
  return apiFetch(`/api/admin/question-bank/bulk/${jobId}`, { token });
}

// GET /api/admin/question-bank/bulk/history?page=&pageSize=
export function getQuestionBankImportHistory(token, { page, pageSize } = {}) {
  return apiFetch(`/api/admin/question-bank/bulk/history${toQueryString({ page, pageSize })}`, { token });
}

// Template downloads are generated server-side so they're always in sync with what the importer
// actually parses (see QuestionBankAdminController.DownloadExcelTemplate/DownloadJsonTemplate).
// These return a raw file, not JSON, so they go through fetch()+Blob directly rather than apiFetch.
async function downloadFile(token, path, fallbackFileName) {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!response.ok) throw new Error(`Couldn't download the template (status ${response.status}).`);

  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition") || "";
  const match = disposition.match(/filename="?([^"]+)"?/);
  const fileName = match?.[1] || fallbackFileName;

  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function downloadExcelTemplate(token) {
  return downloadFile(token, "/api/admin/question-bank/template/excel", "question-bank-template.xlsx");
}

export function downloadJsonTemplate(token) {
  return downloadFile(token, "/api/admin/question-bank/template/json", "question-bank-template.json");
}
